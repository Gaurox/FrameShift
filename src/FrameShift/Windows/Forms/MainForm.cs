using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "FrameShift";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 360);

        if (File.Exists(IconPaths.AppIcon))
        {
            Icon = new Icon(IconPaths.AppIcon);
        }

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "FrameShift",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point)
        };

        Controls.Add(titleLabel);
    }
}
