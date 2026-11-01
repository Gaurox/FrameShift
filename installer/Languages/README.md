# Installer language sources

`ChineseSimplified.isl` is the unmodified UTF-8 simplified Chinese translation from
[jrsoftware/issrc, tag is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/Unofficial/ChineseSimplified.isl).
Its original maintainer credits remain in the file. Inno Setup's licence is retained
in `INNO-LICENSE.txt` beside it. The compiler supplies the matching English `Default.isl`.

SHA-256 of the vendored file: `90E5E7DB5A01BDE429A169D49BB7059675FD77F7F424645A25EE7A8A24014DB3`.

`Spanish.isl` is the unmodified Spanish translation shipped with Inno Setup 6.7.1,
also available at [jrsoftware/issrc, tag is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/Spanish.isl).
It is maintained by Jorge Andres Brugger; the original header credits him and the
other contributors. Its version is 1.7.1 (20250625). `INNO-LICENSE.txt` also applies.

SHA-256 of the vendored file: `C61EE6287D6A10DB186CCE43B556422E0A8BC631784132468A9CEBCA4F2DF0D5`.

`BrazilianPortuguese.isl` is the unmodified Brazilian Portuguese translation shipped
with Inno Setup 6.7.1, also available at
[jrsoftware/issrc, tag is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/BrazilianPortuguese.isl).
Its header credits Cesar82 and remains unchanged. `INNO-LICENSE.txt` also applies.

SHA-256 of the vendored file: `E7E5E3DBD0DDEE4C5A1F502F3035E431A21F87E68CEF9A0295680B63ED61200B`.

`Russian.isl` is the unmodified Russian translation shipped with Inno Setup 6.7.1,
also available at [jrsoftware/issrc, tag is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/Russian.isl).
Its header credits Dmitry Kann (yktoo.com) and remains unchanged. `INNO-LICENSE.txt`
also applies.

SHA-256 of the vendored file: `81981FE6CFEF7F3B2F744BEAC1494F4D2B29CEA69C96A08D1C0A6B79DC0ED8F2`.

FrameShift's own component, Explorer and Pascal-script messages live in the
`[CustomMessages]` section of `../FrameShift.iss`, with `english`, `chinesesimp`,
`spanish`, `brazilianportuguese` and `russian` entries. All five languages use the same payload, application ID,
component IDs and command lines. The installer stores its application language
default in `{app}/language.txt`; uninstall removes that owned file.
