using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

/// <summary>
/// Shared WinForms menu renderer. Its colors are read from the effective FrameShift palette.
/// </summary>
internal sealed class FrameShiftMenuRenderer : ToolStripProfessionalRenderer
{
    public FrameShiftMenuRenderer()
        : base(new FrameShiftMenuColorTable())
    {
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == true ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        base.OnRenderArrow(e);
    }
}

internal sealed class FrameShiftMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => FrameShiftTheme.Surface;
    public override Color ToolStripBorder => FrameShiftTheme.SurfaceBorder;
    public override Color MenuBorder => FrameShiftTheme.SurfaceBorder;
    public override Color MenuItemBorder => FrameShiftTheme.PrimaryBlue;
    public override Color MenuItemSelected => FrameShiftTheme.AccentSoft;
    public override Color MenuItemSelectedGradientBegin => FrameShiftTheme.AccentSoft;
    public override Color MenuItemSelectedGradientEnd => FrameShiftTheme.AccentSoft;
    public override Color MenuItemPressedGradientBegin => FrameShiftTheme.AccentSoftHover;
    public override Color MenuItemPressedGradientEnd => FrameShiftTheme.AccentSoftHover;
    public override Color CheckBackground => FrameShiftTheme.AccentSoft;
    public override Color CheckSelectedBackground => FrameShiftTheme.AccentSoftHover;
    public override Color CheckPressedBackground => FrameShiftTheme.AccentSoftHover;
    public override Color ImageMarginGradientBegin => FrameShiftTheme.Surface;
    public override Color ImageMarginGradientMiddle => FrameShiftTheme.Surface;
    public override Color ImageMarginGradientEnd => FrameShiftTheme.Surface;
    public override Color SeparatorDark => FrameShiftTheme.SurfaceBorder;
    public override Color SeparatorLight => FrameShiftTheme.SurfaceBorder;
}
