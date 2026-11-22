using CampusManager.App.Services;

namespace CampusManager.Tests;

public class CategoryColorTests
{
    [Fact]
    public void EachCategoryHasItsOwnColor()
    {
        var colors = Fmt.Categories.Select(Fmt.CategoryColor).ToList();
        Assert.Equal(colors.Count, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void UnknownCategoryGetsNeutralColor() =>
        Assert.Equal(Fmt.CategoryColor("Personnel"), Fmt.CategoryColor("Dossier inventé"));

    [Fact]
    public void LooksLikeTdOrTpGetTheirColorWhateverTheCase() =>
        Assert.Equal(Fmt.CategoryColor("TD"), Fmt.CategoryColor("td"));
}
