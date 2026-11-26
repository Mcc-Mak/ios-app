#!/usr/bin/env python3
"""
decimate_glb.py — 將現有 GLB 分塊重新簡化至目標大小(不需 osgconv/pymeshlab)。

v4:合併每塊內所有 mesh → fast_simplification(目標面數)→ 平面色材質。
  - 合併:每塊 16 mesh → 1 mesh(減少紋理數量 16×)
  - 簡化:fast_simplification.simplify(verts, faces, target_count=N)
    (UV 會丟失,改用紋理平均色作為 baseColorFactor)
  - 紋理:取第一個 mesh 的 baseColorTexture 平均色,設為 PBRMaterial.baseColorFactor

v5:新增 --textured 模式(保留紋理,trimesh 純路徑)。
  - 每個子網格:fast_simplification 簡化 → scipy KDTree UV 重投影(逆距離加權 k=3)
  - 紋理降解析度至 --texture-size(預設 128px)
  - 不需 pymeshlab(避免其無法匯出 .glb/.obj-with-texture 的限制)
  - 適用於 20MB IPA 分塊(遠優於平面色的 2MB 版)

用法:
  # 2MB 平面色(原 v4):
  python decimate_glb.py \\
    --input  /path/to/source_chunks \\
    --output /path/to/ipa_chunks \\
    --target-total-mb 2 --faces-per-chunk 1500

  # 20MB 保留紋理(v5):
  python decimate_glb.py \\
    --input  /path/to/source_chunks \\
    --output /path/to/ipa_chunks_20mb \\
    --target-total-mb 20 --faces-per-chunk 12000 --textured --texture-size 64
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
import trimesh
import fast_simplification


def _safe_resolve(p: Path, *, must_exist: bool = False) -> Path:
    """驗證並解析 CLI 引數路徑,防範路徑穿越(SonarCloud S8707)。"""
    resolved = p.expanduser().resolve()
    if must_exist and not resolved.exists():
        print(f"錯誤:路徑不存在: {resolved}", file=sys.stderr)
        sys.exit(1)
    return resolved


def decimate_chunk(src_glb: Path, dst_glb: Path,
                   target_faces: int) -> tuple[int, int]:
    """載入 GLB,合併 mesh,簡化,匯出。回傳 (bytes, triangles)。"""
    scene = trimesh.load(str(src_glb), force="scene")

    meshes = []
    if isinstance(scene, trimesh.Scene):
        for _n, g in scene.geometry.items():
            if hasattr(g, "faces") and len(g.faces) > 0:
                meshes.append(g)
    elif isinstance(scene, trimesh.Trimesh) and len(scene.faces) > 0:
        meshes.append(scene)

    if not meshes:
        return 0, 0

    # 1. 合併所有 mesh 為單一網格(trimesh.util.concatenate)
    combined = trimesh.util.concatenate(meshes)
    orig_faces = len(combined.faces)

    # 2. 採樣紋理平均色(簡化後 UV 丟失,改用平均色)
    avg_color = np.array([180, 180, 180], dtype=np.uint8)  # 預設灰
    for g in meshes:
        vis = getattr(g, "visual", None)
        if vis is not None:
            mat = getattr(vis, "material", None)
            if mat is not None:
                img = getattr(mat, "baseColorTexture", None)
                if img is not None and isinstance(img, Image.Image):
                    arr = np.array(img.convert("RGB"))
                    avg_color = arr.mean(axis=(0, 1)).astype(np.uint8)
                    break  # 取第一個有紋理的 mesh

    # 3. 簡化網格(fast_simplification,目標面數)
    if orig_faces > target_faces:
        verts = np.array(combined.vertices, dtype=np.float64)
        faces = np.array(combined.faces, dtype=np.int64)
        try:
            new_verts, new_faces = fast_simplification.simplify(
                verts, faces, target_count=target_faces)
            combined = trimesh.Trimesh(
                vertices=np.asarray(new_verts, dtype=np.float64),
                faces=np.asarray(new_faces, dtype=np.int64),
                process=False)
        except Exception as e:
            print(f"    簡化失敗({e}),保留原網格", file=sys.stderr)

    # 4. 套用平面色材質(無紋理,baseColorFactor = 平均色)
    mat = trimesh.visual.material.PBRMaterial()
    mat.baseColorFactor = [int(avg_color[0]), int(avg_color[1]),
                           int(avg_color[2]), 255]
    mat.doubleSided = True
    mat.roughnessFactor = 1.0
    combined.visual = trimesh.visual.TextureVisuals(material=mat)

    # 5. 匯出 GLB
    dst_glb.parent.mkdir(parents=True, exist_ok=True)
    combined.export(str(dst_glb))
    actual_bytes = dst_glb.stat().st_size
    return actual_bytes, len(combined.faces)


def _reproject_uvs(orig_verts: np.ndarray, orig_uvs: np.ndarray,
                   new_verts: np.ndarray) -> np.ndarray:
    """為每個新頂點找最近原始頂點(逆距離加權 k=3),內插 UV。

    fast_simplification 不保留 UV,故簡化後需重投影。
    使用 scipy.spatial.cKDTree(不需 rtree)做最近鄰搜尋。
    """
    from scipy.spatial import cKDTree
    k = min(3, len(orig_verts))
    tree = cKDTree(orig_verts)
    dist, indices = tree.query(new_verts, k=k)
    if k == 1:
        return orig_uvs[indices]
    # 逆距離加權
    weights = 1.0 / (dist + 1e-12)
    weights /= weights.sum(axis=1, keepdims=True)
    neighbor_uvs = orig_uvs[indices]  # (N, k, 2)
    return np.einsum("ij,ijk->ik", weights, neighbor_uvs)


def decimate_chunk_textured(src_glb: Path, dst_glb: Path,
                            target_faces: int,
                            texture_size: int) -> tuple[int, int]:
    """
    v5: 保留紋理的簡化(trimesh 純路徑,不需 pymeshlab)。

    流程:
      1. trimesh 載入 GLB(保留子網格 + UV + 紋理)
      2. 每個子網格:fast_simplification 簡化 → scipy KDTree UV 重投影
      3. 紋理降解析度至 texture_size
      4. 合併子網格 → 匯出 GLB

    回傳 (bytes, triangles)。
    """
    scene = trimesh.load(str(src_glb), force="scene")

    # 收集所有子網格
    sub_meshes = []
    if isinstance(scene, trimesh.Scene):
        for _n, g in scene.geometry.items():
            if hasattr(g, "faces") and len(g.faces) > 0:
                sub_meshes.append(g)
    elif isinstance(scene, trimesh.Trimesh) and len(scene.faces) > 0:
        sub_meshes.append(scene)

    if not sub_meshes:
        return 0, 0

    # 每個子網格的目標面數(平均分配)
    target_per = max(200, target_faces // len(sub_meshes))

    out_meshes = []
    for g in sub_meshes:
        orig_verts = np.array(g.vertices, dtype=np.float64)
        orig_faces = np.array(g.faces, dtype=np.int64)

        # 取原始 UV + 材質
        orig_uvs = None
        mat = None
        vis = getattr(g, "visual", None)
        if vis is not None:
            mat = getattr(vis, "material", None)
            if hasattr(vis, "uv") and vis.uv is not None and len(vis.uv) == len(orig_verts):
                orig_uvs = np.array(vis.uv, dtype=np.float64)

        # 簡化
        if len(orig_faces) > target_per:
            new_verts, new_faces = fast_simplification.simplify(
                orig_verts, orig_faces, target_count=target_per)
            new_verts = np.asarray(new_verts, dtype=np.float64)
            new_faces = np.asarray(new_faces, dtype=np.int64)
        else:
            new_verts = orig_verts
            new_faces = orig_faces

        # UV 重投影
        new_uvs = None
        if orig_uvs is not None and len(new_verts) > 0:
            new_uvs = _reproject_uvs(orig_verts, orig_uvs, new_verts)

        # 建立結果網格
        result = trimesh.Trimesh(vertices=new_verts, faces=new_faces,
                                  process=False)
        if new_uvs is not None and mat is not None:
            # 降解析度紋理
            img = getattr(mat, "baseColorTexture", None)
            if img is not None and isinstance(img, Image.Image):
                if img.size[0] > texture_size or img.size[1] > texture_size:
                    img = img.resize(
                        (texture_size, texture_size), Image.LANCZOS)
                    mat.baseColorTexture = img
                mat.doubleSided = True
                mat.roughnessFactor = 1.0
            result.visual = trimesh.visual.TextureVisuals(
                uv=new_uvs, material=mat)

        out_meshes.append(result)

    # 合併並匯出
    if len(out_meshes) > 1:
        combined = trimesh.util.concatenate(out_meshes)
    else:
        combined = out_meshes[0]

    dst_glb.parent.mkdir(parents=True, exist_ok=True)
    combined.export(str(dst_glb))
    actual_bytes = dst_glb.stat().st_size
    face_count = len(combined.faces) if hasattr(combined, "faces") else 0
    return actual_bytes, face_count


def main() -> int:
    p = argparse.ArgumentParser(
        description="重新簡化 GLB 分塊至目標大小(fast_simplification + 合併 mesh)",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    p.add_argument("--input", required=True, help="來源分塊目錄(含 manifest.json + *.glb)")
    p.add_argument("--output", required=True, help="輸出目錄")
    p.add_argument("--target-total-mb", type=float, default=2.0, help="目標總大小(MB)")
    p.add_argument("--faces-per-chunk", type=int, default=1500,
                   help="每塊目標面數(合併後)")
    p.add_argument("--textured", action="store_true",
                   help="v5: 保留紋理(fast_simplification + UV 重投影 + 紋理降解析度)")
    p.add_argument("--texture-size", type=int, default=128,
                   help="v5: 紋理降解析度目標(像素,正方形)。僅 --textured 時生效")
    args = p.parse_args()

    src_dir = _safe_resolve(Path(args.input), must_exist=True)
    dst_dir = _safe_resolve(Path(args.output))
    dst_dir.mkdir(parents=True, exist_ok=True)

    manifest_path = src_dir / "manifest.json"
    if not manifest_path.exists():
        print(f"錯誤:找不到 {manifest_path}", file=sys.stderr)
        return 1

    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    n_chunks = len(manifest["chunks"])

    mode = "保留紋理" if args.textured else "平面色"
    print(f"目標: {args.target_total_mb} MB 總計, {n_chunks} 分塊")
    print(f"  每塊 ~{args.faces_per_chunk} 面, {mode}")
    if args.textured:
        print(f"  紋理降解析度: {args.texture_size}x{args.texture_size}px")
    print(f"  來源: {src_dir}")
    print(f"  輸出: {dst_dir}")
    print()

    total_bytes = 0
    total_tris = 0
    for i, c in enumerate(manifest["chunks"], 1):
        src_glb = src_dir / c["file"]
        dst_glb = dst_dir / c["file"]
        if not src_glb.exists():
            print(f"  [{i}/{n_chunks}] {c['id']}: 跳過(找不到 {src_glb.name})")
            continue
        if args.textured:
            b, t = decimate_chunk_textured(src_glb, dst_glb, args.faces_per_chunk, args.texture_size)
        else:
            b, t = decimate_chunk(src_glb, dst_glb, args.faces_per_chunk)
        c["bytes"] = b
        c["triangles"] = t
        total_bytes += b
        total_tris += t
        print(f"  [{i}/{n_chunks}] {c['id']}: {b / 1024:.0f} KB, {t} 面")

    manifest["totalBytes"] = total_bytes
    manifest["totalTriangles"] = total_tris

    with open(dst_dir / "manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    print(f"\n總計: {total_bytes / 1e6:.2f} MB, {total_tris} 面")
    print(f"manifest 已寫入: {dst_dir / 'manifest.json'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
