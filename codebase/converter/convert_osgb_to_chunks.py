#!/usr/bin/env python3
"""
Stage A: OSGB → Chunked GLB Converter

將一個香港 CSDI 3D 圖幅(.osgb,PagedLOD 四元樹)轉換為空間分割的 .glb 分塊,
供 Unity 執行時逐塊串流載入。

設計:
  - 輸入:單一 .osgb.zip(CSDI 下載)或已解壓的目錄。
  - CSDI OSGB 為 PagedLOD 四元樹,頂層有 N 個子圖幅(Tile_+X_+Y),
    每個子圖幅自有根 .osgb(最粗 LOD)+ 較細 LOD 子檔。
  - 分塊策略:每個子圖幅 = 一個 .glb 分塊(空間單元,便於鄰近載入)。
  - 每個子圖幅取根 LOD,以 osgconv 轉 OBJ(含紋理),
    再以 pymeshlab 的 meshing_decimation_quadric_edge_collapse_with_texture
    簡化至目標大小(保留 UV / 紋理貼圖),最後以 trimesh 重新置中並匯出 GLB。
  - 紋理:降解析度至 --max-texture-px(預設 512),保留 UV 對應。
  - 輸出:<output>/<chunk_id>.glb + manifest.json + 分塊大小報告。
  - 驗證:以 validate_chunks.py 做硬性大小閘道(供 CI gate)。

無寫死路徑;所有路徑經 CLI 參數或環境變數傳入。

用法:
  python convert_osgb_to_chunks.py \\
    --input /path/to/11-NW-19B_OSGB.zip \\
    --output /path/to/chunks_out \\
    --osgconv /path/to/osgconv \\
    [--max-chunk-mb 8] [--max-total-mb 200] [--max-texture-px 512] \\
    [--lod root] [--decimate-threshold 3000] [--verbose]

環境變數(可替代 CLI):
  OSGB_INPUT, OSGB_OUTPUT, OSGCONV_PATH, MAX_CHUNK_MB, MAX_TOTAL_MB, MAX_TEXTURE_PX
"""
from __future__ import annotations

import argparse
import json
import logging
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

# ────────────────────────── logging ──────────────────────────
log = logging.getLogger("converter")


def setup_logging(verbose: bool) -> None:
    logging.basicConfig(
        level=logging.DEBUG if verbose else logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s",
        datefmt="%H:%M:%S",
    )


# ────────────────────────── dataclasses ──────────────────────────
@dataclass
class SubTile:
    """一個子圖幅(分塊單元)。"""
    index: int                  # 1-based 子圖幅序號(目錄名尾碼)
    dir_name: str               # e.g. "11-NW-19B-1"
    tile_x: int                 # Tile_+X 的 X
    tile_y: int                 # Tile_+Y 的 Y
    root_osgb: Path             # 根 .osgb 路徑
    lod_files: list[Path] = field(default_factory=list)  # 可選的較細 LOD 檔


@dataclass
class ChunkRecord:
    """manifest.json 中每個分塊的記錄。"""
    id: str
    grid_x: int
    grid_y: int
    bbox_min: list[float]
    bbox_max: list[float]
    centroid: list[float]
    file: str
    bytes: int
    triangles: int
    neighbors: list[str]
    priority: int = 1


# ────────────────────────── Stage 1: 探索 OSGB 結構 ──────────────────────────
def discover_subtiles(input_path: Path, work_dir: Path, lod: str = "root") -> tuple[Path, list[SubTile]]:
    """
    輸入可能是 .zip 或已解壓目錄。回傳 (根目錄, 子圖幅清單)。
    預期結構:<root>/<SheetNo>-<N>/Tile_+X_+Y.osgb(+ LOD 子檔)

    lod 選擇:
      "root" — 最粗 LOD(檔名不含 _L)。最小,但建物無牆面(堆疊水平平板)。
      "l15"  — 第 15 層 LOD(檔名含 _L15_)。
      "l16"  — 第 16 層 LOD(檔名含 _L16_)。建物有真正牆面,視覺顯著改善。
      每個子圖幅的 root/l15/l16 皆為單一 .osgb 檔(L17+ 才分裂為四元樹多檔)。
    """
    if input_path.is_file() and input_path.suffix.lower() == ".zip":
        extract_dir = work_dir / "osgb_extract"
        extract_dir.mkdir(parents=True, exist_ok=True)
        log.info("解壓 OSGB zip → %s", extract_dir)
        with zipfile.ZipFile(input_path) as z:
            z.extractall(extract_dir)
        root = _find_sheet_root(extract_dir)
    elif input_path.is_dir():
        root = _find_sheet_root(input_path)
    else:
        raise ValueError(f"輸入必須是 .zip 或目錄: {input_path}")

    if root is None:
        raise ValueError(f"在 {input_path} 下找不到預期的 <SheetNo>-N/Tile_+X_+Y.osgb 結構")

    log.info("OSGB 根目錄: %s", root)

    # LOD 選擇:root = 檔名不含 _L;l15/l16 = 檔名含 _L15_/_L16_
    if lod == "root":
        lod_token = None  # 不含 _L
    else:
        lod_token = f"_L{lod[1:]}_"  # e.g. "_L16_"

    subtiles: list[SubTile] = []
    for d in sorted(root.iterdir()):
        if not d.is_dir():
            continue
        m = re.match(r"^(.+)-(\d+)$", d.name)
        if not m:
            continue
        idx = int(m.group(2))
        if lod_token is None:
            # root: 檔名不含 _L
            roots = sorted([f for f in d.iterdir()
                            if f.suffix.lower() == ".osgb" and "_L" not in f.name])
        else:
            # l15/l16: 檔名含 _L<N>_
            roots = sorted([f for f in d.iterdir()
                            if f.suffix.lower() == ".osgb" and lod_token in f.name])
        if not roots:
            log.warning("子圖幅 %s 找不到 LOD=%s 的 .osgb(跳過)", d.name, lod)
            continue
        # 座標從檔名解析(root 與 L16 命名一致:Tile_+X_+Y[_L16_NN].osgb)
        rm = re.match(r"Tile_\+(-?\d+)_\+(-?\d+)", roots[0].name)
        if not rm:
            log.warning("無法解析子圖幅座標: %s", roots[0].name)
            continue
        tile_x, tile_y = int(rm.group(1)), int(rm.group(2))
        # 收集所有 LOD 子檔(供參考)
        lod_files = sorted([f for f in d.iterdir()
                            if f.suffix.lower() == ".osgb" and "_L" in f.name])
        subtiles.append(SubTile(
            index=idx, dir_name=d.name, tile_x=tile_x, tile_y=tile_y,
            root_osgb=roots[0], lod_files=lod_files,
        ))

    if not subtiles:
        raise ValueError(f"在 {root} 下找不到任何子圖幅目錄(LOD={lod})")

    log.info("發現 %d 個子圖幅(LOD=%s)", len(subtiles), lod)
    return root, subtiles


def _find_sheet_root(extract_dir: Path) -> Optional[Path]:
    """尋找包含 <SheetNo>-N/ 子目錄的根。可能多一層包裝目錄。"""
    for candidate in [extract_dir, *extract_dir.iterdir()]:
        if not candidate.is_dir():
            continue
        subdirs = [d for d in candidate.iterdir() if d.is_dir() and re.match(r"^.+-\d+$", d.name)]
        if len(subdirs) >= 2:
            return candidate
    return None


# ────────────────────────── Stage 2: OSGB → OBJ (osgconv) ──────────────────────────
def convert_osgb_to_obj(osgb_path: Path, out_dir: Path, osgconv: Path) -> Path:
    """
    以 osgconv 將單一 .osgb 轉為 .obj(含紋理)。
    使用 -O OutputTextureFiles 讓內嵌紋理寫出到磁碟。
    out_dir 會成為 cwd,使紋理落在 .obj 旁。
    回傳 .obj 路徑。
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    obj_path = out_dir / (osgb_path.stem + ".obj")

    env = os.environ.copy()
    osg_bin = osgconv.parent
    env["PATH"] = str(osg_bin) + os.pathsep + env.get("PATH", "")
    plugin_dir = osg_bin / f"osgPlugins-{_osg_version(osgconv)}"
    if plugin_dir.is_dir():
        env["OSG_PLUGIN_PATH"] = str(plugin_dir)

    cmd = [str(osgconv), str(osgb_path), str(obj_path), "-O", "OutputTextureFiles"]
    log.debug("  osgconv: %s", osg_path_for_log(osgb_path))
    r = subprocess.run(cmd, env=env, capture_output=True, text=True,
                       timeout=300, cwd=str(out_dir))
    if r.returncode != 0 or not obj_path.exists():
        raise RuntimeError(
            f"osgconv 失敗 (rc={r.returncode}): {osgb_path.name}\n"
            f"stdout: {r.stdout[-400:]}\nstderr: {r.stderr[-600:]}"
        )

    # osgconv 寫出的 .mtl 可能以 CSDI 建置機的絕對路徑引用紋理
    # (如 F:\01_HK_2024\...\11-NW-19B-7_013.png)。osgconv 已將紋理以
    # basename 複製到 out_dir,故改寫 .mtl 為 basename-only,讓 trimesh 解析。
    _rewrite_mtl_to_basenames(obj_path)
    return obj_path


def _rewrite_mtl_to_basenames(obj_path: Path) -> None:
    """將 .mtl 中所有 map_Kd/map_Ks/map_Bump 等的路徑改為 basename。"""
    mtl_path = obj_path.with_suffix(".mtl")
    if not mtl_path.exists():
        return
    try:
        text = mtl_path.read_text(encoding="utf-8", errors="replace")
        changed = False
        new_lines = []
        for line in text.splitlines():
            stripped = line.strip()
            # 偵測紋理貼圖行(map_Kd、map_Ks、map_Bump、map_d、bump 等)
            if re.match(r"^(map_\w+|bump|disp|decal|refl)\s+", stripped, re.IGNORECASE):
                parts = stripped.split(None, 1)
                if len(parts) == 2:
                    tex_ref = parts[1].strip().strip('"')
                    # 取 basename,處理 Windows/Unix 路徑與反斜線
                    tex_ref_norm = tex_ref.replace("\\", "/")
                    basename = tex_ref_norm.split("/")[-1]
                    if basename != tex_ref:
                        new_lines.append(f"{parts[0]} {basename}")
                        changed = True
                        continue
            new_lines.append(line)
        if changed:
            mtl_path.write_text("\n".join(new_lines) + "\n", encoding="utf-8")
            log.debug("  已改寫 .mtl 紋理路徑為 basename: %s", mtl_path.name)
    except Exception as e:
        log.debug("  .mtl 改寫失敗(非致命): %s", e)


def _osg_version(osgconv: Path) -> str:
    """從 plugin 目錄名推斷版本(如 osgPlugins-3.6.5)。"""
    parent = osgconv.parent
    for d in parent.iterdir():
        m = re.match(r"osgPlugins-(.+)$", d.name)
        if m:
            return m.group(1)
    return "3.6.5"


def osg_path_for_log(p: Path) -> str:
    return p.name if len(str(p)) > 80 else str(p)


# ────────────────────────── Stage 3: 載入 OBJ + 簡化 + 匯出 GLB ──────────────────────────
def build_chunk_glb(
    obj_path: Path,
    out_glb: Path,
    *,
    target_bytes: int,
    max_texture_px: int,
    decimate_threshold: int,
    recenter: np.ndarray,
) -> tuple[int, int, np.ndarray, np.ndarray]:
    """
    載入 OBJ(含紋理),以 pymeshlab 的 UV 保留簡化至目標大小,
    降解析度紋理,重新置中至 recenter(本地座標),匯出 .glb。
    回傳 (位元組數, 三角面總數, bbox_min, bbox_max)。

    流程:pymeshlab 載入 → meshing_decimation_quadric_edge_collapse_with_texture
    (保留 UV/紋理) → 存 OBJ → trimesh 載入 → 置中 + 紋理降解析 → 匯出 GLB。
    """
    import warnings
    warnings.filterwarnings("ignore")
    import pymeshlab
    import trimesh

    # 1. pymeshlab 載入 + 計算原始三角面數與 bbox
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(str(obj_path))
    m0 = ms.current_mesh()
    orig_faces = m0.face_number()
    orig_verts = m0.vertex_number()

    # 取得原始 bbox(從頂點)
    # pymeshlab 的 bbox:用 vertex matrix
    vmatrix = m0.vertex_matrix()
    bbox_min = np.array(vmatrix.min(axis=0)).flatten()
    bbox_max = np.array(vmatrix.max(axis=0)).flatten()

    # 2. 決定目標三角面數
    # 經驗:含紋理 GLB 約 11k faces/MB(含 256px 紋理)。
    # 保守取 target_bytes 的 70% 給幾何,30% 給紋理。
    geom_budget = target_bytes * 0.7
    bytes_per_face = 60  # 位置+索引+法線+UV 的壓縮後估計
    target_faces = int(geom_budget / bytes_per_face)
    target_faces = min(target_faces, orig_faces)

    log.debug("  pymeshlab: %d faces → target %d", orig_faces, target_faces)

    # 3. 簡化(若需要)
    if orig_faces > target_faces and orig_faces > decimate_threshold:
        try:
            ms.meshing_decimation_quadric_edge_collapse_with_texture(
                targetfacenum=target_faces
            )
            m1 = ms.current_mesh()
            log.debug("  decimated: %d → %d faces, %d textures",
                      orig_faces, m1.face_number(), m1.texture_number())
        except Exception as e:
            log.warning("  pymeshlab 簡化失敗(%s),保留原始", e)

    # 4. 存為 OBJ(含紋理)到暫存目錄,再以 trimesh 載入
    tmp_dir = out_glb.parent / f"_tmp_{out_glb.stem}"
    tmp_dir.mkdir(parents=True, exist_ok=True)
    tmp_obj = tmp_dir / "chunk.obj"
    ms.save_current_mesh(str(tmp_obj))

    # 5. trimesh 載入 + 置中 + 紋理降解析
    scene = trimesh.load(str(tmp_obj), force="scene")
    geometries: list[trimesh.Trimesh] = []
    if isinstance(scene, trimesh.Scene):
        for _name, g in scene.geometry.items():
            if hasattr(g, "faces") and len(g.faces) > 0:
                geometries.append(g)
    elif hasattr(scene, "faces"):
        geometries.append(scene)

    if not geometries:
        # 清理暫存
        import shutil
        shutil.rmtree(tmp_dir, ignore_errors=True)
        raise RuntimeError(f"簡化後 OBJ 無有效網格: {obj_path}")

    # 5a. Mesh cleanup:移除孤立小元件(spike/雜訊)。
    # 攝影測量資料常有飛散三角形(spike),以「連通元件面數 < 門檻」過濾。
    # 門檻取 max(20, 總面數 0.5%);低於此的元件視為雜訊移除。
    cleaned: list[trimesh.Trimesh] = []
    total_faces_pre = 0
    total_faces_post = 0
    for g in geometries:
        total_faces_pre += len(g.faces)
        # trimesh.split 需 networkx;改用 face_adjacency 自行找連通元件
        try:
            import scipy.sparse as sp
            import scipy.sparse.csgraph as csg
            # 建 face-adjacency graph(共邊的 face 互連)
            edges = g.face_adjacency  # (F1, F2) pairs sharing an edge
            n = len(g.faces)
            if n > 0 and len(edges) > 0:
                row = np.concatenate([e[0] for e in edges] + [e[1] for e in edges])
                col = np.concatenate([e[1] for e in edges] + [e[0] for e in edges])
                data = np.ones(len(row), dtype=np.int8)
                adj = sp.csr_matrix((data, (row, col)), shape=(n, n))
                n_comp, labels = csg.connected_components(adj, directed=False)
                comp_sizes = np.bincount(labels)
                threshold = max(20, int(n * 0.005))  # 0.5% 或至少 20 面
                keep_mask = comp_sizes[labels] >= threshold
                if keep_mask.sum() < n:
                    # trimesh 5.x submesh() 回傳 Trimesh 而非 list,改用 update_faces 保留紋理
                    g = g.copy()
                    g.update_faces(keep_mask)
                    log.debug("  cleanup: 移除 %d 個小元件(<%d 面),保留 %d/%d 面",
                              n_comp - len(np.unique(labels[keep_mask])),
                              threshold, len(g.faces), n)
        except ImportError:
            pass  # scipy 不可用則跳過 cleanup
        except Exception as e:
            log.debug("  cleanup 跳過(%s)", e)
        if hasattr(g, "faces") and len(g.faces) > 0:
            cleaned.append(g)
            total_faces_post += len(g.faces)
    geometries = cleaned
    if total_faces_pre != total_faces_post:
        log.debug("  cleanup 總計: %d → %d faces (移除 %d 雜訊面)",
                  total_faces_pre, total_faces_post, total_faces_pre - total_faces_post)

    # 5b. 浮空地面移除(floating-ground removal)。
    # CSDI 攝影測量資料在同一 XY 位置常有多層水平面(地面 + 浮空層 + 屋頂),
    # 全貼同一張空拍紋理 → 視覺上像多層「地面疊地面」(用户稱「4 個島」)。
    #
    # 演算法(KDTree 局部最低 Z):
    #   1. 對每個水平面,以 KDTree 查 35m 半徑內所有水平面,取最低 Z = 局部地面
    #   2. 若面 Z − 局部地面 Z 在 3~20m → 浮空層,移除
    #   3. 保留:地面(dz<3m)+ 屋頂(dz>20m)
    #
    # 35m → 50m 半徑:地面層約覆蓋 60% 面積,50m 可涵蓋 ~78% 浮空面;
    # 邊界外(>50m 無地面)的浮空面視為該區域的地面,保留。
    from scipy.spatial import cKDTree
    R = 50.0  # m,局部地面搜尋半徑
    MIN_DZ = 3.0  # m,高於局部地面此值才視為浮空
    MAX_DZ = 20.0  # m,高於此值視為屋頂,保留
    deduped: list[trimesh.Trimesh] = []
    removed_floating = 0
    for g in geometries:
        n = len(g.faces)
        if n < 10:
            deduped.append(g)
            continue
        try:
            tri_verts = np.asarray(g.vertices)[np.asarray(g.faces)]  # (N, 3, 3)
            v0, v1, v2 = tri_verts[:, 0], tri_verts[:, 1], tri_verts[:, 2]
            cross = np.cross(v1 - v0, v2 - v0)
            norms = np.linalg.norm(cross, axis=1, keepdims=True)
            norms[norms == 0] = 1.0
            fn = cross / norms  # (N, 3) 正規化面法線
            fc = tri_verts.mean(axis=1)  # (N, 3) 面心

            horiz = np.abs(fn[:, 2]) > 0.85
            if horiz.sum() < 20:
                deduped.append(g)
                continue
            horiz_idx = np.where(horiz)[0]
            h_xy = fc[horiz_idx, :2]
            h_z = fc[horiz_idx, 2]

            # KDTree 查每個水平面 35m 半徑內的所有水平面,取最低 Z
            tree = cKDTree(h_xy)
            float_mask = np.zeros(n, dtype=bool)
            for ii, face_i in enumerate(horiz_idx):
                neighbors = tree.query_ball_point(h_xy[ii], r=R)
                if len(neighbors) > 0:
                    local_min = float(h_z[neighbors].min())
                    dz = float(h_z[ii]) - local_min
                    if MIN_DZ <= dz <= MAX_DZ:
                        float_mask[face_i] = True

            n_float = int(float_mask.sum())
            if n_float > 0:
                keep = ~float_mask
                g = g.copy()
                g.update_faces(keep)
                removed_floating += n_float
                log.debug("  浮空地面移除: %d faces (50m 半徑內 3-20m 浮空層)", n_float)
        except Exception as e:
            log.debug("  浮空地面移除跳過(%s)", e)
        if hasattr(g, "faces") and len(g.faces) > 0:
            deduped.append(g)
    geometries = deduped
    if removed_floating > 0:
        log.info("  浮空地面移除總計: %d faces", removed_floating)

    for g in geometries:
        g.vertices = g.vertices - recenter
        _downscale_textures(g, max_texture_px)

    total_tris = sum(len(g.faces) for g in geometries)

    # 6. 匯出 GLB
    out_scene = trimesh.Scene(geometries)
    out_glb.parent.mkdir(parents=True, exist_ok=True)
    out_scene.export(str(out_glb))
    actual_bytes = out_glb.stat().st_size

    # 7. 若仍超出目標,第二輪更激進簡化
    if actual_bytes > target_bytes * 1.15 and total_tris > 5000:
        log.debug("  首輪 %d bytes > 目標,第二輪簡化", actual_bytes)
        over_ratio = (target_bytes / actual_bytes) ** 1.3
        new_target = max(2000, int(total_tris * over_ratio))
        ms2 = pymeshlab.MeshSet()
        ms2.load_new_mesh(str(tmp_obj))
        try:
            ms2.meshing_decimation_quadric_edge_collapse_with_texture(
                targetfacenum=new_target
            )
            ms2.save_current_mesh(str(tmp_obj))
            scene2 = trimesh.load(str(tmp_obj), force="scene")
            geoms2: list[trimesh.Trimesh] = []
            if isinstance(scene2, trimesh.Scene):
                for _n, g in scene2.geometry.items():
                    if hasattr(g, "faces") and len(g.faces) > 0:
                        g.vertices = g.vertices - recenter
                        _downscale_textures(g, max_texture_px)
                        geoms2.append(g)
            out_scene = trimesh.Scene(geoms2)
            out_scene.export(str(out_glb))
            actual_bytes = out_glb.stat().st_size
            total_tris = sum(len(g.faces) for g in geoms2)
            log.debug("  第二輪: %d bytes, %d tris", actual_bytes, total_tris)
        except Exception as e:
            log.warning("  第二輪簡化失敗: %s", e)

    # 清理暫存目錄
    import shutil
    shutil.rmtree(tmp_dir, ignore_errors=True)

    return actual_bytes, total_tris, bbox_min, bbox_max


def _downscale_textures(mesh: "trimesh.Trimesh", max_px: int) -> None:
    vis = getattr(mesh, "visual", None)
    if vis is None:
        return
    mat = getattr(vis, "material", None)
    if mat is None:
        return
    img = getattr(mat, "image", None) or getattr(mat, "baseColorTexture", None)
    if img is None:
        return
    try:
        im = img if isinstance(img, Image.Image) else Image.open(img)
        if max(im.size) > max_px:
            scale = max_px / max(im.size)
            im = im.resize((int(im.size[0] * scale), int(im.size[1] * scale)), Image.LANCZOS)
            if isinstance(img, Image.Image):
                mat.image = im
    except Exception as e:
        log.debug("  紋理降解析度失敗: %s", e)


# ────────────────────────── Stage 4: manifest + 鄰居 ──────────────────────────
def build_manifest(
    chunks: list[ChunkRecord],
    *,
    sheet_name: str,
    origin: list[float],
    tile_size: list[float],
    grid_cols: int,
    grid_rows: int,
    total_bytes: int,
    total_triangles: int,
) -> dict:
    return {
        "version": "1.0",
        "source": sheet_name,
        "crs": "EPSG:2326",
        "origin": origin,
        "tileSize": tile_size,
        "gridCols": grid_cols,
        "gridRows": grid_rows,
        "chunkCount": len(chunks),
        "totalBytes": total_bytes,
        "totalTriangles": total_triangles,
        "attribution": "© 香港特別行政區地政總署 / Government of the Hong Kong SAR (Lands Department 3D Visualisation Map)",
        "chunks": [asdict(c) for c in sorted(chunks, key=lambda c: (c.grid_y, c.grid_x))],
    }


def compute_neighbors(subtiles: list[SubTile]) -> dict[int, list[int]]:
    """依子圖幅的 (tile_x, tile_y) 網格相鄰關係計算鄰居。回傳 {index: [neighbor_indices]}。"""
    by_coord = {(s.tile_x, s.tile_y): s.index for s in subtiles}
    result = {}
    for s in subtiles:
        nbrs = []
        for dx, dy in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
            key = (s.tile_x + dx, s.tile_y + dy)
            if key in by_coord:
                nbrs.append(by_coord[key])
        result[s.index] = nbrs
    return result


# ────────────────────────── 主流程 ──────────────────────────
def main() -> int:
    p = argparse.ArgumentParser(
        description="OSGB → 分塊 GLB 轉換器(Stage A)",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    p.add_argument("--input", default=os.environ.get("OSGB_INPUT"),
                   help="輸入 .osgb.zip 或已解壓目錄(亦可由 OSGB_INPUT 環境變數提供)")
    p.add_argument("--output", default=os.environ.get("OSGB_OUTPUT"),
                   help="輸出目錄(亦可由 OSGB_OUTPUT 環境變數提供)")
    p.add_argument("--osgconv", default=os.environ.get("OSGCONV_PATH", "osgconv"),
                   help="osgconv 執行檔路徑(亦可由 OSGCONV_PATH 環境變數提供)")
    p.add_argument("--max-chunk-mb", type=float,
                   default=float(os.environ.get("MAX_CHUNK_MB", "8")),
                   help="單一分塊 .glb 最大 MB")
    p.add_argument("--max-total-mb", type=float,
                   default=float(os.environ.get("MAX_TOTAL_MB", "200")),
                   help="全圖幅分塊總和最大 MB")
    p.add_argument("--max-texture-px", type=int,
                   default=int(os.environ.get("MAX_TEXTURE_PX", "512")),
                   help="紋理最長邊像素")
    p.add_argument("--decimate-threshold", type=int, default=3000,
                   help="低於此三角面數的網格不簡化(保留紋理/UV)")
    p.add_argument("--lod", choices=["root", "l15", "l16"], default="root",
                   help="LOD 層級:root=最粗(建物無牆面,堆疊平板);l15=第15層;"
                        "l16=第16層(建物有真正牆面,視覺顯著改善,推薦)。"
                        "每子圖幅 root/l15/l16 各為單一 .osgb 檔")
    p.add_argument("--verbose", action="store_true")
    args = p.parse_args()

    setup_logging(args.verbose)

    if not args.input:
        p.error("必須提供 --input(或設 OSGB_INPUT 環境變數)")
    if not args.output:
        p.error("必須提供 --output(或設 OSGB_OUTPUT 環境變數)")

    input_path = Path(args.input).resolve()
    output_dir = Path(args.output).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    osgconv = Path(args.osgconv).resolve()

    if not osgconv.exists():
        log.error("找不到 osgconv: %s", osgconv)
        log.error("Windows: 自 https://objexx.com/OpenSceneGraph.html 下載 7z 解壓後傳 --osgconv <bin>/osgconv.exe")
        log.error("macOS:   brew install open-scene-graph")
        log.error("Linux:   apt install openscenegraph")
        return 2

    max_chunk_bytes = int(args.max_chunk_mb * 1_000_000)
    max_total_bytes = int(args.max_total_mb * 1_000_000)

    log.info("=== Stage A: OSGB → 分塊 GLB ===")
    log.info("輸入: %s", input_path)
    log.info("輸出: %s", output_dir)
    log.info("osgconv: %s", osgconv)
    log.info("預算: 每分塊 ≤ %.1f MB,總和 ≤ %.1f MB", args.max_chunk_mb, args.max_total_mb)

    work_dir = output_dir / "_work"
    work_dir.mkdir(exist_ok=True)

    # Stage 1: 探索
    root, subtiles = discover_subtiles(input_path, work_dir, lod=args.lod)
    sheet_name = _guess_sheet_name(root)
    log.info("圖幅: %s,子圖幅數: %d,LOD: %s", sheet_name, len(subtiles), args.lod)

    neighbors_map = compute_neighbors(subtiles)

    # 先做一次完整載入以計算全域 bbox / 置中點(用第一個子圖幅的根估計,
    # 實際每塊獨立轉換時會各自取 bbox;manifest 的 origin 取所有子圖幅的總中心)
    log.info("=== 預先掃描所有子圖幅的 bbox 以計算置中點 ===")
    all_min = np.array([np.inf, np.inf, np.inf])
    all_max = np.array([-np.inf, -np.inf, -np.inf])
    # 為了避免重複轉換,先快速 osgconv 所有根 OBJ 到 work_dir,記錄 bbox
    obj_cache: dict[int, Path] = {}
    for s in subtiles:
        obj_out = work_dir / s.dir_name
        try:
            obj_path = convert_osgb_to_obj(s.root_osgb, obj_out, osgconv)
            obj_cache[s.index] = obj_path
            # 快速讀取頂點範圍(不載入紋理)
            import trimesh
            sc = trimesh.load(str(obj_path), force="scene")
            if isinstance(sc, trimesh.Scene):
                for _n, g in sc.geometry.items():
                    if hasattr(g, "vertices") and len(g.vertices) > 0:
                        all_min = np.minimum(all_min, g.vertices.min(axis=0))
                        all_max = np.maximum(all_max, g.vertices.max(axis=0))
        except Exception as e:
            log.warning("子圖幅 %d 預掃描失敗: %s", s.index, e)

    if not np.isfinite(all_min[0]):
        log.error("無法從任何子圖幅取得 bbox")
        return 3

    centroid = (all_min + all_max) / 2.0
    tile_size = all_max - all_min
    log.info("全域 bbox: %s → %s", all_min.tolist(), all_max.tolist())
    log.info("尺寸: %s m,置中點: %s", tile_size.tolist(), centroid.tolist())

    # Stage 2+3: 逐子圖幅簡化 + 匯出 GLB
    log.info("=== 逐子圖幅轉換為 .glb 分塊 ===")
    chunks: list[ChunkRecord] = []
    total_bytes = 0
    total_tris = 0

    # 網格座標正規化(用於 manifest 的 grid_x/grid_y)
    min_tx = min(s.tile_x for s in subtiles)
    min_ty = min(s.tile_y for s in subtiles)
    grid_cols = len(set(s.tile_x for s in subtiles))
    grid_rows = len(set(s.tile_y for s in subtiles))

    for i, s in enumerate(subtiles, 1):
        chunk_id = f"x{s.tile_x}_y{s.tile_y}"
        out_glb = output_dir / f"chunk_{chunk_id}.glb"
        log.info("[%d/%d] %s → %s", i, len(subtiles), s.dir_name, out_glb.name)

        obj_path = obj_cache.get(s.index)
        if obj_path is None or not obj_path.exists():
            log.warning("  跳過(無 OBJ)")
            continue

        try:
            cb, ct, bmin, bmax = build_chunk_glb(
                obj_path, out_glb,
                target_bytes=max_chunk_bytes,
                max_texture_px=args.max_texture_px,
                decimate_threshold=args.decimate_threshold,
                recenter=centroid,
            )
        except Exception as e:
            log.error("  分塊建置失敗: %s", e)
            continue

        # 鄰居 ID
        nbr_ids = [f"x{subtiles[n-1].tile_x}_y{subtiles[n-1].tile_y}"
                   for n in neighbors_map.get(s.index, [])]
        # 注意:neighbors_map 的值是 1-based index,要對應回 SubTile
        nbr_ids = []
        for n_idx in neighbors_map.get(s.index, []):
            ns = next((x for x in subtiles if x.index == n_idx), None)
            if ns:
                nbr_ids.append(f"x{ns.tile_x}_y{ns.tile_y}")

        rec = ChunkRecord(
            id=chunk_id,
            grid_x=s.tile_x - min_tx,
            grid_y=s.tile_y - min_ty,
            bbox_min=[round(float(v), 3) for v in (bmin - centroid)],
            bbox_max=[round(float(v), 3) for v in (bmax - centroid)],
            centroid=[round(float(v), 3) for v in ((bmin + bmax) / 2 - centroid)],
            file=out_glb.name,
            bytes=cb,
            triangles=ct,
            neighbors=nbr_ids,
            priority=1,
        )
        chunks.append(rec)
        total_bytes += cb
        total_tris += ct
        log.info("  OK: %.2f MB, %d tris", cb / 1e6, ct)

        if total_bytes > max_total_bytes:
            log.error("  總和已超過 %.1f MB 預算(目前 %.1f MB),中止", args.max_total_mb, total_bytes / 1e6)
            break

    # Stage 4: manifest
    manifest = build_manifest(
        chunks,
        sheet_name=sheet_name,
        origin=[round(float(v), 3) for v in centroid],
        tile_size=[round(float(v), 3) for v in tile_size],
        grid_cols=grid_cols,
        grid_rows=grid_rows,
        total_bytes=total_bytes,
        total_triangles=total_tris,
    )
    manifest_path = output_dir / "manifest.json"
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
    log.info("=== manifest.json 寫入: %s ===", manifest_path)

    # 清理 work 目錄(保留 manifest + glb)
    shutil.rmtree(work_dir, ignore_errors=True)

    log.info("=== 完成 ===")
    log.info("分塊數: %d", len(chunks))
    log.info("總大小: %.2f MB / %.1f MB 預算", total_bytes / 1e6, args.max_total_mb)
    log.info("總三角面: %d", total_tris)
    _print_size_report(chunks, max_chunk_bytes, max_total_bytes)
    return 0


def _guess_sheet_name(root: Path) -> str:
    parent_name = root.name
    m = re.match(r"^(\d+-[A-Z]+-\d+[A-Z]?)", parent_name)
    return m.group(1) if m else parent_name


def _print_size_report(chunks: list[ChunkRecord], max_chunk: int, max_total: int) -> None:
    log.info("--- 分塊大小報告 ---")
    over = [c for c in chunks if c.bytes > max_chunk]
    for c in sorted(chunks, key=lambda x: -x.bytes):
        flag = " ⚠ 超出" if c.bytes > max_chunk else ""
        log.info("  %-14s %7.2f MB  %7d tris%s", c.id, c.bytes / 1e6, c.triangles, flag)
    if over:
        log.error("⚠ %d 個分塊超出單塊預算", len(over))
    total = sum(c.bytes for c in chunks)
    if total > max_total:
        log.error("⚠ 總和 %.2f MB 超出總預算 %.1f MB", total / 1e6, max_total / 1e6)


if __name__ == "__main__":
    sys.exit(main())
