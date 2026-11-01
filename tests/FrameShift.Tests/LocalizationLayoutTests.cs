using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class LocalizationLayoutTests
{
    [Theory]
    [InlineData(580, "提取文字和调整图像尺寸 - ")]
    [InlineData(360, "提取文字和调整图像尺寸 - ")]
    [InlineData(580, "Extraer texto y cambiar tamaño de imagen - ")]
    [InlineData(360, "Extraer texto y cambiar tamaño de imagen - ")]
    [InlineData(580, "Extrair texto e aumentar resolução da imagem - ")]
    [InlineData(360, "Extrair texto e aumentar resolução da imagem - ")]
    public void LongLocalizedActionCaptions_KeepSearchAndButtonsInsideTheViewport(int width, string prefix)
    {
        StaTest.Run(() =>
        {
            using var host = new Form { ClientSize = new Size(width, 520) };
            var panel = new ActionsPanel { Dock = DockStyle.Fill };
            host.Controls.Add(panel);
            panel.SetFiles([@"C:\missing\中文图像.png"], []);
            // Long translated captions must be measured without changing cached action IDs.
            foreach (var button in Descendants(panel).OfType<Button>())
                button.Text = prefix + button.Text;
            _ = host.Handle;
            foreach (var control in Descendants(host)) _ = control.Handle;
            for (var i = 0; i < 3; i++)
                foreach (var control in Descendants(host).Reverse()) control.PerformLayout();

            var search = Descendants(panel).OfType<TextBox>().Single();
            Assert.True(search.Right <= search.Parent!.ClientSize.Width,
                $"搜索框超出容器：{search.Bounds} / {search.Parent!.ClientSize}");
            var actions = Descendants(panel).OfType<FlowLayoutPanel>().Single(flow => flow.AutoScroll);
            Assert.NotEmpty(actions.Controls.OfType<Button>());
            Assert.All(actions.Controls.OfType<Button>(), button =>
                Assert.True(button.Right <= actions.ClientSize.Width,
                    $"操作按钮超出容器：{button.Text} {button.Bounds} / {actions.ClientSize}"));
            Assert.False(host.Visible);
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
