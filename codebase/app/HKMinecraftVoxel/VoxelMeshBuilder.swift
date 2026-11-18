//
//  VoxelMeshBuilder.swift
//  HKMinecraftVoxel
//
//  將 TerrainDataSource 轉成 SceneKit 體素幾何。
//
//  策略:對每個 (x,z) 網格柱,依高度堆疊立方體;但僅暴露「頂層 + 邊緣外露層」,
//  並以單一 SCNGeometry 合併所有 box 來降低 draw call。
//  著色採 Minecraft 風格的高度分帶(水/沙/草/岩/雪),材質為無光澤常數色 +
//  per-face 著色,營造方塊感。
//

import Foundation
import SceneKit
import UIKit
import simd

enum VoxelMeshBuilder {

    /// 由地形資料建立合併後的體素節點。
    /// - Parameter data: 地形資料來源
    /// - Parameter baseY: 海平面 Y 座標(公尺)
    static func buildNode(from data: TerrainDataSource, baseY: Float = 0) -> SCNNode {
        let s = data.voxelSize
        let gw = data.gridWidth
        let gd = data.gridDepth

        // 收集所有體素盒(世界座標中心 + 高度分帶顏色)。
        var boxes: [(center: SIMD3<Float>, size: SIMD3<Float>, color: SIMD3<Float>)] = []
        boxes.reserveCapacity(gw * gd * 2)   // 頂層 + 一層基底,粗估

        for x in 0..<gw {
            for z in 0..<gd {
                let h = data.height(atX: x, z: z)
                let topVoxels = max(Int((h / s).rounded()), 1)
                let topLayer = topVoxels - 1
                let color = biomeColor(forHeight: h)

                // 頂層體素(可見)。
                let topY = baseY + s * (Float(topLayer) + 0.5)
                let cx = (Float(x) - Float(gw) / 2) * s
                let cz = (Float(z) - Float(gd) / 2) * s
                boxes.append((
                    center: SIMD3(cx, topY, cz),
                    size: SIMD3(s, s, s),
                    color: color
                ))

                // 基底體素(海平面下/地下),避免從側面看穿。
                let baseYc = baseY - s * 0.5
                boxes.append((
                    center: SIMD3(cx, baseYc, cz),
                    size: SIMD3(s, s, s),
                    color: SIMD3(0.30, 0.26, 0.20)   // 深土
                ))

                // 水面:海平面 + 微透明藍平板(高度低於水位時填補)。
                if h < s {
                    let waterY = baseY + s * 0.4
                    boxes.append((
                        center: SIMD3(cx, waterY, cz),
                        size: SIMD3(s, s * 0.8, s),
                        color: SIMD3(0.15, 0.35, 0.70)
                    ))
                }
            }
        }

        let geometry = mergedBoxGeometry(boxes: boxes)
        let material = SCNMaterial()
        material.lightingModel = .constant          // Minecraft 平塗感
        material.isDoubleSided = false
        material.diffuse.contents = UIColor.white
        geometry.materials = [material]

        let node = SCNNode(geometry: geometry)
        node.name = "voxelTerrain"
        return node
    }

    // MARK: - 著色分帶

    private static func biomeColor(forHeight h: Float) -> SIMD3<Float> {
        // 高度分帶(公尺):水/沙/草/岩/雪。
        switch h {
        case ..<5:    return SIMD3(0.85, 0.82, 0.55)   // 沙(海岸)
        case ..<180:  return SIMD3(0.32, 0.58, 0.24)   // 草(低地)
        case ..<400:  return SIMD3(0.28, 0.45, 0.20)   // 林(中坡)
        case ..<700:  return SIMD3(0.45, 0.40, 0.36)   // 岩(高坡)
        default:      return SIMD3(0.92, 0.92, 0.95)   // 雪(山頂)
        }
    }

    // MARK: - 幾何合併

    /// 將多個 box 合併為單一 SCNGeometry,以頂點色緩衝提供 per-voxel 著色。
    private static func mergedBoxGeometry(
        boxes: [(center: SIMD3<Float>, size: SIMD3<Float>, color: SIMD3<Float>)]
    ) -> SCNGeometry {
        // 六面 cuboid 手動鋪頂點:每面 4 個獨立頂點,避免法線/顏色共用造成接縫。
        // 以扁平 [Float] 存放,確保資料佈局(stride = 3 × 4 bytes)與
        // SCNGeometrySource 的宣告一致;SCNVector3 的 stride 為 16 bytes,不可直接使用。
        var positions: [Float] = []
        var colors: [Float] = []
        var indices: [Int32] = []

        // 立方體 8 角(以中心 + 半尺寸)。
        let half: Float = 0.5
        // 六面索引(每面 4 角,逆時針)。
        let faces: [(Int, Int, Int, Int)] = [
            (0, 1, 2, 3), // +X
            (5, 4, 7, 6), // -X
            (4, 0, 3, 7), // +Y
            (1, 5, 6, 2), // -Y
            (4, 5, 1, 0), // +Z
            (3, 2, 6, 7), // -Z
        ]

        for b in boxes {
            let hx = b.size.x * half
            let hy = b.size.y * half
            let hz = b.size.z * half
            let c = b.center

            // 8 角。
            let corners: [SIMD3<Float>] = [
                SIMD3(c.x + hx, c.y - hy, c.z + hz), // 0
                SIMD3(c.x + hx, c.y - hy, c.z - hz), // 1
                SIMD3(c.x + hx, c.y + hy, c.z - hz), // 2
                SIMD3(c.x + hx, c.y + hy, c.z + hz), // 3
                SIMD3(c.x - hx, c.y - hy, c.z + hz), // 4
                SIMD3(c.x - hx, c.y - hy, c.z - hz), // 5
                SIMD3(c.x - hx, c.y + hy, c.z - hz), // 6
                SIMD3(c.x - hx, c.y + hy, c.z + hz), // 7
            ]

            // 每面用 4 個獨立頂點(避免法線/顏色共用造成的縫),並加入顏色。
            for f in faces {
                let base = Int32(positions.count / 3)
                let v0 = corners[f.0], v1 = corners[f.1], v2 = corners[f.2], v3 = corners[f.3]
                positions.append(contentsOf: [v0.x, v0.y, v0.z])
                positions.append(contentsOf: [v1.x, v1.y, v1.z])
                positions.append(contentsOf: [v2.x, v2.y, v2.z])
                positions.append(contentsOf: [v3.x, v3.y, v3.z])
                for _ in 0..<4 {
                    colors.append(contentsOf: [b.color.x, b.color.y, b.color.z])
                }
                indices.append(contentsOf: [base, base + 1, base + 2, base, base + 2, base + 3])
            }
        }

        let posSource = geometrySource(positions, semantic: .vertex)
        let colSource = geometrySource(colors, semantic: .color)

        let element = SCNGeometryElement(
            indices: indices,
            primitiveType: .triangles
        )

        return SCNGeometry(sources: [posSource, colSource], elements: [element])
    }

    /// 由扁平 Float 緩衝建立 3 分量浮點幾何來源。
    /// - Parameters:
    ///   - values: 連續的 xyz 分量
    ///   - semantic: `.vertex` 或 `.color`
    private static func geometrySource(
        _ values: [Float],
        semantic: SCNGeometrySource.Semantic
    ) -> SCNGeometrySource {
        let data = values.withUnsafeBufferPointer { Data(buffer: $0) }
        return SCNGeometrySource(
            data: data,
            semantic: semantic,
            vectorCount: values.count / 3,
            usesFloatComponents: true,
            componentsPerVector: 3,
            bytesPerComponent: MemoryLayout<Float>.size,
            dataOffset: 0,
            dataStride: MemoryLayout<Float>.size * 3
        )
    }
}
