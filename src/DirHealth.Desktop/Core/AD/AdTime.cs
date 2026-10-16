namespace DirHealth.Desktop.Core.AD;

/// <summary>AD time conversions — pure, testable (S14-4).</summary>
public static class AdTime
{
    /// <summary>accountExpires: 0 and Int64.MaxValue both mean "never". DateTime.FromFileTimeUtc
    /// throws on Int64.MaxValue, so this must not go through the generic FILETIME path.</summary>
    public static DateTime? FromAccountExpires(long value)
    {
        if (value <= 0 || value == long.MaxValue) return null;
        try { return DateTime.FromFileTimeUtc(value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Whole days between <paramref name="utc"/> and now; null when unknown.</summary>
    public static int? DaysSince(DateTime? utc, DateTime nowUtc) =>
        utc is { } d ? (int)Math.Floor((nowUtc - d).TotalDays) : null;

    /// <summary>Delegation kind from userAccountControl and msDS-AllowedToDelegateTo.</summary>
    public static string DelegationKind(int uac, bool hasAllowedToDelegateTo)
    {
        if (UacFlags.TrustedForUnconstrainedDelegation(uac)) return "Unconstrained";
        if (hasAllowedToDelegateTo)
            return UacFlags.Has(uac, UacFlags.TrustedToAuthForDelegation) ? "Constrained (any protocol)" : "Constrained";
        return "";
    }
}
