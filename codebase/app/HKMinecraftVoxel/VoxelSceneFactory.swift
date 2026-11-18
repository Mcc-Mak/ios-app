//
//  VoxelSceneFactory.swift
//  HKMinecraftVoxel
//
//  組裝 SceneKit 場景:體素地形 + 天空背景 + 環境光 + 攝影機。
//

import Foundation
import SceneKit
import UIKit

enum VoxelSceneFactory {

    /// 建立香港體素飛覽場景。
    static func makeHongKongScene() -> SCNScene {
        let scene = SCNScene()

        // 背景天空(漸層感,以純色近似)。
        scene.background.contents = UIColor(
            red: 0.53, green: 0.72, blue: 0.92, alpha: 1.0
        )

        // 地形(程序化 HK)。
        let terrain = ProceduralHKTerrain()
        let terrainNode = VoxelMeshBuilder.buildNode(from: terrain)
        scene.rootNode.addChildNode(terrainNode)

        // 光照:環境光 + 一個方向光(太陽)。
        let ambient = SCNLight()
        ambient.type = .ambient
        ambient.color = UIColor(white: 0.75, alpha: 1.0)
        let ambientNode = SCNNode()
        ambientNode.light = ambient
        scene.rootNode.addChildNode(ambientNode)

        let sun = SCNLight()
        sun.type = .directional
        sun.color = UIColor(white: 0.95, alpha: 1.0)
        sun.intensity = 1200
        let sunNode = SCNNode()
        sunNode.light = sun
        sunNode.eulerAngles = SCNVector3(-Float.pi / 4, Float.pi / 6, 0)
        scene.rootNode.addChildNode(sunNode)

        // 霧:強化縱深與「香港煙霞」感。
        scene.fogStartDistance = 400
        scene.fogEndDistance = 2400
        scene.fogColor = UIColor(red: 0.78, green: 0.85, blue: 0.92, alpha: 1.0)

        return scene
    }
}
