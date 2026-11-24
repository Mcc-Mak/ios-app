#!/usr/bin/env python3
"""
decimate_glb.py — 將現有 GLB 分塊重新簡化至目標大小(不需 osgconv/pymeshlab)。

v4:合併每塊內所有 mesh → fast_simplification(目標面數)→ 平面色材質。
  - 合併:每塊 16 mesh → 1 mesh(減少紋理數量 16×)
  - 簡化:fast_simplification.simplify(verts, faces, target_count=N)
    (UV 會丟失,改用紋理平均色作為 baseColorFactor)
  - 紋理:取第一個 mesh 的 baseColorTexture 平均色,設為 PBRMaterial.baseColorFactor

用法:
  python decimate_glb.py \\
    --input  /path/to/source_chunks \\
    --output /path/to/ipa_chunks \\
    --target-total-mb 2 --faces-per-chunk 1500
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
    args = p.parse_args()

    src_dir = Path(args.input)
    dst_dir = Path(args.output)
    dst_dir.mkdir(parents=True, exist_ok=True)

    manifest_path = src_dir / "manifest.json"
    if not manifest_path.exists():
        print(f"錯誤:找不到 {manifest_path}", file=sys.stderr)
        return 1

    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    n_chunks = len(manifest["chunks"])

    print(f"目標: {args.target_total_mb} MB 總計, {n_chunks} 分塊")
    print(f"  每塊 ~{args.faces_per_chunk} 面(合併後),平面色材質(無紋理)")
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
