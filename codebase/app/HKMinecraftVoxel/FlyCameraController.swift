//
//  FlyCameraController.swift
//  HKMinecraftVoxel
//
//  飛行攝影機:環繞香港地形上空,可由使用者在 SceneView 中拖曳/縮放
//  (.allowsCameraControl)。此 controller 提供一個自動巡航的起始視角與
//  HUD 顯示的狀態。
//

import Foundation
import SceneKit
import SwiftUI

final class FlyCameraController: ObservableObject {

    @Published var altitude: Float = 380
    @Published var orbiting: Bool = true

    let cameraNode: SCNNode

    private var displayLink: CADisplayLink?
    private var angle: Float = 0
    private let center = SIMD3<Float>(0, 0, 0)
    private let orbitRadius: Float = 1400

    init() {
        cameraNode = SCNNode()
        cameraNode.camera = SCNCamera()
        cameraNode.camera?.fieldOfView = 60
        cameraNode.camera?.zFar = 6000
        applyPose()
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
        cameraNode.position = SCNVector3(x, altitude, z)
        cameraNode.look(at: SCNVector3(center.x, 120, center.z))
    }
}

// MARK: - HUD

struct FlyHUD: View {
    @ObservedObject var controller: FlyCameraController

    var body: some View {
        HStack(spacing: 16) {
            Label(String(format: "%.0f m", controller.altitude), systemImage: "arrow.up.and.down")
            Button {
                controller.orbiting ? controller.stopOrbit() : controller.startOrbit()
            } label: {
                Label(controller.orbiting ? "巡航中" : "已暫停",
                      systemImage: controller.orbiting ? "pause.circle" : "play.circle")
            }
            .buttonStyle(.bordered)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.ultraThinMaterial, in: Capsule())
        .onAppear { controller.startOrbit() }
        .onDisappear { controller.stopOrbit() }
    }
}
