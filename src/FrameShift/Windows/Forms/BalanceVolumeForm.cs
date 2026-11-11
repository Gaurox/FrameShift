using System.Drawing;
using FrameShift.Core.Actions;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class BalanceVolumeForm : Form
{
    public string SelectedIntensity { get; private set; } = "moderate";

    public BalanceVolumeForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(620, 350), new Size(380, 280));
        FrameShiftWindowChrome.Apply(this, L.Get("BalanceVolume_Name"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("BalanceVolume_Name"),
            L.Get("BalanceVolume_Description"), IconPaths.BalanceVolumeIcon, IconPaths.AppIcon, "♪");
        var light = Choice("light", "Light");
        var moderate = Choice("moderate", "Moderate");
        var strong = Choice("strong", "Strong");
        moderate.Checked = true;
        var content = FrameShiftUiFactory.CreateSection(L.Get("BalanceVolume_Intensity"),
            FrameShiftUiFactory.CreateChoiceRow(light, moderate, strong));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressAudioForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        var start = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BalanceVolume_Start"), true);
        start.DialogResult = DialogResult.OK;
        AcceptButton = start;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, start),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("BalanceVolume_Output")));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
    }

    private FrameShiftChoiceCard Choice(string intensity, string resource)
    {
        var card = new FrameShiftChoiceCard(L.Get("BalanceVolume_" + resource),
            L.Get("BalanceVolume_" + resource + "Description")) { Name = intensity };
        card.CheckedChanged += (_, _) => { if (card.Checked) SelectedIntensity = intensity; };
        return card;
    }
}
