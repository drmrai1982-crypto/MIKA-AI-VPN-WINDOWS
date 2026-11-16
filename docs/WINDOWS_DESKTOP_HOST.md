# Windows desktop host integration

Target desktop shell: **WPF on .NET 8 (self-contained x64)**, subject to the first end-to-end build.

WPF is selected as the initial implementation path because it is a mature Windows desktop UI framework with Windows 10/11 support, straightforward Persian RTL support, and no dependency on a browser runtime. The final installer can bundle the .NET runtime so users do not need to install it separately.

## Native-core boundary

The upstream Rust layer already exposes a C ABI through `core/fcae-ffi/include/fcae.h`. The exported lifecycle surface includes:

- `fcae_abi_version` and `fcae_config_default`
- `fcae_init`
- `fcae_start`, `fcae_stop_begin`, and `fcae_stop`
- `fcae_get_telemetry`
- `fcae_available_backends`, `fcae_backend_info`
- `fcae_tun_engine_count`, `fcae_tun_engine_info`
- `fcae_is_privileged`, `fcae_last_error`, and `fcae_shutdown`

The desktop UI should call this ABI through a small, explicitly versioned native boundary. It must not reimplement VPN protocols or silently substitute simulated connection state.

## Important build integration issue

At the inspected upstream revision, `fcae-ffi/ffi/Cargo.toml` declares only `crate-type = ["staticlib"]`. That is suitable for the Android CMake integration, but a .NET P/Invoke client normally needs a Windows DLL with exported C symbols.

The Windows port must therefore produce and validate a Windows DLL boundary in this repository (for example, by a Windows-specific cdylib build or a small native host DLL that links the Rust static library). Do not change the Android repository to solve this. Verify the exported symbols against `fcae.h` before connecting the WPF UI.

## Windows TUN lifecycle

For desktop builds, the native bridge creates and configures the TUN device itself; Android's externally supplied VpnService file descriptor is not the Windows model. The Rust runtime contains Windows route/DNS management and Wintun staging code.

The desktop host must:
- detect missing elevation and present a clear UAC/restart path;
- never report Connected until native telemetry confirms it;
- display actionable native errors from the FFI;
- request stop and wait for cleanup before application exit;
- avoid force-killing the process while route/DNS restoration is in progress;
- show only backend and TUN engine combinations reported as available by the native API.

## UI scope for the first functional slice

1. Main connection screen with Connect/Disconnect and truthful connection states.
2. Backend and supported tunnel-engine selection based on native capability data.
3. Live upload/download counters from native telemetry.
4. Diagnostics panel with current adapter, privilege state, and last native error.
5. Persian/English language switch and correct RTL/LTR layout.
6. About page with MIKA AI Technologies branding.

No button is to be presented as operational until it is wired to the native API and tested against a real Windows tunnel.
