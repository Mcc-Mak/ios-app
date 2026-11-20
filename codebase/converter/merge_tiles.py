#!/usr/bin/env python3
"""
merge_tiles.py — 合併多個圖幅的分塊為單一分塊集 + 統一 manifest。

每個圖幅的 .glb 分塊已由 convert_osgb_to_chunks.py 個別置中至該圖幅的 centroid。
本腳本:
  1. 讀取各圖幅的 manifest.json(含 origin/centroid)
  2. 以第一個圖幅的 origin 為全域原點,計算各圖幅的偏移量
  3. 重新命名 .glb(前綴圖幅代號避免衝突,雖然實測座標不衝突)並複製到輸出目錄
  4. 改寫每塊的 centroid(加上圖幅偏移)、合併 manifest

輸出結構與單一圖幅相同:<output>/manifest.json + <output>/*.glb

用法:
  python merge_tiles.py --tiles 15A:/path/to/15A 15B:/path/to/15B ... --output /path/to/merged
"""
import argparse
import json
import shutil
import sys
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser(description="合併多圖幅分塊為單一 manifest + .glb 集")
    p.add_argument("--tiles", nargs="+", required=True,
                   help="格式: <TileCode>:<chunks_dir>(含 manifest.json + .glb)")
    p.add_argument("--output", required=True, help="合併輸出目錄")
    p.add_argument("--source-name", default="11-NW-15-merged",
                   help="合併後的 source 名稱(預設 11-NW-15-merged)")
    args = p.parse_args()

    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)

    # 解析 --tiles
    tiles = []
    for spec in args.tiles:
        if ":" not in spec:
            print(f"ERROR: --tiles 格式須為 Code:Path,收到 {spec!r}", file=sys.stderr)
            return 1
        code, path = spec.split(":", 1)
        tiles.append((code, Path(path)))

    # 驗證 + 載入
    manifests = []
    for code, d in tiles:
        mf = d / "manifest.json"
        if not mf.exists():
            print(f"ERROR: {code} 的 manifest.json 不存在: {mf}", file=sys.stderr)
            return 1
        m = json.loads(mf.read_text(encoding="utf-8"))
        manifests.append((code, d, m))
        print(f"  {code}: {m['chunkCount']} chunks, origin={m.get('origin')}")

    # 全域原點 = 第一個圖幅的 origin
    global_origin = manifests[0][2]["origin"]
    print(f"\n全域原點(= 第一圖幅 origin): {global_origin}")

    # 合併
    all_chunks = []
    total_bytes = 0
    total_tris = 0
    glb_count = 0

    for code, d, m in manifests:
        origin = m["origin"]
        # 圖幅偏移量 = 此圖幅 origin - 全域原點(HK 網格座標,X=東,Y=北,Z=高)
        # GLB 頂點已相對此圖幅 origin 置中,故容器節點需設於 positionOffset
        # 以正確定位多圖幅(否則全部疊在原點)
        offset = [origin[i] - global_origin[i] for i in range(3)]

        for c in m["chunks"]:
            # 重新命名 .glb(前綴圖幅代號,雖然座標不衝突,但更清晰)
            old_glb = d / c["file"]
            new_name = f"{code}_{c['id']}.glb"
            new_glb = out / new_name
            if old_glb.exists():
                shutil.copy2(old_glb, new_glb)
                glb_count += 1
            else:
                print(f"WARNING: {old_glb} 不存在,跳過", file=sys.stderr)

            # centroid = 本地座標 + 圖幅偏移 = 全域座標(供 ChunkStreamer 距離計算)
            # positionOffset = 圖幅偏移(供 ChunkStreamer 容器節點定位)
            # 兩者分離:centroid 用於「該載入哪些塊」,positionOffset 用於「塊放在哪裡」
            local_centroid = c.get("centroid", [0, 0, 0])
            global_centroid = [local_centroid[i] + offset[i] for i in range(3)]

            merged_chunk = dict(c)
            merged_chunk["id"] = f"{code}_{c['id']}"
            merged_chunk["file"] = new_name
            merged_chunk["centroid"] = global_centroid   # 全域座標(距離計算用)
            merged_chunk["positionOffset"] = offset      # 圖幅偏移(渲染定位用)
            # bbox 也用全域座標(供 BoundingRadius 計算,與 centroid 一致)
            if "bbox_min" in c:
                merged_chunk["bbox_min"] = [c["bbox_min"][i] + offset[i] for i in range(3)]
            if "bbox_max" in c:
                merged_chunk["bbox_max"] = [c["bbox_max"][i] + offset[i] for i in range(3)]
            merged_chunk["tile"] = code
            all_chunks.append(merged_chunk)
            total_bytes += c.get("bytes", 0)
            total_tris += c.get("triangles", 0)

    # 合併 manifest
    merged_manifest = {
        "version": "1.0",
        "source": args.source_name,
        "crs": manifests[0][2].get("crs", "EPSG:2326"),
        "origin": global_origin,
        "tileSize": [750, 600, 150],  # 近似值(各圖幅略有差異)
        "gridCols": 10,  # 2 圖幅 × 5 cols
        "gridRows": 8,   # 2 圖幅 × 4 rows
        "chunkCount": len(all_chunks),
        "totalBytes": total_bytes,
        "totalTriangles": total_tris,
        "attribution": manifests[0][2].get("attribution", ""),
        "tiles": [code for code, _, _ in manifests],
        "chunks": all_chunks,
    }

    mf_out = out / "manifest.json"
    mf_out.write_text(json.dumps(merged_manifest, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n=== 合併完成 ===")
    print(f"  圖幅數: {len(tiles)}")
    print(f"  分塊數: {len(all_chunks)}")
    print(f"  .glb 檔: {glb_count}")
    print(f"  總大小: {total_bytes / 1e6:.2f} MB")
    print(f"  總三角面: {total_tris:,}")
    print(f"  輸出: {out}")
    print(f"  manifest: {mf_out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
