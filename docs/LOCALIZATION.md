# English and simplified Chinese

FrameShift 1.22 prepares a single bilingual application and installer. It is not
published yet. English is the fallback for development builds, missing installation
defaults and unsupported language values. Only display culture is selected; numeric
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

## Language selection

- The installer always offers English and 简体中文. Its custom pages, component
  captions, model instructions and optional Explorer entries use that selection.
- The application reads the user's saved language preference, then the installation
  default in `language.txt`, then English. It initializes before action catalogs,
  forms and background work are created.
- Settings can save English or 简体中文 for the next application launch. Already
  open windows and action catalogs are not rebuilt. Re-running the installer changes
  Explorer's registered menu captions; changing a per-user application setting does
  not write administrative Explorer registry entries.
- The subtitle worker receives `FRAMESHIFT_UI_LANGUAGE` in its child environment.
  This changes its progress text without changing request/response protocol fields
  or the language it recognizes in media.

## Maintaining translations

`Core/Localization/Strings.resx` is the English source and
`Strings.zh-CN.resx` the simplified Chinese translation. `L.Get` retrieves text;
`L.Format` formats numbered arguments. Add matching keys to both resources, retaining
each argument and its numeric format. Keep filenames, identifiers, state keys,
regular expressions, raw FFmpeg diagnostics and processing parameters outside them.
Display labels for enums and choices are separate from their stored values.

The worker links the same managed resource files; neither project gains a package
dependency. The Chinese Inno Setup language file is checked into `installer/Languages`
with its provenance, instead of relying on a modified compiler installation.

Tests check exact resource coverage and format arguments, Unicode paths, stable
action IDs, both Settings languages and narrow Chinese button layouts. Release
packaging must include both `zh-CN` satellite assemblies, for the app and worker.
Screenshots can be saved by setting `FRAMESHIFT_LOCALIZATION_SCREENSHOTS` when
running the targeted localization tests. Hidden rendering does not establish actual
high-DPI, screen-reader, Explorer or installation acceptance.
