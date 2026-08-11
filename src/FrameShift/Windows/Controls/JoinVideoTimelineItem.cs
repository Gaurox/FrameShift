using System;
using System.Drawing;
using FrameShift.Core.FFprobe;

namespace FrameShift.Windows.Controls;

internal sealed class JoinVideoTimelineItem
{
    public JoinVideoTimelineItem(string sourcePath, int receivedIndex)
    {
        SourcePath = sourcePath;
        ReceivedIndex = receivedIndex;
        try
        {
            CreatedUtc = System.IO.File.GetCreationTimeUtc(sourcePath);
            ModifiedUtc = System.IO.File.GetLastWriteTimeUtc(sourcePath);
        }
        catch
        {
            CreatedUtc = DateTime.MinValue;
            ModifiedUtc = DateTime.MinValue;
        }
    }

    public string SourcePath { get; }

    public int ReceivedIndex { get; }

    public DateTime CreatedUtc { get; }

    public DateTime ModifiedUtc { get; }

    public JoinVideoProbeResult? Probe { get; set; }

    public Bitmap? Thumbnail { get; set; }

    public string? LoadError { get; set; }

    public bool IsLoaded { get; set; }

    public bool IsLoading { get; set; }

    public bool IsRemoved { get; set; }

    public double DurationSeconds => Probe?.DurationSeconds ?? 0d;

    public void DisposeThumbnail()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
