#!/usr/bin/env python3
"""
Stage A 驗證閘道:硬性檢查分塊輸出是否符合大小預算。
若任一 .glb > max-chunk-mb 或總和 > max-total-mb,以非零退出(供 CI gate 使用)。

用法:
  python validate_chunks.py --output /path/to/chunks \\
    [--max-chunk-mb 10] [--max-total-mb 200] [--manifest manifest.json]
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser(description="分塊大小驗證閘道")
    p.add_argument("--output", required=True, help="分塊輸出目錄")
    p.add_argument("--manifest", default=None, help="manifest.json 路徑(預設 <output>/manifest.json)")
    p.add_argument("--max-chunk-mb", type=float, default=10.0)
    p.add_argument("--max-total-mb", type=float, default=200.0)
    args = p.parse_args()

    output = Path(args.output).resolve()
    manifest_path = Path(args.manifest) if args.manifest else output / "manifest.json"

    if not manifest_path.exists():
        print(f"❌ 找不到 manifest: {manifest_path}", file=sys.stderr)
        return 2

    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

    chunks = manifest.get("chunks", [])
    if not chunks:
        print("❌ manifest 無分塊記錄", file=sys.stderr)
        return 2

    max_chunk_bytes = int(args.max_chunk_mb * 1_000_000)
    max_total_bytes = int(args.max_total_mb * 1_000_000)

    errors: list[str] = []
    warnings: list[str] = []

    print(f"=== 驗證 {len(chunks)} 個分塊 ===")
    print(f"預算: 單塊 ≤ {args.max_chunk_mb} MB,總和 ≤ {args.max_total_mb} MB")
    print()

    total = 0
    for c in chunks:
        f_name = c.get("file", "")
        f_path = output / f_name
        # 同時檢查 manifest 記錄與實際檔案大小
        manifest_bytes = c.get("bytes", 0)
        actual_bytes = f_path.stat().st_size if f_path.exists() else 0
        total += actual_bytes

        chunk_id = c.get("id", "?")
        status = "✓"
        if actual_bytes > max_chunk_bytes:
            status = "✗ 超出單塊預算"
            errors.append(f"{chunk_id}: {actual_bytes/1e6:.2f} MB > {args.max_chunk_mb} MB")
        elif actual_bytes > max_chunk_bytes * 0.9:
            status = "⚠ 接近上限"
            warnings.append(f"{chunk_id}: {actual_bytes/1e6:.2f} MB (90% 上限)")

        if actual_bytes != manifest_bytes:
            warnings.append(f"{chunk_id}: manifest 記錄 {manifest_bytes/1e6:.2f} MB 與實際 {actual_bytes/1e6:.2f} MB 不符")

        if not f_path.exists():
            errors.append(f"{chunk_id}: 檔案不存在 {f_path}")
            status = "✗ 檔案缺失"

        print(f"  {chunk_id:14s}  {actual_bytes/1e6:7.2f} MB  {status}")

    print()
    print(f"總和: {total/1e6:.2f} MB / {args.max_total_mb} MB", end="")
    if total > max_total_bytes:
        print("  ✗ 超出總預算")
        errors.append(f"總和 {total/1e6:.2f} MB > {args.max_total_mb} MB")
    else:
        print("  ✓")

    # 結構完整性檢查
    for c in chunks:
        for key in ("id", "grid_x", "grid_y", "bbox_min", "bbox_max",
                    "centroid", "file", "bytes", "triangles", "neighbors"):
            if key not in c:
                errors.append(f"{c.get('id','?')}: 缺少欄位 {key}")

    print()
    if warnings:
        print(f"⚠ {len(warnings)} 項警告:")
        for w in warnings:
            print(f"  {w}")
    if errors:
        print(f"❌ {len(errors)} 項錯誤,驗證失敗:", file=sys.stderr)
        for e in errors:
            print(f"  {e}", file=sys.stderr)
        return 1

    print("✅ 驗證通過")
    return 0


if __name__ == "__main__":
    sys.exit(main())
