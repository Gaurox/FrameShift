# FrameShift 1.22.0 bilingual preparation

Prepared on 1 November 2026. This is the new English/simplified Chinese candidate,
based on published 1.21.1 and the six general correction lots. It contains no Find
Loop code. The former private loop candidate is preserved separately; see the
[branch plan](RELEASE_PLAN_1.22_1.23.md). Nothing is pushed, installed or published
as part of this preparation.

## Scope and initial verification

- Source version is 1.22.0. App ID, component identifiers, processing options,
  output naming, packages and pinned runtime/tool versions remain unchanged.
- Resources retain English and adapt the Chinese fork at `f078b0c`; the subtitle
  worker shares resources and receives only a display-language environment setting.
- Settings saves the user's next-launch language while preserving existing values.
  Installer language selects its own pages/components, Explorer captions and the
  installation's default application language. See [language behavior](LOCALIZATION.md).
- Debug compilation passes without warnings/errors. The final targeted localization
  run passes **11 tests, 0 failures, 0 skips**, including complete resource/format
  coverage, Unicode paths and action identifiers, language persistence, both Settings
  languages and two narrow Chinese layout cases. Hidden renders were inspected.
- The first full Debug attempt inside the sandbox could not connect to testhost;
  it executed **zero tests** and is recorded as aborted, not as a passing suite.
  Targeted tests execute successfully outside that isolation.
- Inno Setup 6.7.1 accepts both the vendored Chinese language file and all custom
  script messages. This is a syntax check, not installed acceptance.

Evidence is under ignored `scratch/release-1.22-*` paths. Canonical Release build,
complete test results, artifact hashes and payload inspection will be recorded below.

## Work before a public release

- Qualify the dependency/runtime maintenance identified by the retained audit work:
  D01/D03 together and evaluation of D02. Current .NET 8.0.31, SDK 8.0.425 and
  ImageSharp 4.1.2 pins are unchanged by the localization work. Recheck appropriate
  versions and the transitive vulnerability scan before public distribution.
- Validate a fresh install, upgrade from published 1.21.1, switching the installer
  language, Explorer component selection and uninstall in both languages. In particular,
  foreign model files and existing user preferences must survive.
- Check actual Windows font scaling/DPI, keyboard/accessibility, both themes and
  native menu rendering. Hidden render/layout tests do not replace this acceptance.
- Exercise the installed general corrections with actual media/models and cancellation.
- Obtain acceptance of the exact candidate before any publication. The README's
  public release remains 1.21.1.
