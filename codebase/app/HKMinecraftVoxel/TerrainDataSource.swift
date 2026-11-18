//
//  TerrainDataSource.swift
//  HKMinecraftVoxel
//
//  地形高度資料來源協定。實作可插拔:
//    - ProceduralHKTerrain:程序化生成香港主要山脊輪廓(離線、無需網路/權杖)。
//    - CSDIDEMTerrain(未來):自香港 CSDI 開放資料 DEM 下載真實高度。
//
//  抽象層讓 App 在無 Cesium ion、無網路的情況下仍可運作,
//  並保留未來接入真實 HK CSDI 資料的擴充點。
//

import Foundation
import simd

/// 體素地形資料來源。回傳二維高度陣列(列優先,height[x][z])。
protocol TerrainDataSource {
    /// 網格於 X/Z 軸的體素數量。
    var gridWidth: Int { get }
    var gridDepth: Int { get }

    /// 每個體素的實際尺寸(公尺)。
    var voxelSize: Float { get }

    /// 取得指定座標的高度(公尺,相對海平面)。
    func height(atX x: Int, z: Int) -> Float
}

/// 程序化香港地形:以多個高斯峰模擬太平山、鳳凰山、大帽山等主要山脊。
/// 純離線、確定性(同一 seed 永遠產生相同地形),便於測試與重現建置。
struct ProceduralHKTerrain: TerrainDataSource {
    let gridWidth: Int
    let gridDepth: Int
    let voxelSize: Float

    /// 山峰定義(相對網格座標 0..1、高度公尺、寬度)。
    private struct Peak {
        let nx: Float       // 標準化 X(0..1)
        let nz: Float       // 標準化 Z(0..1)
        let height: Float   // 公尺
        let sigma: Float    // 高斯寬度(標準化)
    }

    private let peaks: [Peak]

    /// 建立。`seed` 控制細節雜訊,主要山脊位置固定以保持「香港感」。
    init(gridWidth: Int = 96, gridDepth: Int = 96, voxelSize: Float = 40.0, seed: UInt64 = 20261118) {
        self.gridWidth = gridWidth
        self.gridDepth = gridDepth
        self.voxelSize = voxelSize

        // 香港主要山峰的相對位置(簡化、非真實經緯度,僅求輪廓辨識度)。
        peaks = [
            Peak(nx: 0.30, nz: 0.28, height: 552, sigma: 0.10), // 太平山
            Peak(nx: 0.66, nz: 0.62, height: 957, sigma: 0.12), // 大帽山
            Peak(nx: 0.78, nz: 0.85, height: 934, sigma: 0.10), // 鳳凰山
            Peak(nx: 0.45, nz: 0.72, height: 702, sigma: 0.09), // 馬鞍山
            Peak(nx: 0.20, nz: 0.60, height: 495, sigma: 0.08), // 柏架山
            Peak(nx: 0.55, nz: 0.40, height: 312, sigma: 0.07), // 獅子山
        ]

        rng = SplitMix64(seed: seed)
    }

    private var rng: SplitMix64

    func height(atX x: Int, z: Int) -> Float {
        let nx = Float(x) / Float(max(gridWidth - 1, 1))
        let nz = Float(z) / Float(max(gridDepth - 1, 1))

        var h: Float = 0
        for p in peaks {
            let dx = nx - p.nx
            let dz = nz - p.nz
            let d2 = dx * dx + dz * dz
            h += p.height * expf(-(d2) / (2 * p.sigma * p.sigma))
        }

        // 低頻碎形雜訊,增加山脊與海岸線的自然感(確定性)。
        h += fbm(nx, nz) * 60.0

        // 海岸線:低於 0 視為海平面(維多利亞港/南海)。
        return max(h, 0)
    }

    /// 多倍頻雜訊(value noise via hash)。
    private func fbm(_ nx: Float, _ nz: Float) -> Float {
        var total: Float = 0
        var amp: Float = 1
        var freq: Float = 4
        var sumAmp: Float = 0
        for _ in 0..<4 {
            total += amp * valueNoise(nx * freq, nz * freq)
            sumAmp += amp
            amp *= 0.5
            freq *= 2
        }
        return total / max(sumAmp, 0.0001)
    }

    private func valueNoise(_ x: Float, _ z: Float) -> Float {
        let xi = floor(x); let zi = floor(z)
        let xf = x - xi; let zf = z - zi
        let u = smoothstep(xf); let v = smoothstep(zf)

        let x0 = Int(xi); let x1 = x0 + 1
        let z0 = Int(zi); let z1 = z0 + 1

        let c00 = hash01(x0, z0)
        let c10 = hash01(x1, z0)
        let c01 = hash01(x0, z1)
        let c11 = hash01(x1, z1)

        let a = lerp(c00, c10, u)
        let b = lerp(c01, c11, u)
        return lerp(a, b, v) * 2 - 1   // 對應至 [-1, 1]
    }

    private func smoothstep(_ t: Float) -> Float { t * t * (3 - 2 * t) }
    private func lerp(_ a: Float, _ b: Float, _ t: Float) -> Float { a + (b - a) * t }

    private func hash01(_ x: Int, _ z: Int) -> Float {
        var h = UInt64(bitPattern: Int64(x &* 374761393 &+ z &* 668265263))
        h ^= h >> 13
        h &*= 1274126177
        h ^= h >> 16
        return Float(h & 0xFFFFFF) / Float(0xFFFFFF)
    }
}

// MARK: - SplitMix64 PRNG(供未來細節雜訊擴充使用,目前 fbm 為 hash-based)

private struct SplitMix64 {
    var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}
