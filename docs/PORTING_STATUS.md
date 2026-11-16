# Windows porting status

Last reviewed: 2026-11-16  
Target: Windows 10 and Windows 11, x64

## Verified in the upstream repository

The inspected upstream revision contains Windows-specific Rust runtime code:

- `core/fcae-ffi/runtime/src/windows_tun.rs` configures the Windows TUN adapter, addresses, routes, interface settings, DNS guard, and endpoint bypass routes.
- `core/fcae-ffi/runtime/src/windows_dll.rs` provides Windows DLL loading and safe staging of embedded DLL files.
- `core/fcae-ffi/buildtools/src/wintun.rs` stages and checks the Wintun DLL.
- `core/fcae-ffi/bridges/tun2socks` builds an in-process Go c-archive and documents the Windows MinGW C toolchain requirement.
- `core/fcae-ffi/bridges/hev-socks5-tunnel/build.rs` explicitly documents that the HEV Windows backend is MSYS-runtime based and must be built as a DLL; a stub build reports the engine unavailable.
- `core/fcae-ffi/ffi/Cargo.toml` defines optional backends, so merely compiling the FFI crate without backend features does not produce a functioning VPN.

## Important limitations

1. The Android front end is an Android Gradle/Kotlin application. It is not a Windows desktop UI and cannot be packaged as a Windows application.
2. Android native libraries are not Windows binaries. Windows-native DLLs and the correct architecture-specific dependencies must be built separately.
3. Wintun is a required TUN driver dependency for the relevant Windows TUN path. Driver installation, elevation, adapter lifecycle, and cleanup must be tested on real Windows machines.
4. The HEV Windows backend is not a simple static link. Its documented MSYS2 DLL build and runtime dependency set must be reproduced and verified before enabling that backend.
5. The existing standalone Aether desktop workflow is not by itself the complete desktop application or proof that every VPN mode works on Windows.
6. Standalone Tor modes are explicitly rejected by the current Windows TUN validation code; do not advertise them as supported unless a separate implementation and tests are added.
7. No Windows UI, installer, signing pipeline, or end-to-end Windows VPN test suite has been established in this repository yet.

## Staged implementation

### Stage 1 — reproducible compile checks
- Pin the upstream source revision.
- Compile the Windows runtime and FFI shell for x64 on a Windows GitHub Actions runner.
- Keep this workflow honest: it is a compile-check, not a release build and not a tunnel functionality test.

### Stage 2 — select and integrate the real backend
- Inspect the backend registration and session configuration paths.
- Build the selected backend with its real Windows dependencies; never ship stub features.
- Confirm Wintun acquisition, verification, installation/elevation requirements, and clean uninstall behavior.
- Decide the supported VPN modes based on successful Windows builds and tests, not on Android feature parity assumptions.

### Stage 3 — Windows desktop application
- Implement a native desktop UI for MIKA AI VPN.
- Connect the UI to the existing stable FFI boundary rather than reimplementing VPN protocol logic in the UI.
- Implement status, server/mode selection, connect/disconnect, diagnostics, and Persian/English layouts.

### Stage 4 — verification and packaging
- Test Windows 10 and Windows 11 x64, including admin permissions, DNS, IPv4/IPv6, route restoration, reconnect, sleep/resume, and uninstall.
- Add leak/fail-closed tests and verify that stopping the app restores the original network configuration.
- Produce a signed installer only after all required tests pass.

## Current CI check

The initial workflow compiles the Windows-specific runtime and the FFI shell with backend features disabled. This validates only the platform-facing Rust code and build graph. It deliberately does not claim that a VPN backend is enabled or that a tunnel can connect.
