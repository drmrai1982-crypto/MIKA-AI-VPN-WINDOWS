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
uint32_t __cdecl mika_native_expected_abi() {
    return FCAE_ABI_VERSION;
}
