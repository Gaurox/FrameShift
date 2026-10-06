using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.AI;
using FrameShift.Core.AI.RemoveNoise;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class RemoveNoisePreflightTests(FrameShiftTestSettingsDirectory settingsDirectory)
{
    [Theory]
    [InlineData("remove-noise", ".wav")]
    [InlineData("remove-noise-video", ".mp4")]
    public void MissingModels_OffersDownloadBeforePreviewPicker_AndCancellationStopsPreflight(string actionId, string extension)
    {
        var modelsRoot = Path.Combine(settingsDirectory.DirectoryPath, "Models été");
        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath) : null;
        try
        {
            new AiModelSettings { ModelsDirectory = modelsRoot }.Save();
            AiModelStorage.InvalidateCache();
            // Keep this test away from the user's models and never start a download.
            Assert.Equal(Path.Combine(modelsRoot, "deepfilternet3_onnx", "enc.onnx"), DeepFilterNetModelLocator.EncPath);
            Assert.False(DeepFilterNetModelLocator.AllModelsExist());

            StaTest.Run(() =>
            {
                using var timer = new System.Windows.Forms.Timer { Interval = 20 };
                Type? firstDialogType = null;
                var timedOut = false;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Modal);
                    if (dialog is not null)
                    {
                        firstDialogType ??= dialog.GetType();
                        dialog.DialogResult = DialogResult.Cancel;
                        dialog.Close();
                    }
                    if (DateTime.UtcNow > deadline)
                        timedOut = true;
                };

                var options = new Dictionary<string, string>();
                timer.Start();
                var ready = (bool)typeof(Program).GetMethod("RunBatchActionPreflight", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [actionId, new[] { Path.Combine(settingsDirectory.DirectoryPath, "source été" + extension) }, options, new AppLogger()])!;
                timer.Stop();

                Assert.False(timedOut);
                Assert.Equal(typeof(DownloadModelForm), firstDialogType);
                Assert.False(ready);
                Assert.Empty(options);
                Assert.False(Directory.Exists(Path.Combine(modelsRoot, "deepfilternet3_onnx")));
            });
        }
        finally
        {
            if (previousSettings is null)
                File.Delete(AiModelSettings.ConfigFilePath);
            else
                File.WriteAllText(AiModelSettings.ConfigFilePath, previousSettings);
            AiModelStorage.InvalidateCache();
        }
    }
}
