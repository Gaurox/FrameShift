# FrameShift Code File Index

Index court des fichiers actifs du projet.

Règles de lecture :
- code actif uniquement ;
- `references/` exclus ;
- `bin/` et `obj/` exclus ;
- focus sur les fichiers maintenus à la main.

## Entrypoints et packaging

- `src/FrameShift/Program.cs`
- `src/FrameShift/FrameShift.csproj`
- `tests/FrameShift.Tests/FrameShift.Tests.csproj`
- `installer/FrameShift.iss`
- `build_installer.ps1`
- `build_all.ps1`

## Core/Actions

Infrastructure :
- `src/FrameShift/Core/Actions/ActionDescriptor.cs`
- `src/FrameShift/Core/Actions/ActionExecutionResult.cs`
- `src/FrameShift/Core/Actions/ActionOptionKeys.cs`
- `src/FrameShift/Core/Actions/ActionQueueRunner.cs`
- `src/FrameShift/Core/Actions/ActionRegistry.cs`
- `src/FrameShift/Core/Actions/ActionRequest.cs`
- `src/FrameShift/Core/Actions/CancellationScope.cs`
- `src/FrameShift/Core/Actions/ConversionActionHelper.cs`
- `src/FrameShift/Core/Actions/IConversionChoice.cs`
- `src/FrameShift/Core/Actions/IFrameShiftAction.cs`
- `src/FrameShift/Core/Actions/MediaActionMessages.cs`

Conversion et catalogues :
- `src/FrameShift/Core/Actions/AudioConversionCatalog.cs`
- `src/FrameShift/Core/Actions/ImageConversionCatalog.cs`
- `src/FrameShift/Core/Actions/VideoConversionCatalog.cs`
- `src/FrameShift/Core/Actions/VideoConversionPlan.cs`
- `src/FrameShift/Core/Actions/VideoConversionPlanner.cs`
- `src/FrameShift/Core/Actions/ConversionSelection.cs`
- `src/FrameShift/Core/Actions/ConvertAudioAction.cs`
- `src/FrameShift/Core/Actions/ConvertImageAction.cs`
- `src/FrameShift/Core/Actions/ConvertVideoAction.cs`

Compression :
- `src/FrameShift/Core/Actions/CompressAudioAction.cs`
- `src/FrameShift/Core/Actions/CompressAudioSettings.cs`
- `src/FrameShift/Core/Actions/CompressImageAction.cs`
- `src/FrameShift/Core/Actions/CompressImageSettings.cs`
- `src/FrameShift/Core/Actions/CompressVideoAction.cs`
- `src/FrameShift/Core/Actions/VideoCompressionCatalog.cs`
- `src/FrameShift/Core/Actions/VideoCompressionPlan.cs`
- `src/FrameShift/Core/Actions/VideoCompressionPlanner.cs`
- `src/FrameShift/Core/Actions/VideoCompressionProfile.cs`
- `src/FrameShift/Core/Actions/VideoCompressionTarget.cs`

Audio :
- `src/FrameShift/Core/Actions/ChangeAudioSpeedAction.cs`
- `src/FrameShift/Core/Actions/ChangePitchAction.cs`
- `src/FrameShift/Core/Actions/ChangePitchSettings.cs`
- `src/FrameShift/Core/Actions/ChangeSpeedSettings.cs`
- `src/FrameShift/Core/Actions/CutAudioAction.cs`
- `src/FrameShift/Core/Actions/CutAudioEditingService.cs`
- `src/FrameShift/Core/Actions/CutAudioSettings.cs`
- `src/FrameShift/Core/Actions/ReverseAudioAction.cs`

Vidéo :
- `src/FrameShift/Core/Actions/ChangeVideoSpeedAction.cs`
- `src/FrameShift/Core/Actions/CreateGifAction.cs`
- `src/FrameShift/Core/Actions/CreateGifSettings.cs`
- `src/FrameShift/Core/Actions/CropVideoAction.cs`
- `src/FrameShift/Core/Actions/CutVideoAction.cs`
- `src/FrameShift/Core/Actions/CutVideoMath.cs`
- `src/FrameShift/Core/Actions/CutVideoSettings.cs`
- `src/FrameShift/Core/Actions/ExtractAudioAction.cs`
- `src/FrameShift/Core/Actions/ExtractAudioCatalog.cs`
- `src/FrameShift/Core/Actions/ExtractFramesAction.cs`
- `src/FrameShift/Core/Actions/InterpolateVideoAction.cs`
- `src/FrameShift/Core/Actions/InterpolateVideoSettings.cs`
- `src/FrameShift/Core/Actions/RemoveAudioAction.cs`
- `src/FrameShift/Core/Actions/ResizeVideoAction.cs`
- `src/FrameShift/Core/Actions/ResizeSettings.cs`
- `src/FrameShift/Core/Actions/RotateFlipVideoAction.cs`
- `src/FrameShift/Core/Actions/RotateFlipSettings.cs`
- `src/FrameShift/Core/Actions/VideoCropSettings.cs`

Image :
- `src/FrameShift/Core/Actions/CompressImageAction.cs`
- `src/FrameShift/Core/Actions/ConvertToIconAction.cs`
- `src/FrameShift/Core/Actions/ConvertToIconSettings.cs`
- `src/FrameShift/Core/Actions/CropImageAction.cs`
- `src/FrameShift/Core/Actions/ImageToPdfAction.cs`
- `src/FrameShift/Core/Actions/ImageToPdfCropSettings.cs`
- `src/FrameShift/Core/Actions/ImageToPdfItemSettings.cs`
- `src/FrameShift/Core/Actions/ImageToPdfSettings.cs`
- `src/FrameShift/Core/Actions/ResizeImageAction.cs`
- `src/FrameShift/Core/Actions/RotateFlipImageAction.cs`

Media Info :
- `src/FrameShift/Core/Actions/MediaInfoAction.cs`
- `src/FrameShift/Core/Actions/MediaInfoFormatter.cs`

## Core/FFmpeg et FFprobe

- `src/FrameShift/Core/FFmpeg/FfmpegRunner.cs`
- `src/FrameShift/Core/FFprobe/FfprobeRunner.cs`
- `src/FrameShift/Core/FFprobe/MediaProbeResult.cs`
- `src/FrameShift/Core/FFprobe/MediaStreamInfo.cs`
- `src/FrameShift/Core/FFprobe/MediaInfoData.cs`

## Core/Helpers, logging, progress

- `src/FrameShift/Core/Helpers/ImageCropSupport.cs`
- `src/FrameShift/Core/Helpers/ImageToPdfGeometry.cs`
- `src/FrameShift/Core/Helpers/ImageToPdfPdfExporter.cs`
- `src/FrameShift/Core/Helpers/OutputPathHelper.cs`
- `src/FrameShift/Core/Helpers/ToolLocator.cs`
- `src/FrameShift/Core/Logging/AppLogger.cs`
- `src/FrameShift/Core/Progress/IProgressReporter.cs`

## Core/AI

- `src/FrameShift/Core/AI/AiModelDownloadProgress.cs`
- `src/FrameShift/Core/AI/RemoveBackground/BackgroundRemovalEngine.cs`
- `src/FrameShift/Core/AI/RemoveBackground/IBackgroundRemovalEngine.cs`
- `src/FrameShift/Core/AI/RemoveBackground/ModelDownloader.cs`
- `src/FrameShift/Core/AI/RemoveBackground/ModelLocator.cs`
- `src/FrameShift/Core/AI/RemoveBackground/RemoveBackgroundAction.cs`
- `src/FrameShift/Core/AI/SeparateAudio/AudioChunkReader.cs`
- `src/FrameShift/Core/AI/SeparateAudio/AudioSeparationEngine.cs`
- `src/FrameShift/Core/AI/SeparateAudio/AudioStemWriter.cs`
- `src/FrameShift/Core/AI/SeparateAudio/HostSpectro.cs`
- `src/FrameShift/Core/AI/SeparateAudio/ModelDownloader.cs`
- `src/FrameShift/Core/AI/SeparateAudio/ModelLocator.cs`
- `src/FrameShift/Core/AI/SeparateAudio/OverlapAddRing.cs`
- `src/FrameShift/Core/AI/SeparateAudio/SeparateAudioAction.cs`
- `src/FrameShift/Core/AI/RemoveNoise/DeepFilterNetModelLocator.cs`
- `src/FrameShift/Core/AI/RemoveNoise/RemoveNoiseAction.cs`
- `src/FrameShift/Core/AI/RemoveNoise/RemoveNoiseEngine.cs`
- `src/FrameShift/Core/AI/RemoveNoise/RemoveNoiseVideoAction.cs`
- `src/FrameShift/Core/AI/RemoveNoise/RemoveNoiseVideoSettings.cs`

Helpers core utiles :
- `src/FrameShift/Core/Helpers/ImageAutoCropDetector.cs`

## Windows/UI

Batch et progression :
- `src/FrameShift/Windows/Batch/ConversionBatchSession.cs`
- `src/FrameShift/Windows/ProgressUI/ProgressForm.cs`

IA :
- `src/FrameShift/Windows/AI/DownloadModelForm.cs`
- `src/FrameShift/Windows/AI/SeparateAudioPickerForm.cs`
- `src/FrameShift/Windows/AI/RemoveNoiseVideoPickerForm.cs`

Contrôles :
- `src/FrameShift/Windows/Controls/SeekTrackBar.cs`

Formulaires :
- `src/FrameShift/Windows/Forms/MainForm.cs`
- `src/FrameShift/Windows/Forms/MediaInfoForm.cs`
- `src/FrameShift/Windows/Forms/ConversionPickerForm.cs`
- `src/FrameShift/Windows/Forms/ChangePitchForm.cs`
- `src/FrameShift/Windows/Forms/ChangeSpeedForm.cs`
- `src/FrameShift/Windows/Forms/CompressAudioForm.cs`
- `src/FrameShift/Windows/Forms/CompressImageForm.cs`
- `src/FrameShift/Windows/Forms/CompressVideoForm.cs`
- `src/FrameShift/Windows/Forms/ConvertToIconForm.cs`
- `src/FrameShift/Windows/Forms/CreateGifForm.cs`
- `src/FrameShift/Windows/Forms/CropImageForm.cs`
- `src/FrameShift/Windows/Forms/CropVideoForm.cs`
- `src/FrameShift/Windows/Forms/CutAudioForm.cs`
- `src/FrameShift/Windows/Forms/CutVideoForm.cs`
- `src/FrameShift/Windows/Forms/ImageToPdfForm.cs`
- `src/FrameShift/Windows/Forms/InterpolateVideoForm.cs`
- `src/FrameShift/Windows/Forms/ResizeImageForm.cs`
- `src/FrameShift/Windows/Forms/ResizeMediaFormBase.cs`
- `src/FrameShift/Windows/Forms/ResizeVideoForm.cs`
- `src/FrameShift/Windows/Forms/RotateFlipImageForm.cs`
- `src/FrameShift/Windows/Forms/RotateFlipVideoForm.cs`

Helpers UI :
- `src/FrameShift/Windows/Helpers/ControlHelper.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftCropEditorUi.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftEditorShellUi.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftTheme.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiFactory.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiLayout.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiPainter.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftWindowChrome.cs`
- `src/FrameShift/Windows/Helpers/IconPaths.cs`
- `src/FrameShift/Windows/Helpers/ImageBitmapHelper.cs`
- `src/FrameShift/Windows/Helpers/PreviewFrameHelper.cs`
- `src/FrameShift/Windows/Helpers/PreviewLayoutHelper.cs`

## Tests

Unit tests actifs :
- `tests/FrameShift.Tests/ChangePitchSettingsTests.cs`
- `tests/FrameShift.Tests/ConvertToIconSettingsTests.cs`
- `tests/FrameShift.Tests/CreateGifSettingsTests.cs`
- `tests/FrameShift.Tests/CutAudioSettingsTests.cs`
- `tests/FrameShift.Tests/ExtractAudioCatalogTests.cs`
- `tests/FrameShift.Tests/ImageAutoCropDetectorTests.cs`
- `tests/FrameShift.Tests/OutputPathHelperTests.cs`
- `tests/FrameShift.Tests/VideoCompressionPlannerTests.cs`
- `tests/FrameShift.Tests/VideoConversionPlannerTests.cs`

Assets de test utiles :
- `tests/input/`

## Notes

- `Image to PDF` est un module interactif lancé via `Program.cs`, pas un exécutable séparé.
- `CropImageForm` et `CropVideoForm` partagent maintenant une base UI proche via `FrameShiftCropEditorUi.cs`, avec auto-crop visuel et navigation de preview.
- `Media Info` passe par `FfprobeRunner.TryProbeMediaInfoAsync(...)` puis `MediaInfoFormatter`.
- `DownloadModelForm` est maintenant un downloader IA partagé entre `remove-background` et `separate-audio`.
- `build_installer.ps1` est le script canonique ; `build_all.ps1` délègue vers lui.
