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
                let color = biomeColor(forHeight: h, topLayerIndex: topLayer)

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

    private static func biomeColor(forHeight h: Float, topLayerIndex: Int) -> SIMD3<Float> {
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

    /// 將多個 box 合併為單一 SCNGeometry,以顏色緩衝提供 per-voxel 著色。
    private static func mergedBoxGeometry(
        boxes: [(center: SIMD3<Float>, size: SIMD3<Float>, color: SIMD3<Float>)]
    ) -> SCNGeometry {
        // 每個 box = 6 面 × 4 頂點;SCN box 的 per-face 頂點以常數色。
        // 為簡化,改用 SCNCapsule? 否 —— 以 SCNGeometrySource + Element 手建。
        // 採六面 cuboid 手動鋪頂點,確保 per-voxel 顏色一致。
        var positions: [SCNVector3] = []
        var colors: [SCNVector3] = []
        var indices: [Int32] = []

        // 立方體 8 角(以中心 + 半尺寸)。
        let half = SIMD2<Float>(0.5, 0.5)
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
            let hx = b.size.x * half.x
            let hy = b.size.y * half.x
            let hz = b.size.z * half.y   // half.y 未使用,對稱取 0.5
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

            let baseIdx = Int32(positions.count)
            // 每面用 4 個獨立頂點(避免法線/顏色共用造成的縫),並加入顏色。
            for f in faces {
                let v0 = corners[f.0], v1 = corners[f.1], v2 = corners[f.2], v3 = corners[f.3]
                positions.append(SCNVector3(v0.x, v0.y, v0.z))
                positions.append(SCNVector3(v1.x, v1.y, v1.z))
                positions.append(SCNVector3(v2.x, v2.y, v2.z))
                positions.append(SCNVector3(v3.x, v3.y, v3.z))
                for _ in 0..<4 {
                    colors.append(SCNVector3(b.color.x, b.color.y, b.color.z))
                }
                let s = baseIdx + Int32(positions.count) - 4
                indices.append(contentsOf: [s, s + 1, s + 2, s, s + 2, s + 3])
            }
        }

        let posSource = SCNGeometrySource(
            semantic: .vertex,
            vectorCount: positions.count,
            usesFloatComponents: true,
            componentsPerVector: 3,
            bytesPerComponent: MemoryLayout<SCNVector3>.stride / 3,
            offset: 0,
            stride: MemoryLayout<SCNVector3>.stride,
            data: Data(bytes: positions, count: positions.count * MemoryLayout<SCNVector3>.stride)
        )
        let colSource = SCNGeometrySource(
            semantic: .color,
            vectorCount: colors.count,
            usesFloatComponents: true,
            componentsPerVector: 3,
            bytesPerComponent: MemoryLayout<SCNVector3>.stride / 3,
            offset: 0,
            stride: MemoryLayout<SCNVector3>.stride,
            data: Data(bytes: colors, count: colors.count * MemoryLayout<SCNVector3>.stride)
        )

        let element = SCNGeometryElement(
            indices: indices,
            primitiveType: .triangles
        )

        return SCNGeometry(sources: [posSource, colSource], elements: [element])
    }
}
