using System.Collections.Generic;
using FrameShift.Core.Actions;

namespace FrameShift.Core.AI.SeparateAudio;

internal sealed class StemSelection
{
    public bool Vocals { get; init; }
    public bool Drums { get; init; }
    public bool Bass { get; init; }
    public bool Other { get; init; }
    public bool Instrumental { get; init; }

    public bool HasAnyStem => Vocals || Drums || Bass || Other || Instrumental;

    public bool NeedsInternalDrums => (Drums || Instrumental) && !Drums;
    public bool NeedsInternalBass => (Bass || Instrumental) && !Bass;
    public bool NeedsInternalOther => (Other || Instrumental) && !Other;

    public static StemSelection FromOptions(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue(ActionOptionKeys.Stems, out var raw) || string.IsNullOrWhiteSpace(raw))
            return new StemSelection { Vocals = true, Drums = true, Bass = true, Other = true, Instrumental = true };

        var parts = raw.Split(',');
        var set = new HashSet<string>(parts, System.StringComparer.OrdinalIgnoreCase);

        return new StemSelection
        {
            Vocals = set.Contains("vocals"),
            Drums = set.Contains("drums"),
            Bass = set.Contains("bass"),
            Other = set.Contains("other"),
            Instrumental = set.Contains("instrumental"),
        };
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var parts = new List<string>();
        if (Vocals) parts.Add("vocals");
        if (Drums) parts.Add("drums");
        if (Bass) parts.Add("bass");
        if (Other) parts.Add("other");
        if (Instrumental) parts.Add("instrumental");
        return new Dictionary<string, string>
        {
            [ActionOptionKeys.Stems] = string.Join(",", parts)
        };
    }
}
