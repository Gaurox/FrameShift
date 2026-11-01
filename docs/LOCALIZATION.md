# Localization

FrameShift 1.22 prepares one application and installer in English, simplified Chinese,
Spanish and Brazilian Portuguese. It is not published yet. English is the fallback
for development builds, missing installation defaults and unsupported language values.
Only display culture is selected; numeric
parsing, CLI options, model IDs, persisted values, FFmpeg parameters and output
naming retain their existing contracts.

## Contribution and integration

The simplified Chinese wording and narrow ActionsPanel layout correction originate
from [zhazhayu01/FrameShift](https://github.com/zhazhayu01/FrameShift), commit
[`f078b0c3490ae43abba807bba24692df8ef446da`](https://github.com/zhazhayu01/FrameShift/commit/f078b0c3490ae43abba807bba24692df8ef446da),
based on FrameShift 1.21.1 (`b777b55`). The fork's GPL-3.0 contribution is adapted
into resource files instead of replacing English literals in the maintained code.
General fixes made after its base are preserved. Additional wording covers those
fixes and the language preference. Original third-party licence texts are unchanged.

The Spanish translation uses the existing English resources and the same shared
catalog. Neutral `es` covers regional preferences such as `es-ES`, `es-MX` and `es-AR`.
Brazilian Portuguese uses `pt-BR` and the native label `Português (Brasil)`; its
installer identifier is `brazilianportuguese`. Generic `pt` and `pt-PT` remain
unregistered and fall back to English; no other Portuguese variant is enabled.
All four READMEs link to each other. Inno Setup translations retain their own
upstream credits; see [installer language sources](../installer/Languages/README.md).

## Language selection

- `Core/Localization/AppLanguages.cs` is the application's supported-language catalog:
  canonical culture code, native display name and optional aliases (including Inno
  Setup language identifiers). **1.22 registers English, simplified Chinese, Spanish and Brazilian Portuguese.**
- Startup, preferences and the subtitle worker resolve languages through that catalog.
  Matching ignores case and accepts regional parent cultures, such as `en-GB` -> `en`.
  Empty, invalid or unregistered languages fall back to English. Adding a resource
  file alone does not enable a language in the application.
- Settings builds its choices from the catalog and saves the selected culture code;
  it has no per-language index or display-name conditional to extend.
- The installer offers English, 简体中文, Español and Português Brasileiro. Its custom
  pages, component captions, model instructions and optional Explorer entries use
  that selection.
- The application reads the user's saved language preference, then the installation
  default in `language.txt`, then English. It initializes before action catalogs,
  forms and background work are created.
- Settings can save any registered language for the next application launch. Already
  open windows and action catalogs are not rebuilt. Re-running the installer changes
  Explorer's registered menu captions; changing a per-user application setting does
  not write administrative Explorer registry entries.
- The subtitle worker receives `FRAMESHIFT_UI_LANGUAGE` in its child environment.
  This changes its progress text without changing request/response protocol fields
  or the language it recognizes in media.

## Maintaining translations

`Core/Localization/Strings.resx` is the English source. `Strings.zh-CN.resx`,
`Strings.es.resx` and `Strings.pt-BR.resx` contain the simplified Chinese, Spanish
and Brazilian Portuguese translations. `L.Get` retrieves text;
`L.Format` formats numbered arguments. Add matching keys to all resources, retaining
each argument and its numeric format. Keep filenames, identifiers, state keys,
regular expressions, raw FFmpeg diagnostics and processing parameters outside them.
Display labels for enums and choices are separate from their stored values.

The worker links `Core/Localization/*.cs` and `Strings*.resx` automatically, with
stable resource names. Neither project gains a package dependency. Chinese, Spanish
and Brazilian Portuguese Inno Setup language files are checked into
`installer/Languages` with their provenance,
instead of relying on a modified compiler installation.

Tests check exact resource coverage and format arguments for **every registered
translation**, catalog/installer agreement, aliases and regional fallbacks, unchanged
numeric culture, Unicode paths, stable action IDs, Settings choices and narrow
Chinese/Spanish/Brazilian Portuguese button layouts. Release packaging must include each registered language's satellite
assemblies for both the app and worker.
Screenshots can be saved by setting `FRAMESHIFT_LOCALIZATION_SCREENSHOTS` when
running the targeted localization tests. Hidden rendering does not establish actual
high-DPI, screen-reader, Explorer or installation acceptance.

## Adding a future language

For example, a future French release would require these focused changes; **French
is not enabled or distributed in 1.22**:

1. Copy `Strings.resx` to `Strings.fr.resx` and translate the display values, preserving
   keys, arguments and format specifiers. Keep draft translations on a separate branch
   until they are ready to ship.
2. Add `new("fr", "Français", "french")` to `AppLanguages.Supported`. The last value is
   the corresponding Inno Setup identifier. The settings selector, preference loading,
   regional matching (`fr-CA` -> `fr`) and worker language forwarding then use this entry.
   No form logic or per-language project-file entry is needed.
3. In `installer/FrameShift.iss`, add the Inno `[Languages]` entry, its
   `french.AppLanguage=fr` marker and all corresponding `[CustomMessages]` translations.
   Vendor any required `.isl` with its provenance under `installer/Languages`. Inno's
   explicit configuration is retained; no generator or runtime dependency is introduced.
4. Run the localization tests and canonical Release build. The registered-resource
   tests automatically include the new language, and installer/catalog mismatches fail.
   Check both app/worker satellites, installed Explorer captions and actual layouts,
   fonts, keyboard access and longer text before distributing that language.

This mechanism selects supplied translations; it does not generate them. Languages
requiring right-to-left layouts or additional plural forms need their corresponding
UI/wording work and acceptance rather than just a new culture code.
