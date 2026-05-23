using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ImageToPdfAction : IFrameShiftAction
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    private const string SupportedExtensionsText = ".png, .jpg, .jpeg, .webp, .bmp";
    private readonly ImageToPdfPdfExporter _pdfExporter;

    public ImageToPdfAction(ImageToPdfPdfExporter pdfExporter)
    {
        _pdfExporter = pdfExporter;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "image-to-pdf",
        "Image to PDF",
        "Creates a simple one-page PDF from one or more images using the interactive page editor.");

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputPathRequired()));
        }

        if (!File.Exists(request.InputPath))
        {
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath)));
        }

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(sourceExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, SupportedExtensionsText)));
        }

        if (!ImageToPdfSettings.TryFromOptions(request.Options, out var settings, out var settingsError))
        {
            return Task.FromResult(new ActionExecutionResult(false, settingsError ?? MediaActionMessages.ImageToPdfSettingsInvalid()));
        }

        foreach (var item in settings!.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(item.SourcePath)));
            }

            var extension = Path.GetExtension(item.SourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(extension, SupportedExtensionsText)));
            }
        }

        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_image_to_pdf", ".pdf");
        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pdfExporter.Export(settings, outputPath);

            if (!File.Exists(outputPath))
            {
                DeleteIfExists(outputPath);
                return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.PdfFileNotCreated()));
            }

            return Task.FromResult(new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath));
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(outputPath);
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All));
        }
        catch (IOException)
        {
            DeleteIfExists(outputPath);
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.OutputWriteFailed()));
        }
        catch (Exception ex)
        {
            DeleteIfExists(outputPath);
            request.Logger.Log(ex.ToString());
            return Task.FromResult(new ActionExecutionResult(false, ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.PdfExportFailed())));
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
