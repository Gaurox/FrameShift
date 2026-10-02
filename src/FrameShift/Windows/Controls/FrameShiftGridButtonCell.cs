namespace FrameShift.Windows.Controls;

/// <summary>Keep the compact glyph while exposing its action text to accessibility clients.</summary>
internal sealed class FrameShiftGridButtonCell : DataGridViewButtonCell
{
    public FrameShiftGridButtonCell() { }

    protected override AccessibleObject CreateAccessibilityInstance() => new NamedButtonAccessibleObject(this);

    private sealed class NamedButtonAccessibleObject(FrameShiftGridButtonCell cell)
        : DataGridViewButtonCellAccessibleObject(cell)
    {
        public override string? Name
        {
            get => string.IsNullOrEmpty(cell.ToolTipText) ? base.Name : cell.ToolTipText;
            set => base.Name = value;
        }
    }
}
