using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class UiResourceLifetimeTests
{
    [Fact]
    public void Chrome_ReleasesItsPreviousIconOnReplacement_AndItsLastIconOnDisposal()
    {
        StaTest.Run(() =>
        {
            using var form = new Form();
            FrameShiftWindowChrome.Apply(form, "First", IconPaths.AppIcon, "");
            var first = form.Icon!;
            var firstHandle = first.Handle;
            FrameShiftWindowChrome.Apply(form, "Second", IconPaths.FrameShiftAiIcon, "");
            var second = form.Icon!;
            var secondHandle = second.Handle;
            try
            {
                Assert.NotSame(first, second);
                AssertIconHandleReleased(firstHandle);
                form.Dispose();
                AssertIconHandleReleased(secondHandle);
            }
            finally { first.Dispose(); second.Dispose(); }
        });
    }

    [Fact]
    public void Chrome_RepeatedApplyReusesTheIcon_AndRecreationKeepsTheSameOwnedInstance()
    {
        StaTest.Run(() =>
        {
            using var form = new RecreatedHandleForm();
            FrameShiftWindowChrome.Apply(form, "First");
            var icon = form.Icon!;
            try
            {
                _ = form.Handle;
                for (var i = 0; i < 20; i++)
                {
                    FrameShiftWindowChrome.Apply(form, $"Title {i}");
                    form.RecreateHiddenHandle();
                    Assert.Same(icon, form.Icon);
                    Assert.Equal($"Title {i}", form.Text);
                    using var bitmap = icon.ToBitmap();
                    Assert.True(bitmap.Width > 0);
                }
                Assert.False(form.Visible);
            }
            finally { form.Dispose(); icon.Dispose(); }
        });
    }

    [Fact]
    public void Chrome_DoesNotDisposeCallerIcons_AndMissingPathsKeepTheExistingIcon()
    {
        StaTest.Run(() =>
        {
            using var borrowed = new Icon(IconPaths.AppIcon);
            using var form = new Form { Icon = borrowed };
            FrameShiftWindowChrome.Apply(form, "Missing", "missing-icon.ico", "also-missing.ico");
            Assert.Same(borrowed, form.Icon);
            FrameShiftWindowChrome.Apply(form, "Owned", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
            var owned = form.Icon!;
            var ownedHandle = owned.Handle;
            form.Icon = borrowed;
            FrameShiftWindowChrome.Apply(form, "Owned again", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
            AssertIconHandleReleased(ownedHandle);
            form.Dispose();
            using var bitmap = borrowed.ToBitmap();
            Assert.True(bitmap.Width > 0);
        });
    }

    [Fact]
    public void Chrome_InvalidReplacementKeepsTheUsableIcon_AndFallbackIsOwned()
    {
        StaTest.Run(() =>
        {
            var invalidPath = Path.Combine(Path.GetTempPath(), $"FrameShift-F4-{Guid.NewGuid():N}.ico");
            try
            {
                File.WriteAllText(invalidPath, "invalid icon fixture");
                using var form = new Form();
                FrameShiftWindowChrome.Apply(form, "Fallback", "missing.ico", IconPaths.AppIcon);
                var icon = form.Icon!;
                var iconHandle = icon.Handle;
                Assert.ThrowsAny<Exception>(() => FrameShiftWindowChrome.Apply(form, "Invalid", invalidPath, ""));
                Assert.Same(icon, form.Icon);
                using var bitmap = icon.ToBitmap();
                form.Dispose();
                AssertIconHandleReleased(iconHandle);
            }
            finally { File.Delete(invalidPath); }
        });
    }

    [Fact]
    public void Header_ReplacesAndDisposesItsBitmap_AndKeepsCallerFontsBorrowed()
    {
        StaTest.Run(() =>
        {
            using var form = new Form();
            var header = FrameShiftUiFactory.CreateHeader("Title", "Metadata", IconPaths.AppIcon, "", "");
            form.Controls.Add(header);
            header.PerformLayout();
            var picture = header.Controls.OfType<PictureBox>().Single();
            var initial = picture.Image!;
            var titleFont = header.TitleLabel.Font;
            typeof(FrameShiftHeader).GetField("_iconSize", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(header, Size.Empty);
            header.PerformLayout();
            Assert.NotSame(initial, picture.Image);
            AssertBitmapDisposed(initial);
            var last = picture.Image!;
            using var borrowedFont = new Font("Segoe UI", 12);
            header.TitleLabel.Font = borrowedFont;
            header.Dispose();
            AssertBitmapDisposed(last);
            AssertFontDisposed(titleFont);
            Assert.True(borrowedFont.GetHeight() > 0);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void ToolTile_ReleasesItsBitmapOnReplacementAndDisposal_AndPolicyReleasesItsFont()
    {
        StaTest.Run(() =>
        {
            using var form = new Form();
            FrameShiftWindowPolicy.Initialize(form, new Size(400, 240), new Size(320, 200));
            var font = form.Font;
            var tile = new FrameShiftToolTile("Tool");
            form.Controls.Add(tile);
            tile.SetIcon(IconPaths.AppIcon);
            var first = tile.Image!;
            tile.SetIcon(IconPaths.FrameShiftAiIcon);
            AssertBitmapDisposed(first);
            var last = tile.Image!;
            form.Dispose();
            AssertBitmapDisposed(last);
            AssertFontDisposed(font);
        });
    }

    private static void AssertIconHandleReleased(IntPtr handle)
    {
        var alive = GetIconInfo(handle, out var info);
        if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
        if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
        Assert.False(alive, "The helper-owned native icon handle is still alive.");
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
    private static void AssertBitmapDisposed(Image image)
        => Assert.ThrowsAny<Exception>(() => { using var clone = (Image)image.Clone(); });
    private static void AssertFontDisposed(Font font)
        => Assert.ThrowsAny<Exception>(() => font.GetHeight());

    private sealed class RecreatedHandleForm : Form
    {
        public void RecreateHiddenHandle() => RecreateHandle();
    }
}
