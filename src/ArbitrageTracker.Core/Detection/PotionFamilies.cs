using System.Text.RegularExpressions;
using ArbitrageTracker.Core.Domain;

namespace ArbitrageTracker.Core.Detection;

public readonly record struct DoseName(string Base, int Dose);

public sealed record FamilyVariant(int ItemId, string Name, int Dose);

public sealed record FamilyDef(string BaseName, int BuyLimit, IReadOnlyList<FamilyVariant> Variants);

public static partial class PotionFamilies
{
    [GeneratedRegex(@"^(?<base>.*)\((?<d>[1-4])\)$")]
    private static partial Regex DoseRegex();

    /// <summary>Parse "Prayer potion(3)" → ("Prayer potion", 3). Null if it doesn't match a 1-4 dose.</summary>
    public static DoseName? ParseDose(string name)
    {
        var m = DoseRegex().Match(name);
        if (!m.Success) return null;
        return new DoseName(m.Groups["base"].Value.TrimEnd(), int.Parse(m.Groups["d"].Value));
    }

    /// <summary>Group mappings into potion families that have a (4) variant plus at least one lower dose.</summary>
    public static IReadOnlyList<FamilyDef> Group(IEnumerable<ItemMapping> mappings)
    {
        var byBase = new Dictionary<string, List<FamilyVariant>>();
        var limitByBase = new Dictionary<string, int>();

        foreach (var m in mappings)
        {
            if (ParseDose(m.Name) is not { } dn) continue;
            if (!byBase.TryGetValue(dn.Base, out var list))
            {
                list = new List<FamilyVariant>();
                byBase[dn.Base] = list;
                limitByBase[dn.Base] = m.BuyLimit;   // doses share one limit
            }
            list.Add(new FamilyVariant(m.Id, m.Name, dn.Dose));
        }

        var families = new List<FamilyDef>();
        foreach (var (b, variants) in byBase)
        {
            if (variants.Count < 2) continue;                 // need a spread of doses
            if (variants.All(v => v.Dose != 4)) continue;     // must be able to sell as 4-dose
            families.Add(new FamilyDef(b, limitByBase[b], variants));
        }
        return families;
    }
}
