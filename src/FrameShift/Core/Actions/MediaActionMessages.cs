using System.IO;

namespace FrameShift.Core.Actions;

public static class MediaActionMessages
{
    public const string UniqueOutputPathExhaustedErrorKey = "FrameShift.UniqueOutputPathExhausted";

    public static string InputPathRequired() => L.Get("MediaActionMessages_Source_file_path_is_required_30779439");

    public static string InputFileNotFound(string inputPath) =>
        L.Format("MediaActionMessages_Source_file_not_found_0_1f4966d0",inputPath);

    public static string UnsupportedSourceFormat(string sourceExtension, string supportedFormatsText) =>
        L.Format("MediaActionMessages_Unsupported_source_format_0_Supporte_1c0ba5a8",sourceExtension,supportedFormatsText);

    public static string UnsupportedTargetFormat(string targetId) =>
        L.Format("MediaActionMessages_Unsupported_target_format_0_53b18af0",targetId);

    public static string UnsupportedCompressionProfile(string profileId) =>
        L.Format("MediaActionMessages_Unsupported_compression_profile_0_17ead845",profileId);

    public static string CompressionTargetSizeMissing() =>
        L.Get("MediaActionMessages_Target_file_size_is_missing_eeeca953");

    public static string CompressionTargetSizeInvalid() =>
        L.Get("MediaActionMessages_Target_file_size_is_invalid_c20ffe89");

    public static string CompressionTargetTooSmall() =>
        L.Get("MediaActionMessages_Target_file_size_is_too_small_for_th_73227135");

    public static string SourceAndTargetMustDiffer() =>
        L.Get("MediaActionMessages_The_source_and_target_formats_must_b_47baffde");

    public static string ProbeFailed() =>
        L.Get("MediaActionMessages_FrameShift_could_not_inspect_the_sou_797df9f2");

    public static string RequiredToolMissing(string toolName) =>
        L.Format("MediaActionMessages_Required_tool_0_was_not_found_in_Too_bc46e733",toolName);

    public static string MissingAudioTrack() =>
        L.Get("MediaActionMessages_The_source_file_does_not_contain_an__d6bdeae6");

    public static string MissingVideoTrack() =>
        L.Get("MediaActionMessages_The_source_file_does_not_contain_a_v_44345316");

    public static string CropSettingsMissing() =>
        L.Get("MediaActionMessages_Crop_settings_are_missing_3b8ea3cb");

    public static string CropSettingsInvalid() =>
        L.Get("MediaActionMessages_Crop_settings_are_invalid_574a16c8");

    public static string ImageToPdfSettingsMissing() =>
        L.Get("MediaActionMessages_Image_to_PDF_settings_are_missing_1ec96399");

    public static string ImageToPdfSettingsInvalid() =>
        L.Get("MediaActionMessages_Image_to_PDF_settings_are_invalid_604a7579");

    public static string ImageToPdfRequiresAtLeastOneImage() =>
        L.Get("MediaActionMessages_Add_at_least_one_image_before_export_63ba20bd");

    public static string ImageToPdfPageSizeInvalid() =>
        L.Get("MediaActionMessages_The_selected_custom_page_size_is_inv_9081d11d");

    public static string JoinVideosSettingsMissing() =>
        L.Get("MediaActionMessages_Join_Videos_settings_are_missing_f5cec2fd");

    public static string JoinVideosSettingsInvalid() =>
        L.Get("MediaActionMessages_Join_Videos_settings_are_invalid_00346352");

    public static string JoinVideosRequiresAtLeastTwoVideos() =>
        L.Get("MediaActionMessages_Join_Videos_requires_at_least_two_vi_3ba81022");

    public static string JoinVideosMetadataIncomplete() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_d_59d108ed");

    public static string JoinVideosCopyNotSafe() =>
        L.Get("MediaActionMessages_These_clips_cannot_be_joined_safely__6d99eccc");

    public static string JoinVideosHdrNormalizationNotSupported() =>
        L.Get("MediaActionMessages_Join_Videos_v1_cannot_normalize_HDR__f8530a50");

    public static string JoinVideosHdrMixNotSupported() =>
        L.Get("MediaActionMessages_Join_Videos_v1_cannot_mix_HDR_and_SD_d5720d10");

    public static string JoinVideosFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_joining_the_vide_36589267");

    public static string ImageLoadFailed(string inputPath) =>
        L.Format("MediaActionMessages_FrameShift_could_not_load_this_image_968f2a99",inputPath);

    public static string ImageInvalid(string inputPath) =>
        L.Format("MediaActionMessages_The_selected_file_is_not_a_valid_ima_c6093bca",inputPath);

    public static string ImageFileInaccessible(string inputPath) =>
        L.Format("MediaActionMessages_The_image_file_could_not_be_accessed_e9fef743",inputPath);

    public static string ImageTooLarge(string inputPath, int width, int height) =>
        L.Format("MediaActionMessages_The_image_is_too_large_for_Image_to__8d97981e",inputPath,width,height);

    public static string ImageToPdfItemLoadFailed(string inputPath) =>
        L.Format("MediaActionMessages_FrameShift_could_not_load_this_image_00b23820",inputPath);

    public static string ImageToPdfPrintFailed() =>
        L.Get("MediaActionMessages_FrameShift_could_not_print_this_PDF__41038d3c");

    public static string ResizeSettingsMissing() =>
        L.Get("MediaActionMessages_Resize_settings_are_missing_e0830c97");

    public static string ResizeSettingsInvalid() =>
        L.Get("MediaActionMessages_Resize_settings_are_invalid_b4d91706");

    public static string ConvertToIconSettingsMissing() =>
        L.Get("MediaActionMessages_Convert_to_Icon_settings_are_missing_67a8c632");

    public static string ConvertToIconSettingsInvalid() =>
        L.Get("MediaActionMessages_Convert_to_Icon_settings_are_invalid_cbdc0f96");

    public static string ConvertToIconNoSizesSelected() =>
        L.Get("MediaActionMessages_Select_at_least_one_icon_size_7f008481");

    public static string ConvertToIconFitModeInvalid() =>
        L.Get("MediaActionMessages_Icon_fit_mode_is_invalid_8c12bf00");

    public static string ConvertToIconBackgroundInvalid() =>
        L.Get("MediaActionMessages_Icon_background_is_invalid_bdf47c23");

    public static string ConvertToIconFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_creating_this_ic_25e4b631");

    public static string CompressAudioSettingsMissing() =>
        L.Get("MediaActionMessages_Compress_audio_settings_are_missing_1666cba2");

    public static string CompressAudioSettingsInvalid() =>
        L.Get("MediaActionMessages_Compress_audio_settings_are_invalid_1cbe6e45");

    public static string CompressAudioTargetTooSmall() =>
        L.Get("MediaActionMessages_Target_file_size_is_too_small_for_th_738e3e17");

    public static string CompressAudioTargetUnsupportedFormat(string extension) =>
        L.Format("MediaActionMessages_Target_file_size_is_not_supported_fo_05a118f8",extension.ToUpperInvariant());

    public static string CompressImageSettingsMissing() =>
        L.Get("MediaActionMessages_Compress_image_settings_are_missing_a413ad35");

    public static string CompressImageSettingsInvalid() =>
        L.Get("MediaActionMessages_Compress_image_settings_are_invalid_07875cb7");

    public static string CompressImageTargetUnsupportedFormat(string format) =>
        L.Format("MediaActionMessages_Target_file_size_is_not_supported_fo_ec643c4c",format.ToUpperInvariant());

    public static string CompressImageTargetFailed() =>
        L.Get("MediaActionMessages_FrameShift_could_not_produce_an_imag_ed60c880");

    public static string ChangePitchSettingsMissing() =>
        L.Get("MediaActionMessages_Change_pitch_settings_are_missing_45f3a944");

    public static string ChangePitchSettingsInvalid() =>
        L.Get("MediaActionMessages_Change_pitch_settings_are_invalid_3b56d420");

    public static string ChangePitchSemitonesOutOfRange() =>
        L.Get("MediaActionMessages_Pitch_must_stay_between_24_and_24_se_3f205fcc");

    public static string ChangeSpeedSettingsMissing() =>
        L.Get("MediaActionMessages_Change_speed_settings_are_missing_56d64a59");

    public static string ChangeSpeedSettingsInvalid() =>
        L.Get("MediaActionMessages_Change_speed_settings_are_invalid_42830d3c");

    public static string ChangeSpeedFactorOutOfRange() =>
        L.Get("MediaActionMessages_Speed_must_stay_between_25_and_800_ee9635a7");

    public static string CutAudioSettingsMissing() =>
        L.Get("MediaActionMessages_Cut_audio_settings_are_missing_d3b0db7c");

    public static string CutAudioSettingsInvalid() =>
        L.Get("MediaActionMessages_Cut_audio_settings_are_invalid_5884a959");

    public static string CutAudioStartInvalid() =>
        L.Get("MediaActionMessages_Start_time_must_be_0_or_greater_1d9ae73b");

    public static string CutAudioEndInvalid() =>
        L.Get("MediaActionMessages_End_time_must_be_greater_than_start__4a9c6734");

    public static string CutVideoSettingsMissing() =>
        L.Get("MediaActionMessages_Cut_video_settings_are_missing_2b38f7fc");

    public static string CutVideoSettingsInvalid() =>
        L.Get("MediaActionMessages_Cut_video_settings_are_invalid_0823ba26");

    public static string CreateGifSettingsMissing() =>
        L.Get("MediaActionMessages_Create_GIF_settings_are_missing_72dd21d3");

    public static string CreateGifSettingsInvalid() =>
        L.Get("MediaActionMessages_Create_GIF_settings_are_invalid_87fdb8f3");

    public static string CreateGifStartTimeInvalid() =>
        L.Get("MediaActionMessages_Start_time_must_use_seconds_MM_SS_or_709d843a");

    public static string CreateGifEndTimeInvalid() =>
        L.Get("MediaActionMessages_End_time_must_use_seconds_MM_SS_or_H_cd2f2978");

    public static string CreateGifDurationInvalid() =>
        L.Get("MediaActionMessages_Duration_must_be_greater_than_zero_a_cd547f68");

    public static string CreateGifRangeInvalid() =>
        L.Get("MediaActionMessages_The_selected_GIF_range_exceeds_the_s_efe658b4");

    public static string CreateGifStartOutsideSource() =>
        L.Get("MediaActionMessages_Start_time_must_be_lower_than_the_so_19f3cf08");

    public static string CreateGifResolutionInvalid() =>
        L.Get("MediaActionMessages_GIF_resolution_preset_is_invalid_6df445f6");

    public static string CreateGifFpsInvalid() =>
        L.Get("MediaActionMessages_GIF_FPS_preset_is_invalid_5928a936");

    public static string CreateGifQualityInvalid() =>
        L.Get("MediaActionMessages_GIF_quality_preset_is_invalid_d72ef35e");

    public static string CreateGifFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_creating_this_GI_0031d93b");

    public static string CreateGifPreviewFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_rendering_the_GI_bc25557d");

    public static string CutVideoStartInvalid() =>
        L.Get("MediaActionMessages_Start_frame_must_be_1_or_greater_52255f57");

    public static string CutVideoEndInvalid() =>
        L.Get("MediaActionMessages_End_frame_must_be_greater_than_or_eq_3c6ca0d1");

    public static string CutVideoStartTimeInvalid() =>
        L.Get("MediaActionMessages_Start_time_must_use_seconds_MM_SS_or_709d843a");

    public static string CutVideoEndTimeInvalid() =>
        L.Get("MediaActionMessages_End_time_must_use_seconds_MM_SS_or_H_cd2f2978");

    public static string CutVideoDurationInvalid() =>
        L.Get("MediaActionMessages_Duration_must_use_seconds_MM_SS_or_H_5bc455e6");

    public static string VideoFrameRateUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_v_5991e281");

    public static string VideoFrameCountUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_t_213b7a8e");

    public static string VideoResolutionUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_v_1aff5709");

    public static string DurationUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_m_ad695582");

    public static string ExactlyOneSourceFileRequired(string actionName) =>
        L.Format("MediaActionMessages_0_requires_exactly_one_source_file_9273a459",actionName);

    public static string NoAlternativeTargetFormatsAvailable() =>
        L.Get("MediaActionMessages_No_alternative_target_formats_are_av_d347170b");

    public static string CompressionProfileMissing() =>
        L.Get("MediaActionMessages_Compression_profile_is_missing_c53ce889");

    public static string PreviewGenerationFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_to_generate_the_previe_5df45f07");

    public static string UnsupportedCommandLine(string expectedUsage) =>
        L.Format("MediaActionMessages_Unsupported_command_line_Expected_0_dc6944e3",expectedUsage);

    public static string UnknownAction(string actionId) =>
        L.Format("MediaActionMessages_Unknown_action_0_d1f1bbb3",actionId);

    public static string AtLeastOneInputPathRequired() =>
        L.Get("MediaActionMessages_At_least_one_input_path_is_required_29721118");

    public static string UnhandledError(string message) =>
        L.Format("MediaActionMessages_Unhandled_error_0_8277dade",message);

    public static string CanceledBeforeProcessing() =>
        L.Get("MediaActionMessages_Canceled_before_processing_2b39cef2");

    public static string OperationCanceled(string actionName) =>
        L.Format("MediaActionMessages_0_was_canceled_558ad46c",actionName);

    public static string Completed(string actionName) =>
        L.Format("MediaActionMessages_0_completed_successfully_34ba3dd1",actionName);

    public static string Failed(string actionName) =>
        L.Format("MediaActionMessages_0_failed_9512ad41",actionName);

    public static string FfmpegFailure(string mediaKind) =>
        L.Format("MediaActionMessages_FFmpeg_failed_while_converting_this__39c65011", mediaKind switch
        {
            "video" => L.Get("Common_Video"),
            "audio" => L.Get("Common_Audio"),
            "image" => L.Get("Common_Image"),
            _ => mediaKind
        });

    public static string FrameExtractionFailure() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_extracting_frame_7f5b663d");

    public static string FrameExtractionProducedNoFiles() =>
        L.Get("MediaActionMessages_FrameShift_did_not_produce_any_PNG_f_27c32098");

    public static string FrameExtractionModeInvalid() =>
        L.Get("MediaActionMessages_Frame_extraction_mode_must_be_all_fi_e1c9212c");

    public static string FrameExtractionProducedNoVideoFrame() =>
        L.Get("MediaActionMessages_FrameShift_could_not_extract_a_video_b5073332");

    public static string FrameExtractionProducedNoKeyframes() =>
        L.Get("MediaActionMessages_FrameShift_did_not_find_any_keyframe_155f06ec");

    public static string PdfExportFailed() =>
        L.Get("MediaActionMessages_FrameShift_failed_while_exporting_th_532e94b2");

    public static string PdfFileNotCreated() =>
        L.Get("MediaActionMessages_The_PDF_file_could_not_be_created_522939db");

    public static string PdfImageLoadFailed(string inputPath) =>
        L.Format("MediaActionMessages_FrameShift_could_not_read_this_image_9b4f342a",inputPath);

    public static string OutputWriteFailed() =>
        L.Get("MediaActionMessages_FrameShift_could_not_write_the_outpu_affa83bf");

    public static string UniqueOutputPathUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_create_a_unique_a213e9cf");

    public static string DiskFull() =>
        L.Get("MediaActionMessages_There_is_not_enough_free_disk_space__1822a942");

    public static string FileCorrupted() =>
        L.Get("MediaActionMessages_This_file_appears_to_be_corrupted_in_1fd2a984");

    public static string CodecUnsupported() =>
        L.Get("MediaActionMessages_The_selected_codecs_are_not_supporte_7bc90e50");

    public static string RotateFlipSettingsMissing() =>
        L.Get("MediaActionMessages_Rotate_flip_settings_are_missing_bdc26372");

    public static string RotateFlipNoTransformSelected() =>
        L.Get("MediaActionMessages_Select_at_least_one_rotation_or_flip_2c8d7465");

    public static string InterpolateVideoSettingsMissing() =>
        L.Get("MediaActionMessages_Interpolate_video_settings_are_missi_00f178b3");

    public static string InterpolateVideoSettingsInvalid() =>
        L.Get("MediaActionMessages_Target_FPS_must_be_greater_than_0_90a20502");

    public static string InterpolateVideoFrameRateUnavailable() =>
        L.Get("MediaActionMessages_FrameShift_could_not_determine_the_v_7fc494c9");

    public static string RifeInterpolateVideoSettingsMissing() =>
        L.Get("MediaActionMessages_RIFE_interpolate_video_settings_are__2b90bbba");

    public static string RifeInterpolateVideoSettingsInvalid() =>
        L.Get("MediaActionMessages_RIFE_interpolate_video_settings_are__1e6494fc");

    public static string RifeInterpolateVideoModelMissing() =>
        L.Get("MediaActionMessages_The_selected_RIFE_model_is_not_avail_45542235");

    public static string CreateSubtitlesModelMissing() =>
        L.Get("MediaActionMessages_The_Whisper_Base_subtitle_model_is_n_11bf9a5a");

    public static string CreateSubtitlesNoSpeechRecognized() =>
        L.Get("MediaActionMessages_No_speech_could_be_recognized_in_thi_a94f31e6");

    public static string SubtitleFileWriteFailed() =>
        L.Get("MediaActionMessages_FrameShift_could_not_write_the_subti_7dc3518e");

    public static string AddSubtitlesToVideoSettingsMissing() =>
        L.Get("MediaActionMessages_Subtitle_file_path_is_missing_d13f5382");

    public static string AddSubtitlesToVideoModeInvalid() =>
        L.Get("MediaActionMessages_Subtitle_mode_is_invalid_b8c4f963");

    public static string AddSubtitlesToVideoBurnSettingsInvalid() =>
        L.Get("MediaActionMessages_Burn_subtitle_settings_are_invalid_6746684c");

    public static string AddSubtitlesToVideoSelectableSubtitleFormatInvalid() =>
        L.Get("MediaActionMessages_Selectable_subtitle_track_supports_o_a4f14538");

    public static string AddSubtitlesToVideoBurnSubtitleFormatInvalid() =>
        L.Get("MediaActionMessages_Burn_Subtitles_Into_Video_supports_s_bdb529cb");

    public static string AddSubtitlesToVideoFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_adding_the_subti_58c846f7");

    public static string BurnSubtitlesToVideoFailed() =>
        L.Get("MediaActionMessages_FFmpeg_failed_while_burning_subtitle_4f3b99b8");
}
