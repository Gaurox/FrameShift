using FrameShift.Core.Helpers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using YamlDotNet.RepresentationModel;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace FrameShift.Core.AI.Ocr;

internal sealed class OcrEngine : IDisposable
{
    private InferenceSession _det;
    private InferenceSession _rec;
    private readonly OcrModelDefinition _model;
    private readonly string _directory;
    private readonly string[] _characters;
    public string Provider { get; private set; }

    public OcrEngine(OcrModelDefinition model, bool forceCpu = false, string? directory = null)
    {
        _model = model;
        _directory = directory ?? model.DirectoryPath;
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(_directory, "rec/inference.yml")))
            yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var post = (YamlMappingNode)root.Children[new YamlScalarNode("PostProcess")];
        var dictionary = (YamlSequenceNode)post.Children[new YamlScalarNode("character_dict")];
        _characters = new[]
        {
            ""
        }.Concat(dictionary.Children.Cast<YamlScalarNode>().Select(node => node.Value ?? "")).Append(" ").ToArray();
        if (_characters.Length != model.Classes)
            throw new InvalidDataException("The OCR model and character dictionary do not match.");
        (_det, Provider) = OnnxProviderHelper.CreateSessionPreferred(Path.Combine(_directory, "det/inference.onnx"), "OCR detector", forceCpu);
        try
        {
            (_rec, var recProvider) = OnnxProviderHelper.CreateSessionPreferred(Path.Combine(_directory, "rec/inference.onnx"), "OCR recognizer", forceCpu || Provider == "CPU");
            if (recProvider == "CPU" && Provider != "CPU")
            {
                try
                {
                    var (cpuDetector, _) = OnnxProviderHelper.CreateSessionPreferred(Path.Combine(_directory, "det/inference.onnx"), "OCR detector", true);
                    _det.Dispose();
                    _det = cpuDetector;
                }
                catch
                {
                    _rec.Dispose();
                    throw;
                }
            }
            if (recProvider == "CPU")
                Provider = "CPU";
        }
        catch
        {
            _det.Dispose();
            throw;
        }

        if (_rec.OutputMetadata.Values.First().Dimensions[^1] != model.Classes)
        {
            Dispose();
            throw new InvalidDataException("Unexpected OCR recognition output.");
        }
    }

    public static Image<Rgb24> LoadImage(string path)
    {
        var info = SafeImageReader.Identify(path);
        if ((long)info.Width * info.Height > 64_000_000)
            throw new InvalidDataException("The image is too large. Use an image containing at most 64 million pixels.");
        using var rgba = SafeImageReader.LoadFirstFrame<Rgba32>(path);
        rgba.Mutate(ctx => ctx.AutoOrient());
        if ((long)rgba.Width * rgba.Height > 64_000_000)
            throw new InvalidDataException("The image is too large. Use an image containing at most 64 million pixels.");
        var image = new Image<Rgb24>(rgba.Width, rgba.Height);
        rgba.ProcessPixelRows(image, (input, output) =>
        {
            for (var y = 0; y < input.Height; y++)
            {
                var src = input.GetRowSpan(y);
                var dst = output.GetRowSpan(y);
                for (var x = 0; x < src.Length; x++)
                {
                    var p = src[x];
                    var alpha = p.A / 255f;
                    dst[x] = new((byte)(p.R * alpha + 255 * (1 - alpha)), (byte)(p.G * alpha + 255 * (1 - alpha)), (byte)(p.B * alpha + 255 * (1 - alpha)));
                }
            }
        });
        return image;
    }

    public IReadOnlyList<OcrLine> Recognize(Image<Rgb24> image, int rotation, Action<int, string>? progress, CancellationToken token)
    {
        using var oriented = image.Clone();
        if (rotation != 0)
            oriented.Mutate(c => c.Rotate((RotateMode)rotation));
        const int tileSize = 1536, overlap = 128;
        var polygons = new List<OcrPoint[]>();
        var positions = new List<(int x, int y)>();
        for (var y = 0; y < oriented.Height; y += tileSize - overlap)
            for (var x = 0; x < oriented.Width; x += tileSize - overlap)
                positions.Add((x, y));
        for (var i = 0; i < positions.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (x, y) = positions[i];
            var tile = new Rectangle(x, y, Math.Min(tileSize, oriented.Width - x), Math.Min(tileSize, oriented.Height - y));
            using var crop = oriented.Clone(c => c.Crop(tile));
            var w = Math.Max(32, (int)Math.Round(crop.Width / 32d) * 32);
            var h = Math.Max(32, (int)Math.Round(crop.Height / 32d) * 32);
            using var resized = crop.Clone(c => c.Resize(w, h));
            var input = MakeInput(resized, true);
            var output = Run(input, true, token);
            var boxes = OcrGeometry.Detect(output.Values, output.Width, output.Height, _model.BoxThreshold, token);
            polygons.AddRange(boxes.Select(box => box.Select(p => new OcrPoint(p.X * crop.Width / output.Width + x, p.Y * crop.Height / output.Height + y)).ToArray()));
            progress?.Invoke((i + 1) * 30 / positions.Count, $"Detecting text: tile {i + 1} of {positions.Count}");
        }

        polygons = OcrGeometry.MergeTiles(polygons);
        var lines = new List<OcrLine>();
        var processed = 0;
        foreach (var polygon in polygons.OrderBy(p => p.Min(v => v.Y)).ThenBy(p => p.Min(v => v.X)))
        {
            token.ThrowIfCancellationRequested();
            using var crop = OcrGeometry.Rectify(oriented, polygon);
            if (crop.Height > crop.Width * 1.5)
                crop.Mutate(c => c.Rotate(RotateMode.Rotate270));
            var width = Math.Clamp((int)Math.Ceiling(48d * crop.Width / crop.Height), 16, 3200);
            var paddedWidth = Math.Max(160, (int)Math.Ceiling(width / 32d) * 32);
            using var resized = crop.Clone(c => c.Resize(width, 48));
            var input = MakeInput(resized, false, paddedWidth);
            var result = Run(input, false, token);
            var decoded = Decode(result.Values, result.Width, result.Height, _characters);
            if (!string.IsNullOrWhiteSpace(decoded.Text))
            {
                var points = polygon.Select(p => Unrotate(p, image.Width, image.Height, rotation)).ToArray();
                var line = new OcrLine(decoded.Text, decoded.Confidence, points);
                var duplicate = lines.FindIndex(existing => OcrGeometry.Overlaps(existing, line, .65));
                if (duplicate < 0)
                    lines.Add(line);
                else if (line.Confidence > lines[duplicate].Confidence)
                    lines[duplicate] = line;
            }

            progress?.Invoke(30 + (++processed) * 65 / Math.Max(1, polygons.Count), $"Recognizing line {processed} of {polygons.Count}");
        }

        return OcrReadingOrder.Sort(lines);
    }

    internal static OcrPoint Unrotate(OcrPoint p, int width, int height, int rotation) => rotation switch
    {
        90 => new(p.Y, height - p.X),
        180 => new(width - p.X, height - p.Y),
        270 => new(width - p.Y, p.X),
        _ => p
    };
    private static DenseTensor<float> MakeInput(Image<Rgb24> image, bool detection, int? paddedWidth = null)
    {
        var w = paddedWidth ?? image.Width;
        var plane = w * image.Height;
        var data = new float[plane * 3];
        var mean = new[]
        {
            .485f,
            .456f,
            .406f
        };
        var std = new[]
        {
            .229f,
            .224f,
            .225f
        };
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = row[x];
                    var index = y * w + x;
                    data[index] = detection ? (pixel.B / 255f - mean[0]) / std[0] : pixel.B / 127.5f - 1;
                    data[plane + index] = detection ? (pixel.G / 255f - mean[1]) / std[1] : pixel.G / 127.5f - 1;
                    data[2 * plane + index] = detection ? (pixel.R / 255f - mean[2]) / std[2] : pixel.R / 127.5f - 1;
                }
            }
        });
        return new DenseTensor<float>(data, [1, 3, image.Height, w]);
    }

    private (float[] Values, int Width, int Height) Run(DenseTensor<float> input, bool detection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var session = detection ? _det : _rec;
            using var run = new RunOptions();
            using var registration = token.Register(() => run.Terminate = true);
            using var output = session.Run(new[] { NamedOnnxValue.CreateFromTensor("x", input) }, session.OutputNames, run);
            token.ThrowIfCancellationRequested();
            var tensor = output.First().AsTensor<float>();
            return detection ? (tensor.ToArray(), tensor.Dimensions[^1], tensor.Dimensions[^2]) : (tensor.ToArray(), tensor.Dimensions[1], tensor.Dimensions[2]);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        catch (OnnxRuntimeException) when (Provider != "CPU")
        {
            _det.Dispose();
            _rec.Dispose();
            (_det, Provider) = OnnxProviderHelper.CreateSessionPreferred(Path.Combine(_directory, "det/inference.onnx"), "OCR detector", true);
            (_rec, _) = OnnxProviderHelper.CreateSessionPreferred(Path.Combine(_directory, "rec/inference.onnx"), "OCR recognizer", true);
            return Run(input, detection, token);
        }
    }

    internal static (string Text, float Confidence) Decode(float[] output, int steps, int classes, string[] dictionary)
    {
        var builder = new System.Text.StringBuilder();
        var previous = -1;
        double score = 0;
        var count = 0;
        for (var t = 0; t < steps; t++)
        {
            var best = 0;
            var offset = t * classes;
            for (var i = 1; i < classes; i++)
                if (output[offset + i] > output[offset + best])
                    best = i;
            if (best != 0 && best != previous)
            {
                builder.Append(dictionary[best]);
                score += output[offset + best];
                count++;
            }

            previous = best;
        }

        return (builder.ToString(), count == 0 ? 0 : (float)(score / count));
    }

    public void Dispose()
    {
        _det.Dispose();
        _rec.Dispose();
    }
}
