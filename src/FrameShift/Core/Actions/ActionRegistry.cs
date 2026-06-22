using System;
using System.Collections.Generic;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.AI.RemoveBackground;
using FrameShift.Core.AI.RemoveNoise;
using FrameShift.Core.AI.RemoveObject;
using FrameShift.Core.AI.SeparateAudio;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;

namespace FrameShift.Core.Actions;

public sealed class ActionRegistry
{
    private readonly Dictionary<string, IFrameShiftAction> _actions;

    public ActionRegistry(IEnumerable<IFrameShiftAction> actions)
    {
        _actions = new Dictionary<string, IFrameShiftAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in actions)
        {
            _actions[action.Descriptor.Id] = action;
        }
    }

    public IReadOnlyCollection<IFrameShiftAction> Actions => _actions.Values;

    public bool TryGet(string actionId, out IFrameShiftAction action)
    {
        return _actions.TryGetValue(actionId, out action!);
    }

    public static ActionRegistry CreateDefault()
    {
        var logger = new AppLogger();
        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        var ffmpegRunner = new FfmpegRunner(logger);

        return new ActionRegistry(
        [
            new MediaInfoAction(),
            new RemoveAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new AddSubtitlesToVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ExtractFramesAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CreateGifAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ExtractAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CutAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CutVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ResizeVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CompressVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CompressAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CompressImageAction(
                ffmpegRunner,
                toolLocator),
            new ResizeImageAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ConvertToIconAction(
                ffmpegRunner,
                toolLocator),
            new RotateFlipImageAction(
                ffmpegRunner,
                toolLocator),
            new RotateFlipVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CropImageAction(
                ffmpegRunner,
                toolLocator),
            new ImageToPdfAction(
                new ImageToPdfPdfExporter()),
            new CropVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ConvertAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ReverseAudioAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ChangePitchAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ChangeAudioSpeedAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ChangeVideoSpeedAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new ConvertImageAction(
                ffmpegRunner,
                toolLocator),
            new ConvertVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new InterpolateVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new RifeInterpolateVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new RemoveBackgroundAction(),
            new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Video,
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new RemoveObjectAction(),
            new UpscaleImageAction(),
            new UpscaleVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator),
            new SeparateAudioAction(),
            new RemoveNoiseAction(ffmpegRunner, ffprobeRunner, toolLocator),
            new RemoveNoiseVideoAction(
                ffmpegRunner,
                ffprobeRunner,
                toolLocator)
        ]);
    }
}
