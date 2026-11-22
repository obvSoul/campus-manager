using System.Globalization;

namespace CampusManager.App.Services;

/// <summary>
/// Tri « naturel », comme l'Explorateur Windows : td1, td2… td9, td10 (et non td1, td10, td2).
/// Les nombres sont comparés comme des nombres, le texte sans tenir compte de la casse ni des accents.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();
    static readonly CompareInfo Fr = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
    const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            var da = char.IsAsciiDigit(a[i]);
            var db = char.IsAsciiDigit(b[j]);
            var ea = Chunk(a, i, da);
            var eb = Chunk(b, j, db);
            int c;
            if (da && db)
            {
                // Nombres : sans les zéros de tête, le plus long est le plus grand ; sinon chiffre par chiffre.
                var na = a.AsSpan(i, ea - i).TrimStart('0');
                var nb = b.AsSpan(j, eb - j).TrimStart('0');
                c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : na.SequenceCompareTo(nb);
                if (c == 0) c = (ea - i).CompareTo(eb - j); // « 9 » avant « 09 »
            }
            else c = Fr.Compare(a, i, ea - i, b, j, eb - j, Loose);
            if (c != 0) return c;
            i = ea; j = eb;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    static int Chunk(string s, int start, bool digits)
    {
        var k = start;
        while (k < s.Length && char.IsAsciiDigit(s[k]) == digits) k++;
        return k;
    }
}
