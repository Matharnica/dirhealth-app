using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// Builds the WMI connection authority (fix H1, 2026-11-16). Pure — no network, no AD.
///
/// Why: without an authority, DCOM negotiates and silently falls back to NTLM. A short name that
/// DNS cannot resolve is then answered by any LLMNR/NetBIOS responder in the subnet, which
/// receives an NTLM logon of the admin running DirHealth (relay/cracking). We therefore connect
/// only to the FQDN from dNSHostName and name the Kerberos server principal explicitly.
///
/// Format per Microsoft: ConnectionOptions.Authority "Kerberos:&lt;principal name&gt;"; the WMI
/// sample "Specifying the Server Principal Name" uses <c>kerberos:MyDomain\MyMachine</c>.
/// </summary>
public static partial class WmiAuthority
{
    // RFC 1123 labels, at least two labels (an FQDN, not a short name), no trailing dot.
    [GeneratedRegex(@"^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?$")]
    private static partial Regex FqdnRegex();

    // NetBIOS or DNS domain name; no backslash, slash, colon or blanks.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-\.]{0,63}$")]
    private static partial Regex DomainRegex();

    /// <summary>True for a fully qualified host name (e.g. "pc1.corp.example.com").</summary>
    public static bool IsValidFqdn(string? fqdn) =>
        !string.IsNullOrWhiteSpace(fqdn) && FqdnRegex().IsMatch(fqdn);

    /// <summary>First DNS label of a valid FQDN ("pc1" for "pc1.corp.example.com").</summary>
    public static string HostLabel(string fqdn)
    {
        if (!IsValidFqdn(fqdn)) throw new ArgumentException("Not a valid FQDN.", nameof(fqdn));
        return fqdn[..fqdn.IndexOf('.')];
    }

    /// <summary>
    /// "kerberos:&lt;DOMAIN&gt;\&lt;HOST&gt;" — the server principal is the computer account of the
    /// target in the given domain. Throws on an invalid host or domain; never returns a value
    /// that would let DCOM negotiate (and thus fall back to NTLM).
    /// </summary>
    public static string Build(string fqdn, string domain)
    {
        if (!IsValidFqdn(fqdn)) throw new ArgumentException("Not a valid FQDN.", nameof(fqdn));
        if (string.IsNullOrWhiteSpace(domain) || !DomainRegex().IsMatch(domain))
            throw new ArgumentException("Not a valid domain name.", nameof(domain));
        return $@"kerberos:{domain}\{HostLabel(fqdn)}";
    }
}
