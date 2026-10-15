using System.Security.AccessControl;
using System.Security.Principal;

namespace DirHealth.Desktop.Core.AD.Checks;

/// <summary>
/// PoC (S20-3, ADR 0004 — status Proposed): who can DCSync, evaluated from the domain object's binary
/// security descriptor. Pure — no LDAP, not wired into the scan, not in the UI or the score. It proves the
/// evaluation logic offline; runtime and false-positive rate need a live domain (measurement plan in the ADR).
///
/// Rules: principals are compared by SID, never by name (localized names). Allow ACEs grant, deny ACEs take
/// away; inherit-only ACEs and ACEs scoped to another object class do not apply to the domain object.
/// ControlAccess without an object type means "all extended rights". GenericAll grants DCSync directly;
/// WriteDacl/WriteOwner let a principal grant itself DCSync — reported separately.
/// </summary>
public static class AclLogic
{
    public static readonly Guid ReplicationGetChanges    = new("1131f6aa-9c07-11d1-f79f-00c04fc2dcd2");
    public static readonly Guid ReplicationGetChangesAll = new("1131f6ad-9c07-11d1-f79f-00c04fc2dcd2");
    /// <summary>schemaIDGUID of the domainDNS class — inheritable ACEs scoped to it do apply to the domain head.</summary>
    public static readonly Guid DomainDnsClass           = new("19195a5b-6da0-11d0-afd3-00c04fd930c9");

    private const int ControlAccess = 0x100;
    private const int WriteDacl     = 0x40000;
    private const int WriteOwner    = 0x80000;
    private const int GenericAll    = 0x10000000;
    private const int FullControl   = 0xF01FF;

    public enum DcSyncKind { CanDcSync, CanGrantItself }

    public record DcSyncPrincipal(string Sid, DcSyncKind Kind);

    /// <summary>SIDs that hold replication rights in every domain by design.</summary>
    public static HashSet<string> DefaultDcSyncSids(string domainSid, string? forestRootSid)
    {
        var forest = forestRootSid ?? domainSid;
        return new(StringComparer.OrdinalIgnoreCase)
        {
            "S-1-5-18",                       // SYSTEM
            "S-1-5-9",                        // Enterprise Domain Controllers
            "S-1-5-32-544",                   // Administrators
            $"{domainSid}-512",               // Domain Admins
            $"{domainSid}-516",               // Domain Controllers
            $"{domainSid}-521",               // Read-only Domain Controllers
            $"{forest}-498",                  // Enterprise Read-only Domain Controllers
            $"{forest}-519",                  // Enterprise Admins
        };
    }

    private sealed class Rights { public bool Changes, ChangesAll, Full, GrantSelf, DenyChanges, DenyChangesAll; }

    public static List<DcSyncPrincipal> Evaluate(byte[] securityDescriptor, IReadOnlySet<string> defaultSids)
    {
        var sd   = new RawSecurityDescriptor(securityDescriptor, 0);
        var by   = new Dictionary<string, Rights>(StringComparer.OrdinalIgnoreCase);
        // No DACL at all = everyone has full control
        if (sd.DiscretionaryAcl is null)
            return [new DcSyncPrincipal("S-1-1-0", DcSyncKind.CanDcSync)];

        foreach (var ace in sd.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce known) continue;
            if ((ace.AceFlags & AceFlags.InheritOnly) != 0) continue;
            Guid? objectType = null;
            if (ace is ObjectAce oa)
            {
                if ((oa.ObjectAceFlags & ObjectAceFlags.InheritedObjectAceTypePresent) != 0 && oa.InheritedObjectAceType != DomainDnsClass)
                    continue;
                if ((oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0) objectType = oa.ObjectAceType;
            }
            var sid = known.SecurityIdentifier.Value;
            var r   = by.TryGetValue(sid, out var x) ? x : by[sid] = new Rights();
            int mask = known.AccessMask;
            bool allow = known.AceQualifier == AceQualifier.AccessAllowed;
            bool deny  = known.AceQualifier == AceQualifier.AccessDenied;
            bool ext   = (mask & ControlAccess) != 0;
            bool hitsChanges    = ext && (objectType is null || objectType == ReplicationGetChanges);
            bool hitsChangesAll = ext && (objectType is null || objectType == ReplicationGetChangesAll);

            if (allow)
            {
                if ((mask & GenericAll) != 0 || (mask & FullControl) == FullControl && objectType is null) r.Full = true;
                if (hitsChanges)    r.Changes = true;
                if (hitsChangesAll) r.ChangesAll = true;
                if ((mask & (WriteDacl | WriteOwner)) != 0 && objectType is null) r.GrantSelf = true;
            }
            else if (deny)
            {
                if (hitsChanges)    r.DenyChanges = true;
                if (hitsChangesAll) r.DenyChangesAll = true;
            }
        }

        var result = new List<DcSyncPrincipal>();
        foreach (var (sid, r) in by)
        {
            if (defaultSids.Contains(sid)) continue;
            bool canSync = r.Full && !(r.DenyChanges || r.DenyChangesAll)
                           || r.Changes && r.ChangesAll && !r.DenyChanges && !r.DenyChangesAll;
            if (canSync)          result.Add(new DcSyncPrincipal(sid, DcSyncKind.CanDcSync));
            else if (r.GrantSelf) result.Add(new DcSyncPrincipal(sid, DcSyncKind.CanGrantItself));
        }
        return result.OrderBy(p => p.Kind).ThenBy(p => p.Sid, StringComparer.Ordinal).ToList();
    }
}
