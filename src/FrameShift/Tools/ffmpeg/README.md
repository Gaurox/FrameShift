# Bundled FFmpeg tools

`ffmpeg-tools.zip` contains only the unmodified Windows x64 `ffmpeg.exe` and
`ffprobe.exe` from Gyan's verified essentials build. The ZIP stays below GitHub's
100 MiB file limit; the two individual executables exceed it. No Git LFS or network
download is needed when building a checkout.

The SDK's built-in `Unzip` task materializes the ignored executables here before
normal builds. The canonical `build_installer.ps1` also verifies the ZIP and
materializes missing or old tools before its input checks. It verifies both binary
hashes again in the source and final payload. Only the two executables are copied
to build/publish output; the ZIP and these development notes are not installed.

Build: `2026-10-29-git-31ba826726-essentials_build-www.gyan.dev`.
Source revision: `31ba826726fd0d1547f2c22e041848d1d7f3f860`.
See `PROVENANCE.json` for source URLs, archive/binary hashes and the two verified
security-fix ancestors. Distribution licences and attribution remain in the root
`THIRD_PARTY_NOTICES.md` and packaged licence directory.
