using System.Globalization;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Localization;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Theory]
    [InlineData("en", "Find Loop", "Needs review")]
    [InlineData("zh-CN", "查找循环", "需检查")]
    [InlineData("es", "Buscar bucle", "Requiere revisión")]
    [InlineData("pt-BR", "Encontrar loop", "Precisa de revisão")]
    [InlineData("ru", "Найти цикл", "Требует проверки")]
    [InlineData("fr", "Trouver une boucle", "À vérifier")]
    [InlineData("ja", "ループを検索", "確認が必要")]
    [InlineData("id", "Cari loop", "Perlu ditinjau")]
    public void LocalizedCandidates_KeepExactBoundsAndInvariantBatchOptions(string language, string find, string review)
    {
        StaTest.Run(() =>
        {
            var previousUi = CultureInfo.CurrentUICulture;
            var previousNumeric = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                // A comma-decimal display culture must not leak into CLI values or IDs.
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                using var form = Form();
                Handles(form);
                Assert.Equal(find, Field<Button>(form, "_findLoop").Text);
                Field<TextBox>(form, "_loopMinimum").Text = "1,25";
                Assert.Equal(1.25, form.ReadLoopMinimum());
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
                var candidate = new VideoLoopCandidate(0.125, 2.375, 50);
                form.ApplyLoopAnalysis(new([candidate], 8));
                var grid = Field<DataGridView>(form, "_loopResults");
                Assert.Equal(review, grid.Rows[0].Cells["Junction"].Value);
                Assert.IsType<double>(grid.Rows[0].Cells["Duration"].Value);
                Assert.Contains("2,25", grid.Rows[0].Cells["Duration"].FormattedValue.ToString());
                if (language != "en")
                {
                    Assert.NotEqual("End (exclusive)", grid.Columns["End"]!.HeaderText);
                    Assert.NotEqual("Loop candidates", grid.AccessibleName);
                    Assert.DoesNotContain("Select a candidate", grid.AccessibleDescription);
                    Assert.DoesNotContain("Assessments are estimates", Field<Label>(form, "_loopStatus").Text);
                }

                Call(form, "ConfirmAllLoopCandidates");
                var selected = Assert.Single(form.SelectedLoopExports!);
                Assert.Equal(candidate, selected.Bounds);
                Assert.Equal("0.125:2.375", selected.CandidateId);
                var request = Assert.Single(Program.CreateLoopBatchRequests("vidéo été.mp4", form.Selection!,
                    form.SelectedLoopExports!, new Dictionary<string, string>(), new AppLogger()));
                Assert.Equal("natural", request.Options!["loop-mode"]);
                Assert.Equal("0.125", request.Options["loop-start"]);
                Assert.Equal("2.375", request.Options["loop-end"]);
                Assert.Equal("keep", request.Options["loop-audio"]);
                if (language != "en") Assert.DoesNotContain("end exclusive", request.QueueItemDescription!);

                var slow = new VideoLoopSettings(VideoLoopMode.PingPong, 0.75, 2, false);
                Assert.Equal("0.75", slow.ToOptions()["loop-speed"]);
                Assert.Equal(slow, VideoLoopSettings.FromOptions(slow.ToOptions()));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousNumeric;
                CultureInfo.CurrentUICulture = previousUi;
            }
        });
    }

    [Theory]
    [InlineData("en", "Export Selection", "Export Loop")]
    [InlineData("zh-CN", "导出选区", "导出循环")]
    [InlineData("es", "Exportar selección", "Exportar bucle")]
    [InlineData("pt-BR", "Exportar seleção", "Exportar loop")]
    [InlineData("ru", "Экспортировать выделение", "Экспортировать цикл")]
    [InlineData("fr", "Exporter la sélection", "Exporter la boucle")]
    [InlineData("ja", "選択範囲を書き出す", "ループを書き出す")]
    [InlineData("id", "Ekspor pilihan", "Ekspor loop")]
    public void SwitchingLoopContexts_PreservesLocalizedCutCommands(string language, string selection, string loop)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                using var form = Form();
                Handles(form);
                var export = Field<Button>(form, "_cutOk");
                Assert.Equal(selection, export.Text);
                Assert.Equal(L.Get("CutVideoForm_FrameShift_Cut_video_654b1c4b"), form.Text);
                Assert.Equal(L.Get("CutVideoForm_Cancel_998b9c48"), ((Button)form.CancelButton!).Text);

                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
                Assert.Equal(loop, export.Text);
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Cut));
                Assert.Equal(selection, export.Text);
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }
}
