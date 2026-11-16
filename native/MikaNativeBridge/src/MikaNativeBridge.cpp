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
