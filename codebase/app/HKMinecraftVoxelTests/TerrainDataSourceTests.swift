//
//  TerrainDataSourceTests.swift
//  HKMinecraftVoxel
//
//  程序化香港地形的單元測試:確定性、邊界、海平面鉗制。
//  CI 於 iOS Simulator 以 CODE_SIGNING_ALLOWED=NO 執行(見 .github/workflows/cicd.yml)。
//

import XCTest
@testable import HKMinecraftVoxel

final class TerrainDataSourceTests: XCTestCase {

    func testDeterminism_sameSeedProducesSameHeights() {
        let a = ProceduralHKTerrain(seed: 42)
        let b = ProceduralHKTerrain(seed: 42)
        for x in 0..<a.gridWidth {
            for z in 0..<a.gridDepth {
                XCTAssertEqual(a.height(atX: x, z: z), b.height(atX: x, z: z), accuracy: 0.0001,
                               "同一 seed 應產生相同地形")
            }
        }
    }

    func testHeightsAreNonNegative_seaLevelClamp() {
        let terrain = ProceduralHKTerrain()
        for x in 0..<terrain.gridWidth {
            for z in 0..<terrain.gridDepth {
                let h = terrain.height(atX: x, z: z)
                XCTAssertGreaterThanOrEqual(h, 0, "高度不得低於海平面")
            }
        }
    }

    func testKnownPeaksRiseAboveSurroundings() {
        // 太平山峰位於 (nx=0.30, nz=0.28),對應網格座標。
        let t = ProceduralHKTerrain(gridWidth: 96, gridDepth: 96)
        let peakX = Int(0.30 * 95)
        let peakZ = Int(0.28 * 95)
        let peakH = t.height(atX: peakX, z: peakZ)
        let edgeH = t.height(atX: 0, z: 0)
        XCTAssertGreaterThan(peakH, edgeH, "山峰應高於邊緣")
        XCTAssertGreaterThan(peakH, 300, "太平山應明顯高於 300m")
    }

    func testGridDimensionsAndVoxelSize() {
        let t = ProceduralHKTerrain()
        XCTAssertEqual(t.gridWidth, 96)
        XCTAssertEqual(t.gridDepth, 96)
        XCTAssertEqual(t.voxelSize, 40.0)
    }
}
