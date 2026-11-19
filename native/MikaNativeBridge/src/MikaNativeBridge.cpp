#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cwchar>
#include <string>
#include "fcae.h"

namespace {
HMODULE g_ffi = nullptr;
using AbiVersionFn = uint32_t (__cdecl*)();

void write_message(wchar_t* out, uint32_t capacity, const wchar_t* value) {
    if (!out || capacity == 0) return;
    wcsncpy_s(out, capacity, value ? value : L"", _TRUNCATE);
}

FARPROC symbol(const char* name) {
    return g_ffi ? GetProcAddress(g_ffi, name) : nullptr;
}

void append_line(wchar_t* out, uint32_t capacity, const wchar_t* line) {
    if (!out || capacity == 0 || !line) return;
    const size_t used = wcslen(out);
    if (used >= capacity - 1) return;
    wcsncat_s(out, capacity, line, _TRUNCATE);
}
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_probe(const wchar_t* ffi_directory,
                                  wchar_t* message,
                                  uint32_t message_capacity) {
    if (!ffi_directory || !*ffi_directory) {
        write_message(message, message_capacity, L"FFI directory was not supplied.");
        return 1;
    }

    if (!g_ffi) {
        std::wstring path(ffi_directory);
        if (path.back() != L'\\' && path.back() != L'/') path += L'\\';
        path += L"fcae_ffi.dll";
        g_ffi = LoadLibraryExW(path.c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!g_ffi) {
            const DWORD error = GetLastError();
            wchar_t text[160]{};
            swprintf_s(text, L"Could not load fcae_ffi.dll (Windows error %lu).", error);
            write_message(message, message_capacity, text);
            return 2;
        }
    }

    const char* required[] = {
        "fcae_abi_version",
        "fcae_config_default",
        "fcae_init",
        "fcae_start",
        "fcae_stop",
        "fcae_get_telemetry",
        "fcae_last_error",
        "fcae_available_backends",
        "fcae_backend_count",
        "fcae_backend_info",
        "fcae_tun_engine_count",
        "fcae_tun_engine_info",
        "fcae_is_privileged",
        "fcae_is_running",
        "fcae_shutdown"
    };
    for (const char* name : required) {
        if (!symbol(name)) {
            wchar_t text[192]{};
            swprintf_s(text, L"Required FFI export is missing: %S", name);
            write_message(message, message_capacity, text);
            return 3;
        }
    }

    auto get_version = reinterpret_cast<AbiVersionFn>(symbol("fcae_abi_version"));
    const uint32_t actual = get_version();
    if (actual != FCAE_ABI_VERSION) {
        wchar_t text[160]{};
        swprintf_s(text, L"FFI ABI mismatch. Header=%u, DLL=%u.", FCAE_ABI_VERSION, actual);
        write_message(message, message_capacity, text);
        return 4;
    }

    write_message(message, message_capacity,
        L"FFI loaded. Required exports found. ABI version matches. Tunnel not started.");
    return 0;
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_initialize_inspect(const wchar_t* ffi_directory,
                                                wchar_t* message,
                                                uint32_t message_capacity) {
    const int32_t probe_result = mika_native_probe(ffi_directory, message, message_capacity);
    if (probe_result != 0) return probe_result;

    using InitFn = FcaeStatus (__cdecl*)(const FcaeInitOptions*);
    using BackendCountFn = uint32_t (__cdecl*)();
    using BackendInfoFn = FcaeStatus (__cdecl*)(uint32_t, FcaeBackendInfo*);
    using TunCountFn = uint32_t (__cdecl*)();
    using TunInfoFn = FcaeStatus (__cdecl*)(uint32_t, FcaeTunEngineInfo*);
    using LastErrorFn = const char* (__cdecl*)();
    using ShutdownFn = FcaeStatus (__cdecl*)();

    auto init = reinterpret_cast<InitFn>(symbol("fcae_init"));
    auto backend_count = reinterpret_cast<BackendCountFn>(symbol("fcae_backend_count"));
    auto backend_info = reinterpret_cast<BackendInfoFn>(symbol("fcae_backend_info"));
    auto tun_count = reinterpret_cast<TunCountFn>(symbol("fcae_tun_engine_count"));
    auto tun_info = reinterpret_cast<TunInfoFn>(symbol("fcae_tun_engine_info"));
    auto last_error = reinterpret_cast<LastErrorFn>(symbol("fcae_last_error"));
    auto shutdown = reinterpret_cast<ShutdownFn>(symbol("fcae_shutdown"));

    FcaeInitOptions options{};
    options.struct_size = sizeof(options);
    options.abi_version = FCAE_ABI_VERSION;
    options.max_log_level = FCAE_LOG_WARN;

    const FcaeStatus init_status = init(&options);
    if (init_status != FCAE_OK) {
        wchar_t text[512]{};
        const char* error = last_error();
        swprintf_s(text, L"fcae_init failed (status=%d): %S",
                   static_cast<int>(init_status), error ? error : "no error text");
        write_message(message, message_capacity, text);
        return 5;
    }

    wchar_t report[2048]{};
    swprintf_s(report, L"FCAE initialized successfully. ABI=%u.\r\n",
               FCAE_ABI_VERSION);

    const uint32_t backends = backend_count();
    wchar_t line[320]{};
    swprintf_s(line, L"Backend entries: %u\r\n", backends);
    append_line(report, static_cast<uint32_t>(_countof(report)), line);

    for (uint32_t i = 0; i < backends && i < 16; ++i) {
        FcaeBackendInfo info{};
        info.struct_size = sizeof(info);
        info.abi_version = FCAE_ABI_VERSION;
        const FcaeStatus status = backend_info(i, &info);
        if (status == FCAE_OK) {
            swprintf_s(line, L"Backend[%u]: id=%S, name=%S, available=%u, reason=%S\r\n",
                       i, info.id, info.display_name, info.available ? 1u : 0u,
                       info.unavailable_reason);
        } else {
            swprintf_s(line, L"Backend[%u]: info query failed (status=%d)\r\n",
                       i, static_cast<int>(status));
        }
        append_line(report, static_cast<uint32_t>(_countof(report)), line);
    }

    const uint32_t engines = tun_count();
    swprintf_s(line, L"TUN engine entries: %u\r\n", engines);
    append_line(report, static_cast<uint32_t>(_countof(report)), line);

    for (uint32_t i = 0; i < engines && i < 16; ++i) {
        FcaeTunEngineInfo info{};
        info.struct_size = sizeof(info);
        info.abi_version = FCAE_ABI_VERSION;
        const FcaeStatus status = tun_info(i, &info);
        if (status == FCAE_OK) {
            swprintf_s(line, L"TUN[%u]: engine=%llu, id=%S, name=%S, available=%u, reason=%S\r\n",
                       i, static_cast<unsigned long long>(info.engine), info.id,
                       info.display_name, info.available ? 1u : 0u,
                       info.unavailable_reason);
        } else {
            swprintf_s(line, L"TUN[%u]: info query failed (status=%d)\r\n",
                       i, static_cast<int>(status));
        }
        append_line(report, static_cast<uint32_t>(_countof(report)), line);
    }

    const FcaeStatus shutdown_status = shutdown();
    swprintf_s(line, L"FCAE shutdown status: %d. No tunnel was started.",
               static_cast<int>(shutdown_status));
    append_line(report, static_cast<uint32_t>(_countof(report)), line);
    write_message(message, message_capacity, report);
    return shutdown_status == FCAE_OK ? 0 : 6;
}

extern "C" __declspec(dllexport)
uint32_t __cdecl mika_native_expected_abi() {
    return FCAE_ABI_VERSION;
}


// Stable, deliberately small snapshot ABI for the WPF host. The desktop app
// never mirrors Rust struct layouts; this bridge copies only the fields it uses.
struct MikaTelemetrySnapshot {
    uint32_t struct_size;
    uint32_t abi_version;
    int32_t state;
    int32_t backend;
    int32_t active_mode;
    uint32_t rtt_ms;
    uint64_t rx_bytes_sec;
    uint64_t tx_bytes_sec;
    uint64_t total_rx;
    uint64_t total_tx;
    uint64_t uptime_secs;
    wchar_t connected_peer[64];
    wchar_t status_message[128];
    wchar_t last_error[256];
};

namespace {
bool g_session_initialized = false;

std::string utf8_from_wide(const wchar_t* value) {
    if (!value || !*value) return {};
    const int needed = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, nullptr, 0, nullptr, nullptr);
    if (needed <= 1) return {};
    std::string result(static_cast<size_t>(needed), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, result.data(), needed, nullptr, nullptr) == 0)
        return {};
    result.resize(static_cast<size_t>(needed - 1));
    return result;
}

void copy_utf8_to_wide(wchar_t* out, size_t capacity, const char* value) {
    if (!out || capacity == 0) return;
    out[0] = L'\0';
    if (!value || !*value) return;
    MultiByteToWideChar(CP_UTF8, 0, value, -1, out, static_cast<int>(capacity));
}
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_session_init(const wchar_t* ffi_directory,
                                          wchar_t* message,
                                          uint32_t message_capacity) {
    const int32_t probe = mika_native_probe(ffi_directory, message, message_capacity);
    if (probe != 0) return probe;
    using InitFn = FcaeStatus (__cdecl*)(const FcaeInitOptions*);
    auto init = reinterpret_cast<InitFn>(symbol("fcae_init"));
    FcaeInitOptions options{};
    options.struct_size = sizeof(options);
    options.abi_version = FCAE_ABI_VERSION;
    options.max_log_level = FCAE_LOG_WARN;
    const FcaeStatus status = init(&options);
    if (status != FCAE_OK) {
        using LastErrorFn = const char* (__cdecl*)();
        auto last_error = reinterpret_cast<LastErrorFn>(symbol("fcae_last_error"));
        wchar_t text[512]{};
        const char* error = last_error ? last_error() : "no error text";
        swprintf_s(text, L"FCAE initialization failed (status=%d): %S",
                   static_cast<int>(status), error ? error : "no error text");
        write_message(message, message_capacity, text);
        return static_cast<int32_t>(status);
    }
    g_session_initialized = true;
    write_message(message, message_capacity, L"FCAE session runtime initialized.");
    return 0;
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_session_start(const wchar_t* data_directory,
                                           uint64_t tun_engine,
                                           uint32_t protocol,
                                           wchar_t* message,
                                           uint32_t message_capacity) {
    if (!g_session_initialized) {
        write_message(message, message_capacity, L"Native session is not initialized.");
        return FCAE_NOT_INITIALIZED;
    }
    using BackendCountFn = uint32_t (__cdecl*)();
    using BackendInfoFn = FcaeStatus (__cdecl*)(uint32_t, FcaeBackendInfo*);
    using TunInfoFn = FcaeStatus (__cdecl*)(uint32_t, FcaeTunEngineInfo*);
    using ConfigDefaultFn = FcaeStatus (__cdecl*)(FcaeConfig*);
    using StartFn = FcaeStatus (__cdecl*)(const FcaeConfig*);
    using LastErrorFn = const char* (__cdecl*)();
    using PrivilegedFn = bool (__cdecl*)();

    auto backend_count = reinterpret_cast<BackendCountFn>(symbol("fcae_backend_count"));
    auto backend_info = reinterpret_cast<BackendInfoFn>(symbol("fcae_backend_info"));
    auto tun_info = reinterpret_cast<TunInfoFn>(symbol("fcae_tun_engine_info"));
    auto config_default = reinterpret_cast<ConfigDefaultFn>(symbol("fcae_config_default"));
    auto start = reinterpret_cast<StartFn>(symbol("fcae_start"));
    auto last_error = reinterpret_cast<LastErrorFn>(symbol("fcae_last_error"));
    auto is_privileged = reinterpret_cast<PrivilegedFn>(symbol("fcae_is_privileged"));

    bool aether_available = false;
    for (uint32_t i = 0; i < backend_count(); ++i) {
        FcaeBackendInfo info{};
        info.struct_size = sizeof(info);
        info.abi_version = FCAE_ABI_VERSION;
        if (backend_info(i, &info) == FCAE_OK && info.backend == FCAE_BACKEND_AETHER) {
            aether_available = info.available;
            if (!info.available) {
                wchar_t reason[256]{};
                swprintf_s(reason, L"Aether backend unavailable: %S", info.unavailable_reason);
                write_message(message, message_capacity, reason);
                return FCAE_BACKEND_UNAVAILABLE;
            }
            break;
        }
    }
    if (!aether_available) {
        write_message(message, message_capacity, L"Aether backend is not registered in this runtime.");
        return FCAE_BACKEND_UNAVAILABLE;
    }

    FcaeTunEngineInfo engine{};
    engine.struct_size = sizeof(engine);
    engine.abi_version = FCAE_ABI_VERSION;
    if (tun_info(static_cast<uint32_t>(tun_engine == FCAE_TUN_ENGINE_TUN2SOCKS ? 0 :
                                      tun_engine == FCAE_TUN_ENGINE_ZEPTUN ? 1 : 2), &engine) != FCAE_OK ||
        !engine.available || engine.engine != tun_engine) {
        wchar_t reason[320]{};
        swprintf_s(reason, L"TUN engine unavailable: %S", engine.unavailable_reason);
        write_message(message, message_capacity, reason);
        return FCAE_BACKEND_UNAVAILABLE;
    }

    if (is_privileged && !is_privileged()) {
        write_message(message, message_capacity, L"Administrator privileges are required to create the Windows TUN interface.");
        return FCAE_PERMISSION_DENIED;
    }

    FcaeConfig config{};
    const FcaeStatus defaults_status = config_default(&config);
    if (defaults_status != FCAE_OK) {
        write_message(message, message_capacity, L"Could not create the default FCAE configuration.");
        return static_cast<int32_t>(defaults_status);
    }
    const std::string data_dir = utf8_from_wide(data_directory);
    config.backend = FCAE_BACKEND_AETHER;
    config.protocol = static_cast<FcaeProtocol>(protocol);
    config.mode = FCAE_MODE_TUN;
    config.tun_engine = tun_engine;
    config.data_dir = data_dir.empty() ? nullptr : data_dir.c_str();

    const FcaeStatus status = start(&config);
    if (status != FCAE_OK) {
        wchar_t text[768]{};
        const char* error = last_error ? last_error() : "no error text";
        swprintf_s(text, L"VPN start request failed (status=%d): %S",
                   static_cast<int>(status), error ? error : "no error text");
        write_message(message, message_capacity, text);
        return static_cast<int32_t>(status);
    }
    write_message(message, message_capacity, L"VPN start request accepted. Waiting for native tunnel state.");
    return 0;
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_session_stop(wchar_t* message, uint32_t message_capacity) {
    if (!g_session_initialized) {
        write_message(message, message_capacity, L"Native session is not initialized.");
        return FCAE_NOT_INITIALIZED;
    }
    using StopFn = FcaeStatus (__cdecl*)();
    using LastErrorFn = const char* (__cdecl*)();
    auto stop = reinterpret_cast<StopFn>(symbol("fcae_stop"));
    auto last_error = reinterpret_cast<LastErrorFn>(symbol("fcae_last_error"));
    const FcaeStatus status = stop();
    if (status != FCAE_OK) {
        wchar_t text[512]{};
        const char* error = last_error ? last_error() : "no error text";
        swprintf_s(text, L"VPN stop request failed (status=%d): %S",
                   static_cast<int>(status), error ? error : "no error text");
        write_message(message, message_capacity, text);
        return static_cast<int32_t>(status);
    }
    write_message(message, message_capacity, L"VPN stop requested; native cleanup is in progress.");
    return 0;
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_session_telemetry(MikaTelemetrySnapshot* out,
                                                    wchar_t* message,
                                                    uint32_t message_capacity) {
    if (!out || out->struct_size != sizeof(MikaTelemetrySnapshot) || out->abi_version != FCAE_ABI_VERSION) {
        write_message(message, message_capacity, L"Telemetry snapshot ABI or size mismatch.");
        return FCAE_ABI_MISMATCH;
    }
    if (!g_session_initialized) {
        write_message(message, message_capacity, L"Native session is not initialized.");
        return FCAE_NOT_INITIALIZED;
    }
    using TelemetryFn = FcaeStatus (__cdecl*)(FcaeTelemetry*);
    using LastErrorFn = const char* (__cdecl*)();
    auto get_telemetry = reinterpret_cast<TelemetryFn>(symbol("fcae_get_telemetry"));
    auto last_error = reinterpret_cast<LastErrorFn>(symbol("fcae_last_error"));
    FcaeTelemetry telemetry{};
    telemetry.struct_size = sizeof(telemetry);
    telemetry.abi_version = FCAE_ABI_VERSION;
    const FcaeStatus status = get_telemetry(&telemetry);
    if (status != FCAE_OK) {
        const char* error = last_error ? last_error() : "no native error detail";
        wchar_t text[768]{};
        swprintf_s(text, L"fcae_get_telemetry failed (status=%d): %S",
                   static_cast<int>(status), error ? error : "no native error detail");
        write_message(message, message_capacity, text);
        return static_cast<int32_t>(status);
    }
    write_message(message, message_capacity, L"Native telemetry read successfully."); 
    out->state = static_cast<int32_t>(telemetry.state);
    out->backend = static_cast<int32_t>(telemetry.backend);
    out->active_mode = static_cast<int32_t>(telemetry.active_mode);
    out->rtt_ms = telemetry.rtt_ms;
    out->rx_bytes_sec = telemetry.rx_bytes_sec;
    out->tx_bytes_sec = telemetry.tx_bytes_sec;
    out->total_rx = telemetry.total_rx;
    out->total_tx = telemetry.total_tx;
    out->uptime_secs = telemetry.uptime_secs;
    copy_utf8_to_wide(out->connected_peer, _countof(out->connected_peer), telemetry.connected_peer);
    copy_utf8_to_wide(out->status_message, _countof(out->status_message), telemetry.status_message);
    copy_utf8_to_wide(out->last_error, _countof(out->last_error), telemetry.last_error);
    return 0;
}

extern "C" __declspec(dllexport)
int32_t __cdecl mika_native_session_shutdown(wchar_t* message, uint32_t message_capacity) {
    if (!g_session_initialized) {
        write_message(message, message_capacity, L"Native session is not initialized.");
        return 0;
    }
    using RunningFn = bool (__cdecl*)();
    using StopFn = FcaeStatus (__cdecl*)();
    using ShutdownFn = FcaeStatus (__cdecl*)();
    auto running = reinterpret_cast<RunningFn>(symbol("fcae_is_running"));
    auto stop = reinterpret_cast<StopFn>(symbol("fcae_stop"));
    auto shutdown = reinterpret_cast<ShutdownFn>(symbol("fcae_shutdown"));
    if (running && running()) {
        const FcaeStatus stop_status = stop();
        if (stop_status != FCAE_OK) {
            write_message(message, message_capacity, L"Could not stop the active session before shutdown.");
            return static_cast<int32_t>(stop_status);
        }
    }
    const FcaeStatus status = shutdown();
    g_session_initialized = false;
    write_message(message, message_capacity, status == FCAE_OK
        ? L"Native runtime shut down cleanly."
        : L"Native runtime shutdown reported an error.");
    return static_cast<int32_t>(status);
}
