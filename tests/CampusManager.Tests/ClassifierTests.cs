using CampusManager.Core.Classify;

namespace CampusManager.Tests;

public class ClassifierTests
{
    static string Cat(ClassifyInput i) => Classifier.Classify(i).Category;

    [Fact] public void AssignActivity_IsDevoirs() =>
        Assert.Equal("Devoirs", Cat(new(Modname: "assign", Title: "Rendu du TD#3")));

    [Fact] public void UsesSection_NotOnlyFilename() =>
        Assert.Equal("TD", Cat(new(Modname: "resource", Filename: "feuille3.pdf", Section: "Travaux dirigés")));

    [Fact] public void TdCorrection_IsCorrection() =>
        Assert.Equal("Corrections", Cat(new(Title: "Correction du TD 2", Filename: "td2_corrige.pdf")));

    [Fact]
    public void TpAndCours()
    {
        Assert.Equal("TP", Cat(new(Title: "TP 4 - Pointeurs")));
        Assert.Equal("Cours", Cat(new(Title: "Cours magistral chapitre 2", Filename: "CM2.pdf")));
    }

    [Fact]
    public void NoHint_IsUnsorted_WithZeroConfidence()
    {
        var r = Classifier.Classify(new(Title: "Document", Filename: "scan0001.pdf"));
        Assert.Equal(Classifier.Unsorted, r.Category);
        Assert.Equal(0, r.Confidence);
    }

    [Fact] public void ContradictoryHints_AreUnsorted() =>
        Assert.Equal(Classifier.Unsorted, Cat(new(Title: "TD", Filename: "TP.pdf")));

    [Fact] public void TitleWinsOverSection() =>
        Assert.Equal("TD", Cat(new(Title: "TD 3", Section: "TP")));
}
