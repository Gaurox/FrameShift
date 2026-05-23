using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftUiMetrics
{
    public const int OuterPadding = 12;
    public const int HeaderHeight = 58;
    public const int FooterButtonHeight = 34;
    public const int CompactFooterHeight = 52;
    public const int PrimaryButtonWidth = 140;
    public const int SecondaryButtonWidth = 120;
    public const int FooterButtonGap = 10;
    public const int FooterBottomPadding = 12;
    public const int FooterRightPadding = 12;
    public const int SectionTitleHeight = 18;
    public const int LineGap = 8;
    public const int BlockGap = 10;
    public const int SectionContentGap = LineGap;
    public const int InfoLineHeight = 18;
    public const int PanelCornerRadius = 8;
    public const int InputCornerRadius = 5;
    public const int HeaderIconSize = 38;
    public const int EditorRailWidth = 258;
    public const int WideEditorRailWidth = 340;

    public static readonly Padding StandardSectionPadding = new(12, 10, 12, 12);
    public static readonly Padding StandardInfoCardPadding = new(10, 8, 10, 8);
    public static readonly Padding StandardTextInputPadding = new(10, 6, 10, 6);
}
