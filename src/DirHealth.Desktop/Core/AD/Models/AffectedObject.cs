namespace DirHealth.Desktop.Core.AD.Models;

/// <summary>Kind of object a finding points at (S16-1).</summary>
public enum AffectedObjectType { Unknown, User, Computer, Group, PasswordPolicy, DomainSetting }

/// <summary>
/// One object behind a finding (S16-1). Structured so the finding card can list, export and
/// open it. Findings cached before S16 only stored a display string; those load with
/// Name = that string and an empty DN (it was usually a sAMAccountName, not a DN).
/// </summary>
public class AffectedObject
{
    public string             Name              { get; set; } = "";
    public string             DistinguishedName { get; set; } = "";
    public string             Sid               { get; set; } = "";
    public AffectedObjectType Type              { get; set; }
    /// <summary>Extra context shown next to the name, e.g. the OS of an EOL computer.</summary>
    public string             Detail            { get; set; } = "";

    public static AffectedObject FromUser(AdUser u) =>
        new() { Name = u.SamAccountName, DistinguishedName = u.DistinguishedName, Sid = u.Sid, Type = AffectedObjectType.User };

    public static AffectedObject FromComputer(AdComputer c, string detail = "") =>
        new() { Name = c.Name, DistinguishedName = c.DistinguishedName, Type = AffectedObjectType.Computer, Detail = detail };

    public static AffectedObject FromGroup(AdGroup g) =>
        new() { Name = g.Name, DistinguishedName = g.DistinguishedName, Type = AffectedObjectType.Group };
}
