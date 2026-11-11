using System.Windows.Forms;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class BalanceVolumeFormTests
{
    [Fact]
    public void Picker_DefaultsToModerate_AndSelectsOnlyOneIntensity()
    {
        StaTest.Run(() =>
        {
            using var form = new BalanceVolumeForm();
            var cards = Descendants(form).OfType<FrameShiftChoiceCard>().ToArray();
            Assert.Equal(3, cards.Length);
            Assert.Equal("moderate", form.SelectedIntensity);
            Assert.Equal("moderate", Assert.Single(cards, card => card.Checked).Name);
            foreach (var name in new[] { "light", "strong", "moderate" })
            {
                cards.Single(card => card.Name == name).Checked = true;
                Assert.Equal(name, form.SelectedIntensity);
                Assert.Equal(name, Assert.Single(cards, card => card.Checked).Name);
            }
            Assert.NotNull(form.AcceptButton);
            Assert.NotNull(form.CancelButton);
        });
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
