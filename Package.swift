// swift-tools-version: 6.0
import PackageDescription

// S3 adds the ProfileUI library and the ClaudeProfiles app.
let package = Package(
    name: "ClaudeProfiles",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "cpm", targets: ["cpm"])],
    targets: [
        .target(name: "ProfileCore", swiftSettings: [.swiftLanguageMode(.v5)]),
        .executableTarget(name: "cpm", dependencies: ["ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
        .testTarget(name: "ProfileCoreTests", dependencies: ["ProfileCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
    ]
)
