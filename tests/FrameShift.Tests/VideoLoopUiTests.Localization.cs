using System.Globalization;
using System.Windows.Forms;
using FrameShift.Core.Localization;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Theory]
    [InlineData("en", "Export Selection", "Export Loop")]
    [InlineData("zh-CN", "导出选区", "导出循环")]
    [InlineData("es", "Exportar selección", "Exportar bucle")]
    [InlineData("pt-BR", "Exportar seleção", "Exportar loop")]
    [InlineData("ru", "Экспортировать выделение", "Экспортировать цикл")]
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
