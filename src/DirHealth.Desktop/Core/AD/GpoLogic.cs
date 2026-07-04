using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.AD;

// Pure, AD-independent helpers for the GPO Browser (S10-1) — kept separate so they are unit-testable.
public static class GpoLogic
{
    // Matches a brace-wrapped GUID as stored in a GPO cn and referenced inside a gPLink value.
    private static readonly Regex GuidRegex =
        new(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}",
            RegexOptions.Compiled);

    // Extract every brace-wrapped GPO GUID referenced by a gPLink value.
    // gPLink form: "[LDAP://cn={GUID},cn=policies,cn=system,DC=...;0][LDAP://cn={GUID2},...;2]"
    public static IEnumerable<string> ExtractGuids(string? gpLink)
    {
        if (string.IsNullOrEmpty(gpLink)) yield break;
        foreach (Match m in GuidRegex.Matches(gpLink))
            yield return m.Value;
    }

    // flags bit0 = user settings disabled, bit1 = computer settings disabled; 3 = fully disabled GPO.
    public static string Classify(int flags, int linkCount) =>
        flags == 3   ? "Disabled"
        : linkCount == 0 ? "Orphaned"
        :                  "Active";
}
