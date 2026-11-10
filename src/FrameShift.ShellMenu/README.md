# Classic FrameShift menus — 1.23 integration

This C#/.NET 8 win-x64 Native AOT DLL constructs the complete classic FrameShift
menus for video, audio, images and PDF OCR. One handler inserts **FrameShift**
then **FrameShift AI** consecutively at the position supplied by Explorer;
empty roots are omitted. It has no reference to the application assembly, WinForms, ONNX or
multimedia engines. `IShellExtInit` copies a bounded CF_HDROP selection;
`IContextMenu` builds a flat menu and launches the same installed `FrameShift.exe`
only after an enabled command is invoked. Cut Video retains `--action cut-video`
and therefore the existing Find Loop workflow.

## Data and selection contract

- CLSID: `{F6740A5A-42BC-4716-9512-531F0B254862}`, `ThreadingModel=Apartment`.
- Descriptions: HKLM64 `Software\FrameShift\ShellMenus`, schema 2 and an exact
  `ExecutablePath` matching the application beside this DLL's `Shell/<hash>` path.
- Per-extension command descriptions retain the previous relative
  `<extension>\shell\FrameShift[AI]\shell\<command>` shape. The installer moves these
  tree out of `SystemFileAssociations`; the native data key is not a Shell verb.
  No static-verb masking trick is used.
- Existing captions/icons/component choices are reused. The three extraction
  children provide a separate `FlatMUIVerb` caption, localized through the
  application's existing language synchronizer. Unknown actions are not executed.
- Extensions: MP4, MKV, AVI, MOV, WebM, M4V; MP3, WAV, WAVE, FLAC, M4A, OGG,
  AAC, WMA; PNG, JPG, JPEG, WebP, BMP; PDF (AI OCR only). Multiple extensions
  within one family use the intersection of installed components and actual
  action support. Mixed families and other types are rejected. Convert Audio,
  noise removal and audio separation retain their narrower supported formats.
- With every component selected: video has 18 classic + 4 AI actions; MP3 has
  7 + 3; images have 8 + 8; PDF has only the AI OCR action. The 46 explicit
  command variants reuse the catalog's arities and fixed CLI arguments,
  including all five `--rmbg-model` values. No arbitrary registry command is run.
- Single-file editors are visible but disabled for multiple inputs; Join Videos
  requires two inputs. Its existing editor still controls the actual join order.
- At most 1,000 dropped items are copied. Conservative Windows command-line
  bounds disable oversized launches; inputs are never silently truncated.
- Actual launch requires existing files, uses discrete fixed arguments,
  `UseShellExecute=false`, `CreateNoWindow=true`, and no command interpreter.
- A bounded process-lifetime icon cache keeps menu bitmaps alive across host
  queries. Missing icons do not remove actions. The DPI/theme result still needs
  installed visual qualification.

## Build and lifetime

Use the root `build_installer.ps1` for every release. It restores all projects
locked, retains the complete application test suite, publishes the production
and separate fixture DLLs with AOT 8.0.33, and runs the native COM probe before
Inno Setup. Only the production DLL and its manifest enter the payload, under
`Shell/<DLL SHA-256>/`. License texts are matched to the compiler package.
The fixture/probe/test registry keys are not installed.

Native AOT cannot unload its DLL. `DllCanUnloadNow` returns `S_FALSE`; no code
forces Explorer or another host to exit. Different builds use different paths,
including candidates with the same version number. Existing hosts can retain
older code until they exit. The installer uses `onlyifdoesntexist` for these
immutable files and `uninsrestartdelete` for locked files at uninstall. Native
registration/metadata are removed explicitly; old build copies remain owned by
the Inno uninstall log. Actual activation, upgrade and uninstall need manual QA.

The unified candidate uses a different CLSID from the video-only candidate
`{A95006B5-BD87-4F69-9F52-14B8EA06DA33}`. The installer removes the former
`FrameShift.NativeVideo` registrations and migrates both static roots to one
`FrameShift.NativeMenus` handler per selected extension. Schema 2 makes any
still-loaded video-only handler decline the new data, avoiding duplicate roots.
It prepares all descriptions before activation and restores both static roots
on migration failure. These actual update paths still require installed QA.

An x64 DLL cannot load into Arm64 Explorer: the installer retains the existing
static menus on Arm64 rather than registering this component there. A direct
downgrade is not qualified; uninstall 1.23 before reinstalling 1.22. A loaded
handler refuses pre-1.23 application versions to avoid duplicating older menus.

## Isolated validation

`tests/FrameShift.ShellMenu.Fixture` compiles the same sources with a fixture-only
factory and two private test exports. Its launcher captures arguments instead
of starting processes. The host supplies a real synthetic COM IDataObject and
temporary registry data under a fresh HKCU `Software\FrameShift.Tests\ShellMenu`
key, which it removes at exit. These are not actual file associations.

The probe checks the production PE/factory and absence of test/registration
exports; 23 native checks cover the 18 video commands and adjacent classic/AI
roots across all supported extensions in five languages, icons,
offset/canonical invocation, exact action arguments, component subsets,
multi-selection, malformed ownership/schema and USER/GDI resource bounds.
They route every command and AI model into a captured launcher, exercise
AI-only/classic-only/core-only subsets, and verify old-schema rejection.
It does not register the component, show windows or drive the user's desktop.
In particular, native Win32 structure tests do not certify Explorer rendering.
