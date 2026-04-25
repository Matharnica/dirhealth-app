using System.DirectoryServices;

namespace DirHealth.Desktop.Core.AD;

public class AdConnector
{
    public string? Domain     { get; set; }
    public string? Username   { get; set; }
    public string? Password   { get; set; }

    public DirectoryEntry GetRootEntry()
    {
        if (!string.IsNullOrEmpty(Domain))
        {
            var path = $"LDAP://{Domain}";
            return string.IsNullOrEmpty(Username)
                ? new DirectoryEntry(path)
                : new DirectoryEntry(path, Username, Password);
        }

        using var rootDse = new DirectoryEntry("LDAP://RootDSE");
        var nc = rootDse.Properties["defaultNamingContext"].Value?.ToString()
            ?? throw new InvalidOperationException("Cannot resolve AD domain root (defaultNamingContext empty).");
        return string.IsNullOrEmpty(Username)
            ? new DirectoryEntry($"LDAP://{nc}")
            : new DirectoryEntry($"LDAP://{nc}", Username, Password);
    }

    public DirectorySearcher CreateSearcher(DirectoryEntry root, string filter, params string[] properties)
    {
        var searcher = new DirectorySearcher(root)
        {
            Filter    = filter,
            PageSize  = 1000,
            SizeLimit = 0
        };
        if (properties.Length > 0)
            searcher.PropertiesToLoad.AddRange(properties);
        return searcher;
    }

    public DirectoryEntry GetEntry(string ldapPath) =>
        string.IsNullOrEmpty(Username)
            ? new DirectoryEntry(ldapPath)
            : new DirectoryEntry(ldapPath, Username, Password);

    public bool TestConnection()
    {
        try
        {
            using var entry = GetRootEntry();
            _ = entry.NativeObject;
            return true;
        }
        catch { return false; }
    }

    public bool IsDomainAdmin()
    {
        try
        {
            using var root = GetRootEntry();

            // Resolve the current user's sAMAccountName
            string samName;
            if (!string.IsNullOrEmpty(Username))
            {
                samName = Username.Contains('\\') ? Username.Split('\\')[1] : Username;
            }
            else
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                samName = identity.Name.Contains('\\') ? identity.Name.Split('\\')[1] : identity.Name;
            }

            // Find Domain Admins DN
            using var groupSearcher = CreateSearcher(root,
                "(&(objectClass=group)(cn=Domain Admins))", "distinguishedName");
            var groupResult = groupSearcher.FindOne();
            if (groupResult == null) return false;
            var groupDn = groupResult.Properties["distinguishedName"][0]?.ToString() ?? "";

            // Check membership (LDAP_MATCHING_RULE_IN_CHAIN handles nested groups)
            using var memberSearcher = CreateSearcher(root,
                $"(&(objectClass=user)(sAMAccountName={samName})" +
                $"(memberOf:1.2.840.113556.1.4.1941:={EscapeDn(groupDn)}))",
                "sAMAccountName");
            return memberSearcher.FindOne() != null;
        }
        catch { return false; }
    }

    private static string EscapeDn(string dn) =>
        dn.Replace("\\", "\\5c").Replace("(", "\\28").Replace(")", "\\29");
}
