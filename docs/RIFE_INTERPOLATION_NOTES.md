# RIFE Interpolation Notes

## Status

`Interpolate Video (RIFE)` is part of the active FrameShift surface in version `1.0.6`.

## Scope

This action is the local AI interpolation path:

- action id: `interpolate-video-rife`
- category: AI / video
- model behavior: `x2` native, `x4` built as chained `x2` passes
- launch mode: UI-first picker with preflight before processing
- output rule: adjacent file with unique naming
- runtime rule: local model only, no cloud dependency
- frame sizing: input frames are padded to the next multiple of `64`, then cropped back after inference

## Related active files

- `src/FrameShift/Core/Actions/RifeInterpolateVideoAction.cs`
- `src/FrameShift/Core/Actions/RifeInterpolateVideoSettings.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeFrameInterpolationEngine.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelCatalog.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelDefinition.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelDownloader.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelLocator.cs`
- `src/FrameShift/Windows/AI/RifeInterpolateVideoPickerForm.cs`
- `tests/FrameShift.Tests/RifeInterpolateVideoSettingsTests.cs`

## Packaging notes

- version metadata must stay aligned on `1.0.6`
- `installer/FrameShift.iss` must keep the `ai\\interpolate_video_rife` component and menu wiring
- release payload remains `self-contained win-x64`
