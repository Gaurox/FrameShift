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
- `src/FrameShift/ProgramJoinVideos.cs`
- `src/FrameShift/ProgramRemoveObject.cs`
- `src/FrameShift/ProgramPickersConversion.cs`
- `src/FrameShift/ProgramPickersCut.cs`
- `src/FrameShift/ProgramPickersImage.cs`
- `src/FrameShift/ProgramPickersSubtitles.cs`
- `src/FrameShift/ProgramPickersResizeCompress.cs`
- `src/FrameShift/ProgramPickersTiming.cs`
- `src/FrameShift/FrameShift.csproj`
- `src/FrameShift.SubtitlesWorker/FrameShift.SubtitlesWorker.csproj`
- `src/FrameShift.SubtitlesWorker/Program.cs`
- `src/FrameShift.SubtitlesWorker/native-dml/THIRD_PARTY_NOTICES.txt`
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
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoAction.cs`
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoMode.cs`
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoPlanner.cs`
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoBurnAppearance.cs`
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoSettings.cs`
- `src/FrameShift/Core/Actions/AddSubtitlesToVideoSubtitleSourceLoader.cs`
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
- `src/FrameShift/Core/Actions/ExtractFramesSettings.cs`
- `src/FrameShift/Core/Actions/JoinVideosAction.cs`
- `src/FrameShift/Core/Actions/JoinVideosSettings.cs`
- `src/FrameShift/Core/Actions/JoinVideosPlanner.cs`
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
- `src/FrameShift/Core/FFprobe/JoinVideoProbeResult.cs`

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
- `src/FrameShift/Core/AI/AiModelDirectorySafety.cs`
- `src/FrameShift/Core/AI/AiModelSettings.cs`
- `src/FrameShift/Core/AI/OnnxProviderHelper.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesAction.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesModelCatalog.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesModelLocator.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesModelDownloader.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesAssPreset.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesAssDiagnostic.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesOutputFormat.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesProtocol.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesAss.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesProjectSerialization.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesSrt.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/SubtitleProject.cs`
- `src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesWorkerRunner.cs`
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
- `src/FrameShift/Core/AI/Upscale/UpscaleModelDefinition.cs`
- `src/FrameShift/Core/AI/Upscale/UpscaleModelCatalog.cs`
- `src/FrameShift/Core/AI/Upscale/ModelLocator.cs`
- `src/FrameShift/Core/AI/Upscale/ModelDownloader.cs`
- `src/FrameShift/Core/AI/Upscale/IUpscaleEngine.cs`
- `src/FrameShift/Core/AI/Upscale/UpscaleTiler.cs`
- `src/FrameShift/Core/AI/Upscale/UpscaleFrameProcessor.cs`
- `src/FrameShift/Core/AI/Upscale/UpscaleEngine.cs`
- `src/FrameShift/Core/AI/Upscale/UpscaleImageAction.cs`
- `src/FrameShift/Core/AI/VideoUpscale/VideoUpscaleEngine.cs`
- `src/FrameShift/Core/AI/VideoUpscale/UpscaleRawVideoPipeline.cs`
- `src/FrameShift/Core/Actions/UpscaleVideoAction.cs`
- `src/FrameShift/Core/Actions/UpscaleVideoSettings.cs`

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
- `src/FrameShift/Windows/AI/UpscaleImagePickerForm.cs`
- `src/FrameShift/Windows/AI/UpscaleVideoPickerForm.cs`
- `src/FrameShift/Windows/AI/CreateSubtitlesPickerForm.cs`

Contrôles :
- `src/FrameShift/Windows/Controls/SeekTrackBar.cs`
- `src/FrameShift/Windows/Controls/JoinVideoTimelineItem.cs`
- `src/FrameShift/Windows/Controls/JoinVideosTimelineControl.cs`

Formulaires :
- `src/FrameShift/Windows/Forms/MainForm.cs`
- `src/FrameShift/Windows/Forms/SettingsForm.cs`
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
- `src/FrameShift/Windows/Forms/JoinVideosForm.cs`
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
- `src/FrameShift/Windows/Helpers/FrameShiftThemePreference.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiSettings.cs`
- `src/FrameShift/Windows/Helpers/WindowsThemeDetector.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftMenuRenderer.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiFactory.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiLayout.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiPainter.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftWindowChrome.cs`
- `src/FrameShift/Windows/Helpers/IconPaths.cs`
- `src/FrameShift/Windows/Helpers/ImageBitmapHelper.cs`
- `src/FrameShift/Windows/Helpers/PreviewFrameHelper.cs`
- `src/FrameShift/Windows/Forms/AddSubtitlesToVideoPickerForm.cs`
- `src/FrameShift/Windows/Forms/AddSubtitlesToVideoBurnEditorForm.cs`
- `src/FrameShift/Windows/Helpers/PreviewLayoutHelper.cs`
- `src/FrameShift/Windows/Helpers/NaturalFileNameComparer.cs`

Responsabilités UI partagées :
- `FrameShiftUiMetrics.cs` centralise les métriques de géométrie UI actives : marges, hauteurs standard, footer, gaps, rayons, largeurs de rail.
- `FrameShiftUiLayout.cs` centralise les placements réutilisables sensibles à la largeur utile : sections titrées, footer, rangées de boutons.
- `FrameShiftEditorShellUi.cs` et `FrameShiftCropEditorUi.cs` portent les shells communs des écrans riches avec `TableLayoutPanel`, spacer explicite et hiérarchie stable.
- `FrameShiftWindowChrome.cs` reste le point d’entrée commun pour la barre de titre, `ShowIcon` et la sélection d’icône FrameShift / FrameShift AI.
- `FrameShiftTheme.cs` résout une seule fois la palette effective `System` / `Light` / `Dark` avant la première fenêtre ; `FrameShiftUiSettings.cs` persiste ce choix séparément des réglages IA.
- `FrameShiftMenuRenderer.cs` applique les couleurs de la palette aux menus WinForms appartenant à FrameShift.
- La stratégie DPI visible dans le code actif repose aujourd’hui sur `AutoScaleMode = AutoScaleMode.Dpi` sur les dialogues concernés, l’usage de métriques partagées et des `MinimumSize` explicites sur les écrans riches.

## Tests

Unit tests actifs :
- `tests/FrameShift.Tests/ChangePitchSettingsTests.cs`
- `tests/FrameShift.Tests/AddSubtitlesToVideoPlannerTests.cs`
- `tests/FrameShift.Tests/AddSubtitlesToVideoBurnTests.cs`
- `tests/FrameShift.Tests/AddSubtitlesToVideoBurnEditorFormTests.cs`
- `tests/FrameShift.Tests/AddSubtitlesToVideoSettingsTests.cs`
- `tests/FrameShift.Tests/ConvertToIconSettingsTests.cs`
- `tests/FrameShift.Tests/CreateGifSettingsTests.cs`
- `tests/FrameShift.Tests/CutAudioSettingsTests.cs`
- `tests/FrameShift.Tests/ExtractAudioCatalogTests.cs`
- `tests/FrameShift.Tests/ExtractFramesSettingsTests.cs`
- `tests/FrameShift.Tests/ImageAutoCropDetectorTests.cs`
- `tests/FrameShift.Tests/OutputPathHelperTests.cs`
- `tests/FrameShift.Tests/ProgramBatchTests.cs`
- `tests/FrameShift.Tests/ProgramCliTests.cs`
- `tests/FrameShift.Tests/ConversionBatchQueueMessageTests.cs`
- `tests/FrameShift.Tests/RifeInterpolateVideoSettingsTests.cs`
- `tests/FrameShift.Tests/UpscaleVideoSettingsTests.cs`
- `tests/FrameShift.Tests/CreateSubtitlesTests.cs`
- `tests/FrameShift.Tests/VideoCompressionPlannerTests.cs`
- `tests/FrameShift.Tests/VideoConversionPlannerTests.cs`
- `tests/FrameShift.Tests/FrameShiftThemeTests.cs`
- `tests/FrameShift.Tests/JoinVideosSettingsTests.cs`
- `tests/FrameShift.Tests/JoinVideosPlannerTests.cs`
- `tests/FrameShift.Tests/JoinVideosFormTests.cs`
- `tests/FrameShift.Tests/JoinVideosTimelineControlTests.cs`

Assets de test utiles :
- `tests/input/`

## Notes

- Le launcher `Program` est maintenant découpé en plusieurs fichiers `partial` :
- `Program.cs` = entrypoint + routage principal ;
- `ProgramCli.cs` = parsing CLI ;
- `ProgramBatch.cs` = batch, progression, annulation ;
- `ProgramAiPreflight.cs` = préflight IA ;
- `ProgramImageToPdf.cs` = flux single-instance `Image to PDF` ;
- `ProgramJoinVideos.cs` = flux mono-action multi-instance `Join Videos` ;
- `ProgramPickers*.cs` = fallback UI et collecte d'options par familles d'actions, y compris le picker mode + fichier de `add-subtitles-video`.
- `Image to PDF` reste un module interactif lancé par ce launcher `Program`, pas un exécutable séparé.
- `Join Videos` reste un module interactif mono-piste lancé par `Program` : sa timeline conserve les occurrences du même fichier, son pipe local agrège les invocations Explorer, et le core ne reçoit qu'une opération finalisée via `JoinVideosSettings`.
- `CropImageForm` et `CropVideoForm` partagent maintenant une base UI proche via `FrameShiftCropEditorUi.cs`, avec auto-crop visuel et navigation de preview.
- `Add Subtitles to Video` (`add-subtitles-video`) reste une seule action produit : `SelectableTrack` conserve le planner conteneur léger du lot 1, tandis que `BurnIntoVideo` charge `.srt` / `.ass` / `.frameshift-subtitles.json`, génère au besoin un ASS temporaire dimensionné sur la vidéo, réencode la vidéo via FFmpeg et copie l’audio quand le conteneur de sortie le permet.
- Le lot 3 ajoute `AddSubtitlesToVideoBurnAppearance`, des overrides de style burn sérialisables, et un éditeur WinForms `AddSubtitlesToVideoBurnEditorForm` avec aperçu frame par frame. Le même générateur ASS temporaire sert à la prévisualisation comme à l’incrustation finale ; les `.ass` externes restent en passthrough avec les contrôles visuels désactivés.
- Le lot 4 étend ce flux avec un aperçu animé court basé sur un clip burn temporaire, des copies de travail `.ass` pour fiabiliser les chemins Windows complexes, un probe enrichi (`MediaProbeResult`) pour rotation/HDR, et des nettoyages complets des previews temporaires à chaque annulation ou refresh obsolète.
- Le lot 5 ferme l’intégration produit : `ActionRegistry` l’enregistre définitivement, `Program` / `ProgramCli` / `ProgramPickersSubtitles` gardent le routage UI-first/CLI, `installer/FrameShift.iss` ajoute le composant vidéo dédié, dépendances `ffmpeg` / `ffprobe` et l’entrée Explorer vidéo `Add subtitles to video`, et la doc produit/architecture est alignée sur cette surface réelle.
- `Media Info` passe par `FfprobeRunner.TryProbeMediaInfoAsync(...)` puis `MediaInfoFormatter`.
- `DownloadModelForm` est maintenant un downloader IA partagé entre `remove-background` et `separate-audio`.
- `DownloadModelForm` est maintenant le downloader IA partagé des modules `remove-background`, `remove-noise`, `separate-audio` et `interpolate-video-rife`.
- `RemoveBackground` suit désormais un mini-catalogue de modèles (`fast`, `high-resolution`, `high-resolution-general`) inspiré des patterns déjà utilisés pour `Remove Object` et RIFE ; les deux variantes HR sont figées en CPU only dans `1.0.11`.
- Le catalogue `RemoveBackground` inclut aussi deux modèles **fournis par l'utilisateur** BRIA RMBG-2.0 (`bria-balanced`, `bria-high-quality`) marqués `ManualOnly` : aucun téléchargement/redistribution par FrameShift. Le préflight (`ProgramAiPreflight.EnsureBriaManualModelReady`) vérifie la présence + le SHA256 officiel et affiche `BriaModelNoticeForm` (Open BRIA page / Open folder / Re-check / Cancel, + Use anyway sur mismatch ; dialog adaptatif piloté par un délégué `Func<BriaModelStatus>`, Re-check revalide en place et enchaîne sur l'action) au lieu d'ouvrir `DownloadModelForm`.
- `ConversionBatchSession.cs` et `ProgressForm.cs` gèrent désormais une identité de queue distincte par invocation batch, ce qui permet d’empiler plusieurs relances du même fichier dans la fenêtre ouverte sans perte silencieuse.
- `IconPaths.cs` centralise aussi les icônes IA actives du dossier `Assets\Icons\ai`.
- Les assets upscale dédiés sont `Assets\Icons\ai\upscale_image.(ico|png)` et `upscale_video.(ico|png)` ; les démonstrations vidéo sont dans `screenshots\Gif_demos\demo_upscale_video.gif` et `screenshots\Video_demos\demo_upscale_video.mp4`.
- `ProgramRemoveObject.cs` gère le flux UI-first de `remove-object` (validation extension, ouverture `RemoveObjectEditorForm`), intercepté avant la file dans `Program.cs`.
- **Create Subtitle File** (`create-subtitles-audio` / `create-subtitles-video`) isole sherpa-onnx dans `FrameShift.SubtitlesWorker` pour éviter les collisions de `onnxruntime.dll` avec le runtime ONNX principal. Worker publié en dossier (pas PublishSingleFile) pour que `DirectML.dll` soit trouvé par `LoadLibrary`. Exécution GPU via DirectML avec fallback CPU automatique (init et inférence) ; 4 DLL natives DML dans `native-dml/` (sherpa-onnx 1.13.3 DML, ORT 1.24.4 DirectML, DirectML 1.15.4). FeatureDim par modèle : Base/Small=80, Turbo=128. Trois modèles : `whisper-base`, `whisper-small` (défaut), `whisper-turbo` (~3,1 GB, encodeur ONNX external data). `CreateSubtitlesPickerForm` affiche désormais un sélecteur radio 3 modèles et 3 formats de sortie exclusifs : `Standard SRT` (défaut), `Advanced ASS Subtitle`, `FrameShift Customization Project`. Quand `Advanced ASS Subtitle` est choisi, le formulaire révèle aussi 3 presets exclusifs : `Classic` (défaut), `Word Highlight`, `Progressive Reveal`. Voir `docs/CREATE_SUBTITLES.md` pour les détails complets.
- Le pipeline SRT `CreateSubtitles` passe désormais par un modèle interne `SubtitleProject` / `SubtitleSegment` / `SubtitleWord` pour conserver les timestamps mot par mot déjà fournis par le worker avant formatage `.srt`.
- Lot 2 ajoute un sérialiseur projet versionné `CreateSubtitlesProjectSerializer` (`frameshift-subtitle-project`, v1, extension suggérée `.frameshift-subtitles.json`) et un exporteur `CreateSubtitlesAssFormatter` pour un ASS classique valide.
- Lot 3 branche réellement ces sorties via `CreateSubtitlesOutputFormats`, `ProgramAiPreflight`, `ProgramCli` et `CreateSubtitlesPickerForm`, tout en gardant `Standard SRT` sélectionné par défaut et sans activer d'effets dynamiques ASS.
- Lot 4 ajoute `CreateSubtitlesAssPreset`, `CreateSubtitlesAssDiagnostic`, le câblage UI/CLI du preset ASS et deux rendus dynamiques limités (`Word Highlight`, `Progressive Reveal`) avec fallback automatique vers `Classic` pour les segments dont l'alignement mot à mot n'est pas fiable.
- L’état 1.16.0 garde un `RefinedDisplayStart` partagé entre `ASS`, `SRT`, projet sérialisé et burn vidéo ; en `Word Highlight`, la phrase complète apparaît dès ce début raffiné, tandis que `Progressive Reveal` reste progressif.
- `build_installer.ps1` est le script de release canonique ; `build_all.ps1`, `build_publish.ps1` et `build_publish.bat` délèguent tous vers lui sans logique de build parallèle.
- `AiModelDirectorySafety.cs` canonicalise les choix de dossier de modèles et refuse les racines / emplacements protégés ; l’ISS applique les mêmes règles avant installation et désinstallation.
- `AiModelSettings.cs` gère les préférences utilisateur IA persistées dans `%LOCALAPPDATA%\FrameShift\config\settings.json` ; clé `ModelsDirectory` optionnelle et repli sur le dossier par défaut si la valeur est dangereuse ou inutilisable.
- `AiModelStorage.RootDirectory` est désormais résolu dynamiquement depuis `AiModelSettings` ; les nouveaux dossiers de modèles créés par FrameShift reçoivent un marqueur d’appartenance, utilisé par l’uninstallateur pour ne jamais supprimer la racine configurable, ne retirer que les fichiers de modèles explicitement autorisés et ne supprimer un dossier que s’il devient vide.
- `OnnxProviderHelper.cs` centralise la création de session ONNX (DML → CPU), la détection d'échec DML à l'inférence et les messages utilisateur propres (sans chemins internes ONNX Runtime).
- `Upscale/ModelLocator.cs` sépare le stockage `upscale-image-onnx` / `upscale-video-onnx` et copie à la demande les anciens modèles valides depuis `upscale-onnx`.
- `UpscaleRawVideoPipeline.cs` porte le chemin rapide par défaut de `upscale-video` : FFmpeg décode/encode en `rawvideo` mémoire, `UpscaleVideoAction` garde un fallback automatique vers le pipeline BMP historique, et `UpscaleFrameProcessor.cs` réutilise maintenant ses buffers/tensors pour réduire les allocations par frame. Le profilage (juin 2026) a montré ce pipeline borné par l'inférence ONNX DirectML (copies/conversions/I/O négligeables) ; `UpscaleFrameProcessor` retourne désormais l'image upscalée sans `.Clone()` plein-frame redondant (resize in-place sur le chemin target≠natif), réduisant le pic mémoire sans changer la sortie.
- `BackgroundRemovalEngine.cs` utilise désormais `ProcessPixelRows` + accès direct aux buffers `DenseTensor` (au lieu des indexeurs pixel `image[x,y]` / `tensor[0,c,y,x]`) pour la construction du tenseur d'entrée, la construction du masque et le composite final ; gain mesuré ×5–×22 sur ces phases CPU pour les grandes images via le chemin `fast`/Bria ; les chemins `high-resolution` restent bornés par l'inférence CPU ; sortie bit-à-bit identique.
- `MainForm.cs` inclut désormais une section "AI models folder" avec Browse, Reset to default et Open folder.
- La référence documentaire DPI/UI du projet est maintenant `docs/UI_DPI_AUDIT.md`.
