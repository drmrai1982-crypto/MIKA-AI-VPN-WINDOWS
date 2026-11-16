# Upstream source reference

- Repository: https://github.com/drmrai1982-crypto/MIKA-AI-VPN
- Branch at inspection: `master`
- Pinned commit: `73de903180e5d54b004b4338ad11355b0faf8609`
- Inspected: 2026-11-16

The Windows repository uses this pinned revision for its initial compile-check workflow. The workflow checks out the upstream repository read-only into the runner workspace. It does not commit changes to, or trigger writes against, the Android repository.

When intentionally updating the upstream revision, inspect the Windows-specific code and dependency changes first, update this file and the workflow pin together, and rerun the Windows checks.
