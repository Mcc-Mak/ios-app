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
            // 體素場景(由 controller 持有;攝影機節點已加入場景圖)。
            // 反鋸齒沿用 SceneView 預設的 .multisampling4X;
            // SceneView.Options 並沒有 .antialiased 成員。
            SceneView(
                scene: controller.scene,
                pointOfView: controller.cameraNode,
                options: [.autoenablesDefaultLighting]
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
