//
//  FlyCameraController.swift
//  HKMinecraftVoxel
//
//  飛行攝影機:環繞地形上空自動巡航,可由 HUD 暫停/繼續。
//
//  支援兩種場景:
//    1. 程序化香港體素地形(VoxelSceneFactory)— 原有功能。
//    2. CSDI 旺角真實網格(CSDIMongKokSceneFactory)— 新增,
//       攝影機以 300 m AGL 貼地巡航,利用射線向下取樣地面高度,
//       確保不會穿入建築物。
//

import Foundation
import SceneKit
import SwiftUI
import UIKit

/// 場景模式。
enum SceneMode: String, CaseIterable {
    case proceduralHK = "程序化香港"
    case csdiMongKok = "旺角實景(CSDI)"
}

final class FlyCameraController: ObservableObject {

    /// 當前巡航高度。對 CSDI 模式為 AGL(離地高度);對程序化模式為絕對高度。
    @Published var altitude: Float = 380
    @Published var orbiting: Bool = true
    @Published var sceneMode: SceneMode = .csdiMongKok {
        didSet { rebuildScene() }
    }

    /// 由 controller 持有的場景(攝影機節點已加入 scene graph)。
    private(set) var scene: SCNScene
    let cameraNode: SCNNode

    /// CSDI 模式下的網格根節點(用於射線取樣地面高度)。
    private var meshRootNode: SCNNode?

    private var displayLink: CADisplayLink?
    private var angle: Float = 0
    private let center = SIMD3<Float>(0, 0, 0)
    private let orbitRadius: Float = 450

    /// CSDI 模式巡航 AGL(離地高度)。
    private let cruiseAGL: Float = 300

    init() {
        cameraNode = SCNNode()
        cameraNode.camera = SCNCamera()
        cameraNode.camera?.fieldOfView = 60
        cameraNode.camera?.zFar = 8000

        // 初始場景:CSDI 旺角實景
        let (s, mesh) = CSDIMongKokSceneFactory.makeMongKokScene()
        scene = s
        meshRootNode = mesh
        scene.rootNode.addChildNode(cameraNode)
        altitude = cruiseAGL
        applyPose()
    }

    /// 依 sceneMode 重建場景。
    private func rebuildScene() {
        // 停止舊 displayLink
        orbiting = false
        displayLink?.invalidate()
        displayLink = nil

        // 移除舊攝影機節點(會在加入新場景時重新建構)
        cameraNode.removeFromParentNode()

        switch sceneMode {
        case .csdiMongKok:
            let (s, mesh) = CSDIMongKokSceneFactory.makeMongKokScene()
            scene = s
            meshRootNode = mesh
            altitude = cruiseAGL
            orbitRadius = 450
        case .proceduralHK:
            scene = VoxelSceneFactory.makeHongKongScene()
            meshRootNode = nil
            altitude = 380
            orbitRadius = 1400
        }

        scene.rootNode.addChildNode(cameraNode)
        angle = 0
        applyPose()
        startOrbit()
    }

    /// 開始自動巡航(於檢視出現時呼叫)。
    func startOrbit() {
        guard displayLink == nil else { return }
        orbiting = true
        let link = CADisplayLink(target: self, selector: #selector(tick))
        link.preferredFrameRateRange = CAFrameRateRange(minimum: 30, maximum: 60, preferred: 60)
        link.add(to: .main, forMode: .common)
        displayLink = link
    }

    /// 停止自動巡航(使用者開始手動操控時呼叫)。
    func stopOrbit() {
        orbiting = false
        displayLink?.invalidate()
        displayLink = nil
    }

    @objc private func tick() {
        guard orbiting else { return }
        angle += 0.0025
        applyPose()
    }

    private func applyPose() {
        let x = center.x + orbitRadius * cosf(angle)
        let z = center.z + orbitRadius * sinf(angle)

        let y: Float
        if sceneMode == .csdiMongKok {
            // 300 m AGL:取樣攝影機正下方地面高度,加上巡航 AGL。
            let groundY = sampleGroundHeight(belowX: x, z: z)
            y = groundY + altitude
        } else {
            y = altitude
        }

        cameraNode.position = SCNVector3(x, y, z)
        cameraNode.look(at: SCNVector3(center.x, sceneMode == .csdiMongKok ? altitude * 0.6 : 120, center.z))
    }

    /// 以射線向下取樣 (x, z) 位置的最高實體表面高度。
    /// 對 CSDI 網格從攝影機預期高度上方 500 m 向下射線,取第一個命中點。
    private func sampleGroundHeight(belowX x: Float, z: Float) -> Float {
        guard let mesh = meshRootNode else { return 0 }

        // 從高空向下射線(尋找建築物頂或地面)。座標為世界座標
        // (網格未套用 transform,本地座標 = 世界座標)。
        let from = SCNVector3(x, 1000, z)
        let to = SCNVector3(x, -200, z)

        let results = mesh.hitTest(withSegmentFrom: from, to: to)
        if let first = results.first {
            return first.worldCoordinates.y
        }
        // 若射線未命中(應僅在圖幅邊緣外),回退至 0(海平面)。
        return 0
    }
}

// MARK: - HUD

struct FlyHUD: View {
    @ObservedObject var controller: FlyCameraController

    var body: some View {
        VStack(spacing: 8) {
            Picker("場景", selection: $controller.sceneMode) {
                ForEach(SceneMode.allCases, id: \.self) { mode in
                    Text(mode.rawValue).tag(mode)
                }
            }
            .pickerStyle(.segmented)

            HStack(spacing: 16) {
                Label(String(format: "%.0f m", controller.altitude),
                      systemImage: "arrow.up.and.down")
                Button {
                    controller.orbiting ? controller.stopOrbit() : controller.startOrbit()
                } label: {
                    Label(controller.orbiting ? "巡航中" : "已暫停",
                          systemImage: controller.orbiting ? "pause.circle" : "play.circle")
                }
                .buttonStyle(.bordered)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.ultraThinMaterial, in: Capsule())
        .onAppear { controller.startOrbit() }
        .onDisappear { controller.stopOrbit() }
    }
}
