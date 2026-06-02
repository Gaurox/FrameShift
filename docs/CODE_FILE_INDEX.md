# FrameShift Code File Index

Index court des fichiers actifs du projet.

Règles de lecture :
- code actif uniquement ;
- `references/` exclus ;
- `bin/` et `obj/` exclus ;
- focus sur les fichiers maintenus à la main.

## Entrypoints et packaging

- `src/FrameShift/Program.cs`
- `src/FrameShift/ProgramCli.cs`
- `src/FrameShift/ProgramBatch.cs`
- `src/FrameShift/ProgramAiPreflight.cs`
- `src/FrameShift/ProgramImageToPdf.cs`
- `src/FrameShift/ProgramRemoveObject.cs`
- `src/FrameShift/ProgramPickersConversion.cs`
- `src/FrameShift/ProgramPickersCut.cs`
- `src/FrameShift/ProgramPickersImage.cs`
- `src/FrameShift/ProgramPickersResizeCompress.cs`
- `src/FrameShift/ProgramPickersTiming.cs`
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
- `src/FrameShift/Core/Actions/RifeInterpolateVideoAction.cs`
- `src/FrameShift/Core/Actions/InterpolateVideoSettings.cs`
- `src/FrameShift/Core/Actions/RifeInterpolateVideoSettings.cs`
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
- `src/FrameShift/Core/AI/AiModelFileDownloader.cs`
- `src/FrameShift/Core/AI/AiModelSettings.cs`
- `src/FrameShift/Core/AI/OnnxProviderHelper.cs`
- `src/FrameShift/Core/AI/RemoveBackground/BackgroundRemovalModelCatalog.cs`
- `src/FrameShift/Core/AI/RemoveBackground/BackgroundRemovalModelDefinition.cs`
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
- `src/FrameShift/Core/AI/VideoInterpolation/RifePerfTimer.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifePerformanceMetrics.cs`
- `src/FrameShift/Core/AI/RemoveNoise/RemoveNoiseVideoSettings.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeFrameInterpolationEngine.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelCatalog.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelDefinition.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelDownloader.cs`
- `src/FrameShift/Core/AI/VideoInterpolation/RifeModelLocator.cs`
- `src/FrameShift/Core/AI/RemoveObject/IObjectRemovalEngine.cs`
- `src/FrameShift/Core/AI/RemoveObject/ModelDownloader.cs`
- `src/FrameShift/Core/AI/RemoveObject/ModelLocator.cs`
- `src/FrameShift/Core/AI/RemoveObject/ObjectRemovalEngine.cs`
- `src/FrameShift/Core/AI/RemoveObject/ObjectRemovalModelCatalog.cs`
- `src/FrameShift/Core/AI/RemoveObject/ObjectRemovalModelDefinition.cs`
- `src/FrameShift/Core/AI/RemoveObject/RemoveObjectAction.cs`

Helpers core utiles :
- `src/FrameShift/Core/Helpers/ImageAutoCropDetector.cs`

## Windows/UI

Batch et progression :
- `src/FrameShift/Windows/Batch/ConversionBatchSession.cs`
- `src/FrameShift/Windows/ProgressUI/ProgressForm.cs`

IA :
- `src/FrameShift/Windows/AI/DownloadModelForm.cs`
- `src/FrameShift/Windows/AI/RemoveNoiseAudioPickerForm.cs`
- `src/FrameShift/Windows/AI/RifeInterpolateVideoPickerForm.cs`
- `src/FrameShift/Windows/AI/SeparateAudioPickerForm.cs`
- `src/FrameShift/Windows/AI/RemoveNoiseVideoPickerForm.cs`
- `src/FrameShift/Windows/AI/RemoveObjectEditorForm.cs`
- `src/FrameShift/Windows/AI/BriaModelNoticeForm.cs`

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

Responsabilités UI partagées :
- `FrameShiftUiMetrics.cs` centralise les métriques de géométrie UI actives : marges, hauteurs standard, footer, gaps, rayons, largeurs de rail.
- `FrameShiftUiLayout.cs` centralise les placements réutilisables sensibles à la largeur utile : sections titrées, footer, rangées de boutons.
- `FrameShiftEditorShellUi.cs` et `FrameShiftCropEditorUi.cs` portent les shells communs des écrans riches avec `TableLayoutPanel`, spacer explicite et hiérarchie stable.
- `FrameShiftWindowChrome.cs` reste le point d’entrée commun pour la barre de titre, `ShowIcon` et la sélection d’icône FrameShift / FrameShift AI.
- La stratégie DPI visible dans le code actif repose aujourd’hui sur `AutoScaleMode = AutoScaleMode.Dpi` sur les dialogues concernés, l’usage de métriques partagées et des `MinimumSize` explicites sur les écrans riches.

## Tests

Unit tests actifs :
- `tests/FrameShift.Tests/ChangePitchSettingsTests.cs`
- `tests/FrameShift.Tests/ConvertToIconSettingsTests.cs`
- `tests/FrameShift.Tests/CreateGifSettingsTests.cs`
- `tests/FrameShift.Tests/CutAudioSettingsTests.cs`
- `tests/FrameShift.Tests/ExtractAudioCatalogTests.cs`
- `tests/FrameShift.Tests/ImageAutoCropDetectorTests.cs`
- `tests/FrameShift.Tests/OutputPathHelperTests.cs`
- `tests/FrameShift.Tests/RifeInterpolateVideoSettingsTests.cs`
- `tests/FrameShift.Tests/VideoCompressionPlannerTests.cs`
- `tests/FrameShift.Tests/VideoConversionPlannerTests.cs`

Assets de test utiles :
- `tests/input/`

## Notes

- Le launcher `Program` est maintenant découpé en plusieurs fichiers `partial` :
- `Program.cs` = entrypoint + routage principal ;
- `ProgramCli.cs` = parsing CLI ;
- `ProgramBatch.cs` = batch, progression, annulation ;
- `ProgramAiPreflight.cs` = préflight IA ;
- `ProgramImageToPdf.cs` = flux single-instance `Image to PDF` ;
- `ProgramPickers*.cs` = fallback UI et collecte d'options par familles d'actions.
- `Image to PDF` reste un module interactif lancé par ce launcher `Program`, pas un exécutable séparé.
- `CropImageForm` et `CropVideoForm` partagent maintenant une base UI proche via `FrameShiftCropEditorUi.cs`, avec auto-crop visuel et navigation de preview.
- `Media Info` passe par `FfprobeRunner.TryProbeMediaInfoAsync(...)` puis `MediaInfoFormatter`.
- `DownloadModelForm` est maintenant un downloader IA partagé entre `remove-background` et `separate-audio`.
- `DownloadModelForm` est maintenant le downloader IA partagé des modules `remove-background`, `remove-noise`, `separate-audio` et `interpolate-video-rife`.
- `RemoveBackground` suit désormais un mini-catalogue de modèles (`fast`, `high-resolution`, `high-resolution-general`) inspiré des patterns déjà utilisés pour `Remove Object` et RIFE ; les deux variantes HR sont figées en CPU only dans `1.0.11`.
- Le catalogue `RemoveBackground` inclut aussi deux modèles **fournis par l'utilisateur** BRIA RMBG-2.0 (`bria-balanced`, `bria-high-quality`) marqués `ManualOnly` : aucun téléchargement/redistribution par FrameShift. Le préflight (`ProgramAiPreflight.EnsureBriaManualModelReady`) vérifie la présence + le SHA256 officiel et affiche `BriaModelNoticeForm` (Open BRIA page / Open folder / Re-check / Cancel, + Use anyway sur mismatch ; dialog adaptatif piloté par un délégué `Func<BriaModelStatus>`, Re-check revalide en place et enchaîne sur l'action) au lieu d'ouvrir `DownloadModelForm`.
- `ConversionBatchSession.cs` et `ProgressForm.cs` gèrent désormais une identité de queue distincte par invocation batch, ce qui permet d’empiler plusieurs relances du même fichier dans la fenêtre ouverte sans perte silencieuse.
- `IconPaths.cs` centralise aussi les icônes IA actives du dossier `Assets\Icons\ai`.
- `ProgramRemoveObject.cs` gère le flux UI-first de `remove-object` (validation extension, ouverture `RemoveObjectEditorForm`), intercepté avant la file dans `Program.cs`.
- `build_installer.ps1` est le script canonique ; `build_all.ps1` délègue vers lui.
- `AiModelSettings.cs` gère les préférences utilisateur IA persistées dans `%LOCALAPPDATA%\FrameShift\config\settings.json` ; clé `ModelsDirectory` optionnelle.
- `AiModelStorage.RootDirectory` est désormais résolu dynamiquement depuis `AiModelSettings` (avec fallback automatique sur le chemin par défaut) ; `InvalidateCache()` à appeler après un changement de dossier.
- `OnnxProviderHelper.cs` centralise la création de session ONNX (DML → CPU), la détection d'échec DML à l'inférence et les messages utilisateur propres (sans chemins internes ONNX Runtime).
- `MainForm.cs` inclut désormais une section "AI models folder" avec Browse, Reset to default et Open folder.
- La référence documentaire DPI/UI du projet est maintenant `docs/UI_DPI_AUDIT.md`.
