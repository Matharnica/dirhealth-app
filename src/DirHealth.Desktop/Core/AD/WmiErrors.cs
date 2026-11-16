using System.Management;

namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// Turns WMI/DCOM exceptions into a short message for the detail view (fix H1, 2026-11-16).
/// Kerberos-only connections fail more often than negotiated ones — the admin must see why,
/// not an empty list. Never a stack trace; the code is kept for searching.
/// </summary>
public static class WmiErrors
{
    public static string Describe(Exception ex)
    {
        uint code = ex is ManagementException me && me.ErrorCode != 0 ? (uint)me.ErrorCode : (uint)ex.HResult;
        var text = code switch
        {
            0x80070005 or 0x80041003 =>
                "Access denied — your Windows account has no WMI rights on this computer.",
            0x800706BA =>
                "RPC server unavailable — the computer is unreachable or a firewall blocks WMI (port 135 + dynamic RPC).",
            0x80070721 or 0x80090322 or 0x8009030C or 0x80090311 or 0x8009030E or 0x800706D3 =>
                "Kerberos authentication failed — check the computer's SPN, its DNS name and that your session has a Kerberos ticket. NTLM is not used.",
            0x800705B4 or 0x80041032 =>
                "WMI did not answer in time.",
            0x8004100E =>
                "WMI namespace root\\cimv2 not available on this computer.",
            _ => null,
        };
        var first = (ex.Message ?? "").Split('\n')[0].Trim();
        return text is not null
            ? $"{text} (0x{code:X8})"
            : $"WMI query failed: {first} (0x{code:X8})";
    }
}
