//
//  ContentView.swift
//  HKMinecraftVoxel
//
//  入口檢視:載入 SceneKit 體素場景並提供飛行操控覆蓋層。
//

import SceneKit
import SwiftUI

struct ContentView: View {
    @StateObject private var controller = FlyCameraController()

    var body: some View {
        ZStack(alignment: .bottom) {
            // 體素場景。
            SceneView(
                scene: VoxelSceneFactory.makeHongKongScene(),
                pointOfView: controller.cameraNode,
                options: [.allowsCameraControl, .autoenablesDefaultLighting, .antialiased]
            )
            .ignoresSafeArea()

            // 飛行操控提示與狀態覆蓋層。
            FlyHUD(controller: controller)
                .padding()
        }
    }
}

#Preview {
    ContentView()
}
