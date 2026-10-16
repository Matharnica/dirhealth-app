namespace DirHealth.Desktop.Core.AD.Models;

/// <summary>Kind of object a finding points at (S16-1).</summary>
public enum AffectedObjectType { Unknown, User, Computer, Group, PasswordPolicy, DomainSetting }

/// <summary>Exception state of an affected object (ADR 0001).</summary>
public enum ExceptionState { None, Active, Expired }

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
    /// <summary>Weighs 3× in the EOL score (ADR 0002).</summary>
    public bool               IsDomainController { get; set; }

    /// <summary>Exception state for display — runtime only, not persisted (ADR 0001).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public ExceptionState Exception { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string ExceptionLabel { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsExcepted => Exception == ExceptionState.Active;

    public static AffectedObject FromUser(AdUser u) =>
        new() { Name = u.SamAccountName, DistinguishedName = u.DistinguishedName, Sid = u.Sid, Type = AffectedObjectType.User };

    public static AffectedObject FromComputer(AdComputer c, string detail = "") =>
        new() { Name = c.Name, DistinguishedName = c.DistinguishedName, Sid = c.Sid, Type = AffectedObjectType.Computer,
                Detail = detail, IsDomainController = c.IsDomainController };

    public static AffectedObject FromGroup(AdGroup g) =>
        new() { Name = g.Name, DistinguishedName = g.DistinguishedName, Sid = g.Sid, Type = AffectedObjectType.Group };
}
