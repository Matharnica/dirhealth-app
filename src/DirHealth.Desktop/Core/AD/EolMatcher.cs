namespace DirHealth.Desktop.Core.AD;

// End-of-life OS matching — pure, so the ordering rule (e.g. "2012 R2" before "2012") is unit-testable.
public static class EolMatcher
{
    // ORDER MATTERS: more specific strings ("Server 2012 R2", "Windows 8.1") must come before their
    // shorter prefixes ("Server 2012", "Windows 8"), because matching is a first-hit Contains scan.
    private static readonly (string Substring, DateTime EolDate)[] EolTable =
    [
        ("Windows XP",       new DateTime(2014,  4,  8)),
        ("Windows Vista",    new DateTime(2017,  4, 11)),
        ("Windows 8.1",      new DateTime(2023,  1, 10)),
        ("Windows 8",        new DateTime(2016,  1, 12)),
        ("Windows 7",        new DateTime(2020,  1, 14)),
        ("Server 2003",      new DateTime(2015,  7, 14)),
        ("Server 2008 R2",   new DateTime(2020,  1, 14)),
        ("Server 2008",      new DateTime(2020,  1, 14)),
        ("Server 2012 R2",   new DateTime(2023, 10, 10)),
        ("Server 2012",      new DateTime(2023, 10, 10)),
    ];

    public static bool TryGetEolDate(string os, out DateTime eolDate)
    {
        eolDate = default;
        if (string.IsNullOrEmpty(os)) return false;
        foreach (var (sub, date) in EolTable)
        {
            if (os.Contains(sub, StringComparison.OrdinalIgnoreCase))
            {
                eolDate = date;
                return true;
            }
        }
        return false;
    }
}
