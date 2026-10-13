using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.ViewModels.Tables;

/// <summary>
/// Column definitions for every shared table except users (S14-2, S14-3). One place per
/// view: the grid and the CSV export both read from here.
/// </summary>
public static class ViewColumns
{
    private const string LastLogon = UserColumns.LastLogonHeader;

    public static IReadOnlyList<TableColumn<AdComputer>> Computers { get; } =
    [
        new("Name",              c => c.Name,            nameof(AdComputer.Name), monospace: true),
        new("Operating System",  c => c.OperatingSystem, nameof(AdComputer.OperatingSystem)),
        new("OS Version",        c => c.OsVersion,       nameof(AdComputer.OsVersion)),
        new("Enabled",           c => c.IsEnabled,       nameof(AdComputer.IsEnabled), yesNo: true),
        new("Domain Controller", c => c.IsDomainController, nameof(AdComputer.IsDomainController), yesNo: true),
        new("End of Life",       c => c.IsEol,           nameof(AdComputer.IsEol), yesNo: true),
        new(LastLogon,           c => c.LastLogon,       nameof(AdComputer.LastLogon), "yyyy-MM-dd", monospace: true),
        new("OU",                c => c.Ou,              nameof(AdComputer.Ou)),
    ];

    public static IReadOnlyList<TableColumn<AdUser>> PasswordReport { get; } =
    [
        new("Display Name",      u => u.DisplayName,     nameof(AdUser.DisplayName)),
        new("sAMAccountName",    u => u.SamAccountName,  nameof(AdUser.SamAccountName), monospace: true),
        new("Email",             u => u.Email,           nameof(AdUser.Email)),
        new("Expires In (d)",    u => u.DaysUntilPasswordExpiry, nameof(AdUser.DaysUntilPasswordExpiry),
            "0' days'", exportFormat: "0", foregroundConverterKey: "DaysToExpiryColor"),
        new("Password Expires",  u => u.PasswordExpires, nameof(AdUser.PasswordExpires), "yyyy-MM-dd", monospace: true),
        new("Password Last Set", u => u.PasswordLastSet, nameof(AdUser.PasswordLastSet), "yyyy-MM-dd", monospace: true),
        new("OU",                u => u.Ou,              nameof(AdUser.Ou)),
    ];

    public static IReadOnlyList<TableColumn<AdGroupMember>> GroupMembers { get; } =
    [
        new("Name",              m => m.Name,              nameof(AdGroupMember.Name)),
        new("sAMAccountName",    m => m.SamAccountName,    nameof(AdGroupMember.SamAccountName), monospace: true),
        new("Type",              m => m.ObjectType,        nameof(AdGroupMember.ObjectType)),
        new("Distinguished Name",m => m.DistinguishedName, nameof(AdGroupMember.DistinguishedName), monospace: true),
    ];

    public static IReadOnlyList<TableColumn<AdGroup>> Groups { get; } =
    [
        new("Name",              g => g.Name,        nameof(AdGroup.Name)),
        new("Scope",             g => g.GroupScope,  nameof(AdGroup.GroupScope)),
        new("Members",           g => g.MemberCount, nameof(AdGroup.MemberCount)),
        new("Description",       g => g.Description, nameof(AdGroup.Description)),
        new("Distinguished Name",g => g.DistinguishedName),
    ];

    public static IReadOnlyList<TableColumn<PrivilegedMembershipRow>> PrivilegedMemberships { get; } =
    [
        new("Group",  r => r.Group,  nameof(PrivilegedMembershipRow.Group)),
        new("Member", r => r.Member, nameof(PrivilegedMembershipRow.Member)),
        new("Status", r => r.Status, nameof(PrivilegedMembershipRow.Status)),
        new("Risk",   r => r.Risk,   nameof(PrivilegedMembershipRow.Risk)),
    ];

    public static IReadOnlyList<TableColumn<AdDomainController>> DomainControllers { get; } =
    [
        new("Name",              d => d.Name,            nameof(AdDomainController.Name), monospace: true),
        new("Operating System",  d => d.OperatingSystem, nameof(AdDomainController.OperatingSystem)),
        new("OS Version",        d => d.OsVersion,       nameof(AdDomainController.OsVersion)),
        new("Global Catalog",    d => d.IsGlobalCatalog, nameof(AdDomainController.IsGlobalCatalog), yesNo: true),
        new("FSMO Roles",        d => d.FsmoRolesText,   nameof(AdDomainController.FsmoRolesText)),
        new(LastLogon,           d => d.LastLogon,       nameof(AdDomainController.LastLogon), "yyyy-MM-dd", monospace: true),
        new("End of Life",       d => d.IsEol,           nameof(AdDomainController.IsEol), yesNo: true),
        new("EOL Date",          d => d.EolDate,         nameof(AdDomainController.EolDate), "yyyy-MM-dd"),
        new("Distinguished Name",d => d.DistinguishedName),
    ];

    public static IReadOnlyList<TableColumn<AdDomainTrust>> Trusts { get; } =
    [
        new("Name",         t => t.Name,          nameof(AdDomainTrust.Name)),
        new("Type",         t => t.TrustType,     nameof(AdDomainTrust.TrustType)),
        new("Direction",    t => t.Direction,     nameof(AdDomainTrust.Direction)),
        new("Forest Trust", t => t.IsForestTrust, nameof(AdDomainTrust.IsForestTrust), yesNo: true),
    ];

    public static IReadOnlyList<TableColumn<AdGpo>> Gpos { get; } =
    [
        new("Display Name", g => g.DisplayName, nameof(AdGpo.DisplayName)),
        new("Status",       g => g.Status,      nameof(AdGpo.Status)),
        new("Links",        g => g.LinkCount,   nameof(AdGpo.LinkCount)),
        new("Created",      g => g.WhenCreated, nameof(AdGpo.WhenCreated), "yyyy-MM-dd", monospace: true),
        new("Changed",      g => g.WhenChanged, nameof(AdGpo.WhenChanged), "yyyy-MM-dd", monospace: true),
        new("GUID",         g => g.Guid,        nameof(AdGpo.Guid), monospace: true),
    ];

    public static IReadOnlyList<TableColumn<AdOU>> OrganizationalUnits { get; } =
    [
        new("Name",               o => o.Name,              nameof(AdOU.Name)),
        new("Description",        o => o.Description,       nameof(AdOU.Description)),
        new("Distinguished Name", o => o.DistinguishedName, nameof(AdOU.DistinguishedName), monospace: true),
    ];

    public static IReadOnlyList<TableColumn<AdRecentChange>> RecentChanges { get; } =
    [
        new("Type",            c => c.ObjectType,     nameof(AdRecentChange.ObjectType)),
        new("Name",            c => c.Name,           nameof(AdRecentChange.Name)),
        new("Action",          c => c.Action,         nameof(AdRecentChange.Action)),
        new("Changed (local)", c => c.ChangedAtLocal, nameof(AdRecentChange.ChangedAtLocal), "yyyy-MM-dd HH:mm", monospace: true),
        new("Distinguished Name", c => c.DistinguishedName),
    ];

    public static IReadOnlyList<TableColumn<AdSearchResult>> SearchResults { get; } =
    [
        new("Type",           r => r.TypeLabel,      nameof(AdSearchResult.TypeIcon), width: 52,
            toolTipPath: nameof(AdSearchResult.TypeLabel)),
        new("Name",           r => r.Name,           nameof(AdSearchResult.Name)),
        new("Display Name",   r => r.DisplayName,    nameof(AdSearchResult.DisplayName)),
        new("sAMAccountName", r => r.SamAccountName, nameof(AdSearchResult.SamAccountName), monospace: true),
        new("Email",          r => r.Email,          nameof(AdSearchResult.Email)),
        new("Enabled",        r => r.IsEnabled,      nameof(AdSearchResult.IsEnabled), yesNo: true),
        new("OU",             r => r.OU,             nameof(AdSearchResult.OU)),
        new("SID",            r => r.Sid),
        new("Distinguished Name", r => r.DistinguishedName),
    ];

    /// <summary>User detail "Member Of" and the dossier groups CSV (S15-4/S15-5).</summary>
    public static IReadOnlyList<TableColumn<AdUserGroup>> UserGroups => UserDossierExporter.GroupColumns;

    /// <summary>Dossier findings CSV (S15-5).</summary>
    public static IReadOnlyList<TableColumn<AdFinding>> UserFindings => UserDossierExporter.FindingColumns;
}
