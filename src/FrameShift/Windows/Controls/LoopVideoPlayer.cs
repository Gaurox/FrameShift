using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FrameShift.Core.FFmpeg;

namespace FrameShift.Windows.Controls;

/// <summary>Silent paced playback of a continuous rendered artifact, with bounded memory.</summary>
internal sealed class LoopVideoPlayer : PictureBox
{
    internal LoopVideoPlayer()
    {
        Dock = DockStyle.Fill;
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.Black;
        AccessibleName = L.Get("Loop_SilentPreview");
    }

    internal int PlayedFrames { get; private set; }

    internal async Task PlayAsync(string executable, FfmpegRunner runner, string file,
        int sourceWidth, int sourceHeight, CancellationToken token)
    {
        var scale = Math.Min(1d, 960d / Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        // -re paces decoding with the artifact's timestamps, including VFR and slow motion.
        // Binary stdout follows the runner's existing raw-video convention.
        await using var decoder = runner.StartRawVideoProcess(executable,
            ["-hide_banner", "-loglevel", "error", "-re", "-i", file, "-map", "0:v:0", "-an", "-sn", "-dn",
                "-vf", $"scale={width}:{height},format=bgr24", "-fps_mode", "passthrough", "-f", "rawvideo", "pipe:1"],
            false, false, "loop-preview", token);
        var pixels = new byte[width * height * 3];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await decoder.StandardOutput.ReadAsync(pixels.AsMemory(0, 1), token);
            if (read == 0) break;
            await decoder.StandardOutput.ReadExactlyAsync(pixels.AsMemory(1), token);
            token.ThrowIfCancellationRequested();
            if (IsDisposed) break;
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                for (var row = 0; row < height; row++)
                    Marshal.Copy(pixels, row * width * 3, data.Scan0 + row * data.Stride, width * 3);
            }
            finally { bitmap.UnlockBits(data); }
            var previous = Image; Image = bitmap; previous?.Dispose();
            PlayedFrames++;
        }
        await decoder.WaitForExitAsync(token);
        if (decoder.ExitCode != 0) throw new InvalidOperationException(L.Format("Loop_DecodePreviewFailed", decoder.StandardError));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { var image = Image; Image = null; image?.Dispose(); }
        base.Dispose(disposing);
    }
}
