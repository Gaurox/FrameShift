# Installer language sources

`ChineseSimplified.isl` is the unmodified UTF-8 simplified Chinese translation from
[jrsoftware/issrc, tag is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/Unofficial/ChineseSimplified.isl).
Its original maintainer credits remain in the file. Inno Setup's licence is retained
in `INNO-LICENSE.txt` beside it. The compiler supplies the matching English `Default.isl`.

SHA-256 of the vendored file: `90E5E7DB5A01BDE429A169D49BB7059675FD77F7F424645A25EE7A8A24014DB3`.

FrameShift's own component, Explorer and Pascal-script messages live in the
`[CustomMessages]` section of `../FrameShift.iss`, with both `english` and
`chinesesimp` entries. Both languages use the same payload, application ID,
component IDs and command lines. The installer stores its application language
default in `{app}/language.txt`; uninstall removes that owned file.
