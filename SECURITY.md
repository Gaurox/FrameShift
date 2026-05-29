# Security Policy

## Supported Versions

| Version | Status          |
|---------|-----------------|
| 1.0.x   | Active support  |

Only the latest release receives security fixes. Older versions are not patched.

## Reporting a Vulnerability

If you discover a security vulnerability in FrameShift, please **do not open a public GitHub issue**.

Report it privately through one of the following channels:

- **GitHub Security Advisory:** open a private advisory at  
  `https://github.com/gaurox/FrameShift/security/advisories/new`

- **Direct contact:** reach out privately via GitHub  
  (`@gaurox`) if the advisory form is not available.

Please include:

1. A clear description of the vulnerability.
2. Steps to reproduce (or a proof-of-concept if applicable).
3. The potential impact (data exposure, arbitrary code execution, etc.).
4. The version(s) affected.

## What to Expect

- Acknowledgement within **7 days**.
- An assessment and, if confirmed, a fix in the next release.
- Credit in the release notes if you wish to be named.

## Scope

FrameShift is a local offline desktop utility. Its threat model covers:

- **In scope:** vulnerabilities in the application itself (C# code, installer, context-menu integration, AI model download and integrity verification, FFmpeg process execution).
- **Out of scope:** issues in upstream dependencies (FFmpeg, ONNX Runtime, NAudio, PDFsharp, ImageSharp) — please report those to their respective maintainers.

## Security Design Notes

- All network activity is limited to downloading AI model files from HTTPS endpoints.
- Every downloaded model file is verified against a pinned SHA-256 hash before use.
- FFmpeg is invoked with `UseShellExecute = false` and explicit `ArgumentList` entries (no shell interpolation).
- No user data is transmitted externally. All processing is local.
