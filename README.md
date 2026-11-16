# MIKA AI VPN for Windows

Windows desktop edition of **MIKA AI VPN**, targeting **Windows 10 and Windows 11 (x64)**.

> **Development status:** early porting stage. This repository currently contains the Windows porting plan and a Windows CI compile-check. It is not yet a usable VPN application and does not yet produce an installer.

## Goals

- Keep all Windows-specific work in this repository. The Android repository remains unchanged.
- Reuse the existing Rust VPN runtime only after validating its Windows implementation.
- Provide a native Windows desktop interface branded MIKA AI VPN.
- Package the required tunnel components and Wintun dependency correctly.
- Test real connect/disconnect, DNS, IPv4/IPv6 routing, reconnect, and failure recovery before publishing a release.

## Target platform

- Windows 10 x64
- Windows 11 x64

The first target is x64. ARM64 can be evaluated separately after the x64 build and runtime are verified.

## Source relationship

The current upstream reference is recorded in [UPSTREAM.md](UPSTREAM.md). CI checks out that exact Android-repository revision into a temporary workspace; it does not modify or push to the Android repository.

## Build status

See [docs/PORTING_STATUS.md](docs/PORTING_STATUS.md) for verified findings, limitations, and the staged plan. The workflow under [Actions](../../actions) is a compile-check only; passing it is not proof that a VPN session works.

## Safety and release policy

Do not distribute a build that uses stub tunnel backends. A release is acceptable only after the real Windows tunnel backend, driver deployment, elevated network configuration, and traffic-leak protections have passed tests on Windows 10 and 11.
