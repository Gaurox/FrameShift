using System.IO;

namespace FrameShift.Core.Actions;

public static class MediaActionMessages
{
    public const string UniqueOutputPathExhaustedErrorKey = "FrameShift.UniqueOutputPathExhausted";

    public static string InputPathRequired() => "Source file path is required.";

    public static string InputFileNotFound(string inputPath) =>
        $"Source file not found: {inputPath}";

    public static string UnsupportedSourceFormat(string sourceExtension, string supportedFormatsText) =>
        $"Unsupported source format '{sourceExtension}'. Supported formats: {supportedFormatsText}.";

    public static string UnsupportedTargetFormat(string targetId) =>
        $"Unsupported target format '{targetId}'.";

    public static string UnsupportedCompressionProfile(string profileId) =>
        $"Unsupported compression profile '{profileId}'.";

    public static string CompressionTargetSizeMissing() =>
        "Target file size is missing.";

    public static string CompressionTargetSizeInvalid() =>
        "Target file size is invalid.";

    public static string CompressionTargetTooSmall() =>
        "Target file size is too small for this video duration.";

    public static string SourceAndTargetMustDiffer() =>
        "The source and target formats must be different.";

    public static string ProbeFailed() =>
        "FrameShift could not inspect the source file. The file may be corrupted, incomplete, or unsupported.";

    public static string RequiredToolMissing(string toolName) =>
        $"Required tool '{toolName}' was not found in 'Tools\\ffmpeg'. FrameShift must use the project-local FFmpeg tools, not a system-wide fallback.";

    public static string MissingAudioTrack() =>
        "The source file does not contain an audio track.";

    public static string MissingVideoTrack() =>
        "The source file does not contain a video track.";

    public static string CropSettingsMissing() =>
        "Crop settings are missing.";

    public static string CropSettingsInvalid() =>
        "Crop settings are invalid.";

    public static string ImageToPdfSettingsMissing() =>
        "Image to PDF settings are missing.";

    public static string ImageToPdfSettingsInvalid() =>
        "Image to PDF settings are invalid.";

    public static string ImageToPdfRequiresAtLeastOneImage() =>
        "Add at least one image before exporting the PDF.";

    public static string ImageToPdfPageSizeInvalid() =>
        "The selected custom page size is invalid.";

    public static string JoinVideosSettingsMissing() =>
        "Join Videos settings are missing.";

    public static string JoinVideosSettingsInvalid() =>
        "Join Videos settings are invalid.";

    public static string JoinVideosRequiresAtLeastTwoVideos() =>
        "Join Videos requires at least two video files.";

    public static string JoinVideosMetadataIncomplete() =>
        "FrameShift could not determine the duration, frame rate, or dimensions needed to join these videos.";

    public static string JoinVideosCopyNotSafe() =>
        "These clips cannot be joined safely without re-encoding. Use automatic or normalize mode.";

    public static string JoinVideosHdrNormalizationNotSupported() =>
        "Join Videos v1 cannot normalize HDR clips. Use compatible HDR clips for direct joining, or convert them to SDR first.";

    public static string JoinVideosHdrMixNotSupported() =>
        "Join Videos v1 cannot mix HDR and SDR clips. Convert the clips to a common SDR format first.";

    public static string JoinVideosFailed() =>
        "FFmpeg failed while joining the videos.";

    public static string ImageLoadFailed(string inputPath) =>
        $"FrameShift could not load this image: {inputPath}";

    public static string ImageInvalid(string inputPath) =>
        $"The selected file is not a valid image: {inputPath}";

    public static string ImageFileInaccessible(string inputPath) =>
        $"The image file could not be accessed: {inputPath}";

    public static string ImageTooLarge(string inputPath, int width, int height) =>
        $"The image is too large for Image to PDF: {inputPath} ({width}x{height}).";

    public static string ImageToPdfItemLoadFailed(string inputPath) =>
        $"FrameShift could not load this image for the PDF: {inputPath}";

    public static string ImageToPdfPrintFailed() =>
        "FrameShift could not print this PDF layout.";

    public static string ResizeSettingsMissing() =>
        "Resize settings are missing.";

    public static string ResizeSettingsInvalid() =>
        "Resize settings are invalid.";

    public static string ConvertToIconSettingsMissing() =>
        "Convert to Icon settings are missing.";

    public static string ConvertToIconSettingsInvalid() =>
        "Convert to Icon settings are invalid.";

    public static string ConvertToIconNoSizesSelected() =>
        "Select at least one icon size.";

    public static string ConvertToIconFitModeInvalid() =>
        "Icon fit mode is invalid.";

    public static string ConvertToIconBackgroundInvalid() =>
        "Icon background is invalid.";

    public static string ConvertToIconFailed() =>
        "FFmpeg failed while creating this icon file.";

    public static string CompressAudioSettingsMissing() =>
        "Compress audio settings are missing.";

    public static string CompressAudioSettingsInvalid() =>
        "Compress audio settings are invalid.";

    public static string CompressAudioTargetTooSmall() =>
        "Target file size is too small for this audio duration.";

    public static string CompressAudioTargetUnsupportedFormat(string extension) =>
        $"Target file size is not supported for {extension.ToUpperInvariant()} files. Use MP3, M4A or OGG.";

    public static string CompressImageSettingsMissing() =>
        "Compress image settings are missing.";

    public static string CompressImageSettingsInvalid() =>
        "Compress image settings are invalid.";

    public static string CompressImageTargetUnsupportedFormat(string format) =>
        $"Target file size is not supported for {format.ToUpperInvariant()} output. Use JPG or WEBP.";

    public static string CompressImageTargetFailed() =>
        "FrameShift could not produce an image close to the requested target file size.";

    public static string ChangePitchSettingsMissing() =>
        "Change pitch settings are missing.";

    public static string ChangePitchSettingsInvalid() =>
        "Change pitch settings are invalid.";

    public static string ChangePitchSemitonesOutOfRange() =>
        "Pitch must stay between -24 and +24 semitones.";

    public static string ChangeSpeedSettingsMissing() =>
        "Change speed settings are missing.";

    public static string ChangeSpeedSettingsInvalid() =>
        "Change speed settings are invalid.";

    public static string ChangeSpeedFactorOutOfRange() =>
        "Speed must stay between 25% and 800%.";

    public static string CutAudioSettingsMissing() =>
        "Cut audio settings are missing.";

    public static string CutAudioSettingsInvalid() =>
        "Cut audio settings are invalid.";

    public static string CutAudioStartInvalid() =>
        "Start time must be 0 or greater.";

    public static string CutAudioEndInvalid() =>
        "End time must be greater than start time and inside the source duration.";

    public static string CutVideoSettingsMissing() =>
        "Cut video settings are missing.";

    public static string CutVideoSettingsInvalid() =>
        "Cut video settings are invalid.";

    public static string CreateGifSettingsMissing() =>
        "Create GIF settings are missing.";

    public static string CreateGifSettingsInvalid() =>
        "Create GIF settings are invalid.";

    public static string CreateGifStartTimeInvalid() =>
        "Start time must use seconds, MM:SS, or HH:MM:SS.";

    public static string CreateGifEndTimeInvalid() =>
        "End time must use seconds, MM:SS, or HH:MM:SS.";

    public static string CreateGifDurationInvalid() =>
        "Duration must be greater than zero and use seconds, MM:SS, or HH:MM:SS.";

    public static string CreateGifRangeInvalid() =>
        "The selected GIF range exceeds the source video duration.";

    public static string CreateGifStartOutsideSource() =>
        "Start time must be lower than the source video duration.";

    public static string CreateGifResolutionInvalid() =>
        "GIF resolution preset is invalid.";

    public static string CreateGifFpsInvalid() =>
        "GIF FPS preset is invalid.";

    public static string CreateGifQualityInvalid() =>
        "GIF quality preset is invalid.";

    public static string CreateGifFailed() =>
        "FFmpeg failed while creating this GIF.";

    public static string CreateGifPreviewFailed() =>
        "FFmpeg failed while rendering the GIF preview.";

    public static string CutVideoStartInvalid() =>
        "Start frame must be 1 or greater.";

    public static string CutVideoEndInvalid() =>
        "End frame must be greater than or equal to the start frame and inside the source frame count.";

    public static string CutVideoStartTimeInvalid() =>
        "Start time must use seconds, MM:SS, or HH:MM:SS.";

    public static string CutVideoEndTimeInvalid() =>
        "End time must use seconds, MM:SS, or HH:MM:SS.";

    public static string CutVideoDurationInvalid() =>
        "Duration must use seconds, MM:SS, or HH:MM:SS.";

    public static string VideoFrameRateUnavailable() =>
        "FrameShift could not determine the video frame rate needed for cut video.";

    public static string VideoFrameCountUnavailable() =>
        "FrameShift could not determine the total video frame count needed for cut video.";

    public static string VideoResolutionUnavailable() =>
        "FrameShift could not determine the video resolution.";

    public static string DurationUnavailable() =>
        "FrameShift could not determine the media duration needed for progress reporting.";

    public static string ExactlyOneSourceFileRequired(string actionName) =>
        $"{actionName} requires exactly one source file.";

    public static string NoAlternativeTargetFormatsAvailable() =>
        "No alternative target formats are available for this source file.";

    public static string CompressionProfileMissing() =>
        "Compression profile is missing.";

    public static string PreviewGenerationFailed() =>
        "FFmpeg failed to generate the preview frame.";

    public static string UnsupportedCommandLine(string expectedUsage) =>
        $"Unsupported command line. Expected: {expectedUsage}";

    public static string UnknownAction(string actionId) =>
        $"Unknown action '{actionId}'.";

    public static string AtLeastOneInputPathRequired() =>
        "At least one input path is required.";

    public static string UnhandledError(string message) =>
        $"Unhandled error: {message}";

    public static string CanceledBeforeProcessing() =>
        "Canceled before processing.";

    public static string OperationCanceled(string actionName) =>
        $"{actionName} was canceled.";

    public static string Completed(string actionName) =>
        $"{actionName} completed successfully.";

    public static string Failed(string actionName) =>
        $"{actionName} failed.";

    public static string FfmpegFailure(string mediaKind) =>
        $"FFmpeg failed while converting this {mediaKind} file.";

    public static string FrameExtractionFailure() =>
        "FFmpeg failed while extracting frames from this video.";

    public static string FrameExtractionProducedNoFiles() =>
        "FrameShift did not produce any PNG frames for this video.";

    public static string FrameExtractionModeInvalid() =>
        "Frame extraction mode must be all, first, last, or keyframes.";

    public static string FrameExtractionProducedNoVideoFrame() =>
        "FrameShift could not extract a video frame from this file.";

    public static string FrameExtractionProducedNoKeyframes() =>
        "FrameShift did not find any keyframes to extract from this video.";

    public static string PdfExportFailed() =>
        "FrameShift failed while exporting the PDF document.";

    public static string PdfFileNotCreated() =>
        "The PDF file could not be created.";

    public static string PdfImageLoadFailed(string inputPath) =>
        $"FrameShift could not read this image while building the PDF: {inputPath}";

    public static string OutputWriteFailed() =>
        "FrameShift could not write the output file next to the source file. Check permissions or whether the file is already open.";

    public static string UniqueOutputPathUnavailable() =>
        "FrameShift could not create a unique output file next to the source file.";

    public static string DiskFull() =>
        "There is not enough free disk space to create the output file next to the source file.";

    public static string FileCorrupted() =>
        "This file appears to be corrupted, incomplete, or unreadable.";

    public static string CodecUnsupported() =>
        "The selected codecs are not supported by the bundled FFmpeg.";

    public static string RotateFlipSettingsMissing() =>
        "Rotate / flip settings are missing.";

    public static string RotateFlipNoTransformSelected() =>
        "Select at least one rotation or flip before applying.";

    public static string InterpolateVideoSettingsMissing() =>
        "Interpolate video settings are missing.";

    public static string InterpolateVideoSettingsInvalid() =>
        "Target FPS must be greater than 0.";

    public static string InterpolateVideoFrameRateUnavailable() =>
        "FrameShift could not determine the video frame rate needed for interpolation.";

    public static string RifeInterpolateVideoSettingsMissing() =>
        "RIFE interpolate video settings are missing.";

    public static string RifeInterpolateVideoSettingsInvalid() =>
        "RIFE interpolate video settings are invalid.";

    public static string RifeInterpolateVideoModelMissing() =>
        "The selected RIFE model is not available.";

    public static string CreateSubtitlesModelMissing() =>
        "The Whisper Base subtitle model is not available.";

    public static string CreateSubtitlesNoSpeechRecognized() =>
        "No speech could be recognized in this media.";

    public static string SubtitleFileWriteFailed() =>
        "FrameShift could not write the subtitle file next to the source media.";

    public static string AddSubtitlesToVideoSettingsMissing() =>
        "Subtitle file path is missing.";

    public static string AddSubtitlesToVideoModeInvalid() =>
        "Subtitle mode is invalid.";

    public static string AddSubtitlesToVideoBurnSettingsInvalid() =>
        "Burn subtitle settings are invalid.";

    public static string AddSubtitlesToVideoSelectableSubtitleFormatInvalid() =>
        "Selectable subtitle track supports only external .srt subtitle files.";

    public static string AddSubtitlesToVideoBurnSubtitleFormatInvalid() =>
        "Burn Subtitles Into Video supports .srt, .ass and .frameshift-subtitles.json files.";

    public static string AddSubtitlesToVideoFailed() =>
        "FFmpeg failed while adding the subtitle track to this video.";

    public static string BurnSubtitlesToVideoFailed() =>
        "FFmpeg failed while burning subtitles into this video.";
}
