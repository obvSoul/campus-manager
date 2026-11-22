using CampusManager.App.Services;

namespace CampusManager.Tests;

public class NaturalSortTests
{
    [Fact]
    public void NumbersAreComparedAsNumbers_LikeWindowsExplorer()
    {
        string[] names = ["r101_td10_fonctions.pdf", "r101_td2_conditions.pdf", "r101_td09_conditionsBoucles.pdf", "r101_td1_variables.pdf",
            "r101_td0.pdf", "r101_td12_dictionnaires.pdf", "r101_td8_installation.pdf", "r101_td11_listes.pdf"];
        Assert.Equal(["r101_td0.pdf", "r101_td1_variables.pdf", "r101_td2_conditions.pdf", "r101_td8_installation.pdf",
            "r101_td09_conditionsBoucles.pdf", "r101_td10_fonctions.pdf", "r101_td11_listes.pdf", "r101_td12_dictionnaires.pdf"],
            names.OrderBy(n => n, NaturalComparer.Instance));
    }

    [Fact]
    public void IgnoresCaseAndAccents_AndKeepsTextOrder()
    {
        Assert.True(NaturalComparer.Instance.Compare("Énoncé 2", "enonce 10") < 0);
        Assert.True(NaturalComparer.Instance.Compare("cours", "TD") < 0);
        Assert.Equal(0, NaturalComparer.Instance.Compare("TD3", "td3"));
    }

    [Fact]
    public void HugeNumbersDoNotOverflow() =>
        Assert.True(NaturalComparer.Instance.Compare("a99999999999999999999999", "a100000000000000000000000") < 0);
}
