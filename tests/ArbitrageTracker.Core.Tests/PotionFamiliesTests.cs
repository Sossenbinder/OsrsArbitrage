using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.Domain;
using Xunit;

namespace ArbitrageTracker.Core.Tests;

public class PotionFamiliesTests
{
    private static ItemMapping Map(int id, string name) =>
        new(id, name, "examine", true, 1, 2, 2000, 50, "icon.png");

    [Theory]
    [InlineData("Prayer potion(4)", "Prayer potion", 4)]
    [InlineData("Super combat potion(1)", "Super combat potion", 1)]
    public void ParseDose_extractsBaseAndDose(string name, string expBase, int expDose)
    {
        var p = PotionFamilies.ParseDose(name);
        Assert.NotNull(p);
        Assert.Equal(expBase, p!.Value.Base);
        Assert.Equal(expDose, p.Value.Dose);
    }

    [Theory]
    [InlineData("Dragon dagger")]      // no dose suffix
    [InlineData("Potion(5)")]          // dose out of range
    public void ParseDose_returnsNullForNonDoseNames(string name)
    {
        Assert.Null(PotionFamilies.ParseDose(name));
    }

    [Fact]
    public void Group_buildsFamilyWithFourDosePlusLower()
    {
        var fams = PotionFamilies.Group(new[]
        {
            Map(1, "Prayer potion(4)"),
            Map(2, "Prayer potion(3)"),
            Map(3, "Prayer potion(2)"),
            Map(4, "Prayer potion(1)"),
        });

        var fam = Assert.Single(fams);
        Assert.Equal("Prayer potion", fam.BaseName);
        Assert.Equal(2000, fam.BuyLimit);
        Assert.Equal(4, fam.Variants.Count);
        Assert.Contains(fam.Variants, v => v.Dose == 4 && v.ItemId == 1);
    }

    [Fact]
    public void Group_dropsFamiliesWithoutA4DoseOrWithOnlyOneVariant()
    {
        var fams = PotionFamilies.Group(new[]
        {
            Map(1, "Half potion(3)"),     // no (4)
            Map(2, "Half potion(2)"),
            Map(3, "Loner(4)"),           // only one variant
            Map(4, "Cake(3)"),            // single, no (4)
        });
        Assert.Empty(fams);
    }
}
