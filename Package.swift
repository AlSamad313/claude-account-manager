// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ClaudeProfiles",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "cpm", targets: ["cpm"]),
        .executable(name: "ClaudeProfiles", targets: ["ClaudeProfiles"]),
    ],
    targets: [
        .target(name: "ProfileCore", swiftSettings: [.swiftLanguageMode(.v5)]),
        .executableTarget(name: "cpm", dependencies: ["ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
        .testTarget(name: "ProfileCoreTests", dependencies: ["ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
        .target(name: "ProfileUI", dependencies: ["ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
        .executableTarget(name: "ClaudeProfiles", dependencies: ["ProfileUI", "ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
        .testTarget(name: "ProfileUITests", dependencies: ["ProfileUI", "ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
    ]
)
