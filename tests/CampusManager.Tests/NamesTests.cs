using CampusManager.Core.Library;

namespace CampusManager.Tests;

public class NamesTests
{
    [Fact]
    public void Sanitize_ForbiddenCharsAndReservedNames()
    {
        Assert.Equal("TD1_ _a_b__.pdf", Names.Sanitize("TD1: \"a/b\"?.pdf"));
        Assert.Equal("_con.txt", Names.Sanitize("con.txt"));
        Assert.Equal("fin", Names.Sanitize("fin."));
        Assert.True(Names.Sanitize(new string('a', 300) + ".pdf").Length <= 120);
    }

    [Fact]
    public void UniquePath_Adds2Then3()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("a.pdf"), "x");
        File.WriteAllText(t.Combine("a (2).pdf"), "x");
        Assert.Equal(t.Combine("a (3).pdf"), Names.UniquePath(t.Path, "a.pdf"));
    }
}
