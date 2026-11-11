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
`FrameShift.en.iss`, `FrameShift.zh-CN.iss`, `FrameShift.es.iss`,
`FrameShift.pt-BR.iss`, `FrameShift.ru.iss`, `FrameShift.fr.iss`,
`FrameShift.ja.iss` and `FrameShift.id.iss`, included explicitly in the
`[CustomMessages]` section of `../FrameShift.iss`, with `english`, `chinesesimp`,
`spanish`, `brazilianportuguese`, `russian`, `fr`, `ja` and `id` entries.
All eight languages use the same payload, application ID,
component IDs and command lines. The installer stores its application language
default in `{app}/language.txt`; uninstall removes that owned file.

These custom-message files contain FrameShift text only, not Pascal logic. Keep
keys, language prefixes, macros and configuration markers unchanged. The separate
[translation tool](../../tools/FrameShift.LocalizationTool/README.md) can export,
check and import their text together with application resources.

For an Inno syntax check without producing a Setup, use `/O-` **and an explicit
fresh scratch output directory** (`/O<path>`). The compiler can remove an existing
Setup at its resolved output path even with output disabled. Release installers
still use the canonical build workflow.

## French, Japanese and Indonesian sources

`French.isl` and `Japanese.isl` are unmodified upstream files from
[French at is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/French.isl)
and [Japanese at is-6_7_1](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Files/Languages/Japanese.isl).
SHA-256 respectively:
`145BF4EA34EAA79C52E4FCB77CE3EEE9720B0D424C69458CB99592BC52121FBA`,
`D5450537BB128112347BF86A4BDC3A4BE0605414DF0BC4FC90F55AAEF1BA369B`.
Japanese omits only the empty upstream `HelpTextNote`; it uses the compiler's
empty default for that entry.

`Indonesian.isl` is the unmodified upstream 6.5+ update by Edward Tie, retaining
MozaikTM and other original credits, pinned to
[commit 75f53f40f4968343ff93ab13791018b449cfe444](https://github.com/jrsoftware/issrc/blob/75f53f40f4968343ff93ab13791018b449cfe444/Files/Languages/Unofficial/Indonesian.isl).
The older Indonesian file in tag is-6_7_1 predates the 6.5 message updates and is
not used. SHA-256:
`81C802D7DDF8C05EC51B027171E7E0ADF55AAC051E64ADA7B7E7C7EE57009510`.
All original translator headers and the adjacent `INNO-LICENSE.txt` are retained.
