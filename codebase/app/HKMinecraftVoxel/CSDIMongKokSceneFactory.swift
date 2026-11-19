//
//  CSDIMongKokSceneFactory.swift
//  HKMinecraftVoxel
//
//  載入香港地政總署(Lands Department)3D 視覺化地圖之旺角圖幅(11-NW-19B),
//  以 ModelIO/SceneKit 直接顯示真實 CSDI 網格資料(非程序化、非猜測)。
//
//  資料來源:LandsD 3D Visualisation Map (Tile-based models),OSGB 根 LOD,
//  經離線轉換(osgconv → OBJ → trimesh/pyfqmr 簡化)為 glTF (.glb),
//  座標系 EPSG:2326(HK 1980 Grid),已重新置中至圖幅中心(本地座標)。
//  授權:香港特別行政區地政總署開放資料(需標註來源)。
//

import Foundation
import SceneKit
import ModelIO
import Metal
import UIKit

/// 旺角 CSDI 真實網格場景工廠。載入 bundled glb,組裝 SceneKit 場景。
enum CSDIMongKokSceneFactory {

    /// 圖幅在本地座標系中的尺寸(公尺),用於攝影機與巡航設定。
    /// 來自 mongkok_11-NW-19B_summary.json:size_m = [751.28, 600.49, 257.94]
    static let tileSize = SIMD3<Float>(751.28, 257.94, 600.49)

    /// 攝影機離地高度(公尺,AGL — Above Ground Level)。
    static let cruiseAltitudeAGL: Float = 300

    /// 建立旺角 CSDI 場景。回傳 (scene, meshRootNode) — meshRootNode 用於射線取樣地面高度。
    static func makeMongKokScene() -> (SCNScene, SCNNode) {
        let scene = SCNScene()

        // 天空背景(旺角黃昏感)。
        scene.background.contents = UIColor(
            red: 0.62, green: 0.74, blue: 0.88, alpha: 1.0
        )

        // 載入 bundled glb(置於 Resources/mongkok_11-NW-19B.glb)
        let meshNode = loadMongKokMesh()
        scene.rootNode.addChildNode(meshNode)

        // 光照:環境光 + 方向光(太陽,低角度模擬黃昏)。
        let ambient = SCNLight()
        ambient.type = .ambient
        ambient.color = UIColor(white: 0.65, alpha: 1.0)
        let ambientNode = SCNNode()
        ambientNode.light = ambient
        scene.rootNode.addChildNode(ambientNode)

        let sun = SCNLight()
        sun.type = .directional
        sun.color = UIColor(red: 1.0, green: 0.92, blue: 0.78, alpha: 1.0)
        sun.intensity = 1400
        let sunNode = SCNNode()
        sunNode.light = sun
        sunNode.eulerAngles = SCNVector3(-Float.pi / 5, Float.pi / 4, 0)
        scene.rootNode.addChildNode(sunNode)

        // 霧:強化縱深,遮蔽遠處圖幅邊緣。
        scene.fogStartDistance = 500
        scene.fogEndDistance = 2000
        scene.fogColor = UIColor(red: 0.72, green: 0.80, blue: 0.88, alpha: 1.0)

        return (scene, meshNode)
    }

    /// 載入 glb 並轉為 SCNNode。ModelIO 可直接讀 glTF 2.0(.glb),
    /// 再以 SCNNode(mdlObject:) 橋接至 SceneKit。
    private static func loadMongKokMesh() -> SCNNode {
        guard let url = Bundle.main.url(forResource: "mongkok_11-NW-19B", withExtension: "glb") else {
            // glb 找不到時回傳一個紅色提示節點(開發期診斷用)。
            return makeErrorNode(color: .systemRed, name: "CSDIMeshMissing")
        }

        // ModelIO 讀 glb → MDLAsset。bufferAllocator 用 Metal kernel(可為 nil,ModelIO 會用預設)。
        let allocator: MDLMeshBufferAllocator?
        if let device = MTLCreateSystemDefaultDevice() {
            allocator = MTKMeshBufferAllocator(device: device)
        } else {
            allocator = nil
        }

        let asset = MDLAsset(url: url, vertexDescriptor: nil, bufferAllocator: allocator)

        // 將 MDLAsset 的所有頂層物件橋接為 SCNNode 子節點
        let root = SCNNode()
        root.name = "CSDIMongKokMesh"
        for i in 0..<asset.count {
            let mdlObj = asset.object(at: i)
            let scnNode = SCNNode(mdlObject: mdlObj)
            root.addChildNode(scnNode)
        }

        if root.childNodes.isEmpty {
            return makeErrorNode(color: .systemYellow, name: "CSDIMeshEmpty")
        }

        // glb 座標為公尺(本地座標,已置中)。300 m AGL 巡航直接使用真實公尺。
        return root
    }

    /// 產生診斷用的彩色方塊節點(glb 載入失敗時)。
    private static func makeErrorNode(color: UIColor, name: String) -> SCNNode {
        let node = SCNNode()
        let geom = SCNBox(width: 50, height: 50, length: 50, chamferRadius: 0)
        geom.firstMaterial?.diffuse.contents = color
        node.geometry = geom
        node.name = name
        return node
    }
}
