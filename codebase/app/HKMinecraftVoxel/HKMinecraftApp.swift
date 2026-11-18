//
//  HKMinecraftApp.swift
//  HKMinecraftVoxel
//
//  香港體素飛覽 App(HK Minecraft Voxel Flythrough)
//
//  以 SceneKit + Metal 在 iPhone 上以 Minecraft 體素風格呈現香港地形,
//  地形高度資料來源可插拔(預設:程序化 HK 地形;可擴充:HK CSDI 開放資料 DEM)。
//
//  以原生 Swift 撰寫,以便沿用既有 xcodebuild + GITHUB_TOKEN 未簽署建置管線,
//  無需 Unity 授權或 Cesium ion 權杖。
//

import SwiftUI

@main
struct HKMinecraftApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
