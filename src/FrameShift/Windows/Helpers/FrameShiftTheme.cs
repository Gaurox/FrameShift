using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftTheme
{
    private static readonly Color PrimaryBlueColor = Color.FromArgb(0x8E, 0xBA, 0xF3);
    private static readonly Color SecondaryBlueColor = Color.FromArgb(0x4D, 0x79, 0xB4);

    private static readonly Palette LightPalette = new(
        PageBackground: Color.FromArgb(0xF5, 0xF7, 0xFB),
        Surface: Color.White,
        SurfaceBorder: Color.FromArgb(0xD8, 0xE2, 0xF2),
        TextPrimary: Color.FromArgb(0x1F, 0x28, 0x3A),
        TextSecondary: Color.FromArgb(0x52, 0x5E, 0x73),
        TextMuted: Color.FromArgb(0x7A, 0x84, 0x95),
        AccentSoft: Color.FromArgb(0xEC, 0xF3, 0xFF),
        AccentSoftHover: Color.FromArgb(0xE4, 0xEE, 0xFE));

    private static readonly Palette DarkPalette = new(
        PageBackground: Color.FromArgb(0x16, 0x1A, 0x22),
        Surface: Color.FromArgb(0x20, 0x27, 0x35),
        SurfaceBorder: Color.FromArgb(0x3A, 0x46, 0x5A),
        TextPrimary: Color.FromArgb(0xF4, 0xF7, 0xFC),
        TextSecondary: Color.FromArgb(0xC3, 0xCD, 0xDB),
        TextMuted: Color.FromArgb(0x98, 0xA5, 0xB7),
        AccentSoft: Color.FromArgb(0x2A, 0x3B, 0x55),
        AccentSoftHover: Color.FromArgb(0x34, 0x4A, 0x6A));

    private static FrameShiftThemeMode s_effectiveTheme = FrameShiftThemeMode.Light;
    private static bool s_initialized;

    public static FrameShiftThemeMode EffectiveTheme => s_effectiveTheme;

    public static Color PrimaryBlue => PrimaryBlueColor;
    public static Color SecondaryBlue => SecondaryBlueColor;
    public static Color AccentText => GetAccentTextColor(s_effectiveTheme);
    public static Color PageBackground => ActivePalette.PageBackground;
    public static Color Surface => ActivePalette.Surface;
    public static Color SurfaceBorder => ActivePalette.SurfaceBorder;
    public static Color TextPrimary => ActivePalette.TextPrimary;
    public static Color TextSecondary => ActivePalette.TextSecondary;
    public static Color TextMuted => ActivePalette.TextMuted;
    public static Color AccentSoft => ActivePalette.AccentSoft;
    public static Color AccentSoftHover => ActivePalette.AccentSoftHover;

    /// <summary>
    /// Resolves the effective palette once, after WinForms initialization and before any form is created.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
        {
            return;
        }

        var preference = FrameShiftUiSettings.Load().GetThemePreference();
        s_effectiveTheme = ResolveEffectiveTheme(preference, WindowsThemeDetector.UsesLightTheme());
        ToolStripManager.Renderer = new FrameShiftMenuRenderer();
        s_initialized = true;
    }

    /// <summary>
    /// Applies a saved appearance choice to the existing WinForms surfaces.
    /// System is resolved against the current Windows apps theme at the time of the change.
    /// </summary>
    public static void ApplyPreference(FrameShiftThemePreference preference)
    {
        if (!s_initialized)
        {
            Initialize();
        }

        s_effectiveTheme = ResolveEffectiveTheme(preference, WindowsThemeDetector.UsesLightTheme());
        ToolStripManager.Renderer = new FrameShiftMenuRenderer();
        RefreshOpenForms();
    }

    internal static FrameShiftThemeMode ResolveEffectiveTheme(
        FrameShiftThemePreference preference,
        bool windowsUsesLightTheme)
    {
        return preference switch
        {
            FrameShiftThemePreference.Light => FrameShiftThemeMode.Light,
            FrameShiftThemePreference.Dark => FrameShiftThemeMode.Dark,
            _ => windowsUsesLightTheme ? FrameShiftThemeMode.Light : FrameShiftThemeMode.Dark
        };
    }

    internal static Color GetAccentTextColor(FrameShiftThemeMode theme)
    {
        return theme == FrameShiftThemeMode.Dark ? PrimaryBlueColor : SecondaryBlueColor;
    }

    internal static Color ResolveCurrentBackgroundColor(Color color)
    {
        if (Matches(color, LightPalette.PageBackground) || Matches(color, DarkPalette.PageBackground))
        {
            return PageBackground;
        }

        if (Matches(color, LightPalette.Surface) || Matches(color, DarkPalette.Surface))
        {
            return Surface;
        }

        if (Matches(color, LightPalette.SurfaceBorder) || Matches(color, DarkPalette.SurfaceBorder))
        {
            return SurfaceBorder;
        }

        if (Matches(color, LightPalette.AccentSoft) || Matches(color, DarkPalette.AccentSoft))
        {
            return AccentSoft;
        }

        if (Matches(color, LightPalette.AccentSoftHover) || Matches(color, DarkPalette.AccentSoftHover))
        {
            return AccentSoftHover;
        }

        return color;
    }

    internal static Color ResolveCurrentTextColor(Color color)
    {
        if (Matches(color, LightPalette.TextPrimary) || Matches(color, DarkPalette.TextPrimary))
        {
            return TextPrimary;
        }

        if (Matches(color, LightPalette.TextSecondary) || Matches(color, DarkPalette.TextSecondary))
        {
            return TextSecondary;
        }

        if (Matches(color, LightPalette.TextMuted) || Matches(color, DarkPalette.TextMuted))
        {
            return TextMuted;
        }

        if (Matches(color, GetAccentTextColor(FrameShiftThemeMode.Light)) ||
            Matches(color, GetAccentTextColor(FrameShiftThemeMode.Dark)))
        {
            return AccentText;
        }

        return color;
    }

    private static Palette ActivePalette => s_effectiveTheme == FrameShiftThemeMode.Dark ? DarkPalette : LightPalette;

    private static void RefreshOpenForms()
    {
        for (var index = 0; index < Application.OpenForms.Count; index++)
        {
            var form = Application.OpenForms[index];
            if (form is null || form.IsDisposed)
            {
                continue;
            }

            ApplyToControl(form);
            FrameShiftWindowChrome.RefreshTheme(form);
            form.Invalidate(true);
        }
    }

    private static void ApplyToControl(Control control)
    {
        control.BackColor = ResolveCurrentBackgroundColor(control.BackColor);
        control.ForeColor = ResolveCurrentTextColor(control.ForeColor);

        if (control is Button button)
        {
            button.FlatAppearance.BorderColor = ResolveCurrentBackgroundColor(button.FlatAppearance.BorderColor);
            button.FlatAppearance.MouseOverBackColor = ResolveCurrentBackgroundColor(button.FlatAppearance.MouseOverBackColor);
            button.FlatAppearance.MouseDownBackColor = ResolveCurrentBackgroundColor(button.FlatAppearance.MouseDownBackColor);
        }

        if (control is DataGridView grid)
        {
            ApplyToGridStyle(grid.DefaultCellStyle);
            ApplyToGridStyle(grid.AlternatingRowsDefaultCellStyle);
            ApplyToGridStyle(grid.ColumnHeadersDefaultCellStyle);
            ApplyToGridStyle(grid.RowHeadersDefaultCellStyle);
            grid.BackgroundColor = ResolveCurrentBackgroundColor(grid.BackgroundColor);
            grid.GridColor = ResolveCurrentBackgroundColor(grid.GridColor);
        }

        foreach (Control child in control.Controls)
        {
            ApplyToControl(child);
        }

        control.Invalidate();
    }

    private static void ApplyToGridStyle(DataGridViewCellStyle style)
    {
        style.BackColor = ResolveCurrentBackgroundColor(style.BackColor);
        style.ForeColor = ResolveCurrentTextColor(style.ForeColor);
        style.SelectionBackColor = ResolveCurrentBackgroundColor(style.SelectionBackColor);
        style.SelectionForeColor = ResolveCurrentTextColor(style.SelectionForeColor);
    }

    private static bool Matches(Color first, Color second)
    {
        return first.ToArgb() == second.ToArgb();
    }

    private readonly record struct Palette(
        Color PageBackground,
        Color Surface,
        Color SurfaceBorder,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color AccentSoft,
        Color AccentSoftHover);
}
