using System.Security.AccessControl;
using System.Security.Principal;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// Sprint 20: column chooser, remaining checks, ACL PoC (DCSync logic only).
[Collection(ExportHookCollection.Name)]
public class Sprint20Tests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dh-s20-");
    private readonly ColumnLayoutStore _savedStore = TableViewModel.LayoutStore;
    public Sprint20Tests() => TableViewModel.LayoutStore = new ColumnLayoutStore(Path.Combine(_dir.FullName, "columns.json"));
    public void Dispose()
    {
        TableViewModel.LayoutStore = _savedStore;
        _dir.Delete(recursive: true);
    }

    // ── S20-1 column chooser ─────────────────────────────────────────────────
    private static TableViewModel<AdUser> Users() => new("Users", UserColumns.Grid);

    [Fact]
    public void HideColumn_IsRemembered_PerView()
    {
        var t = Users();
        t.SetColumnHidden("Email", true);
        Assert.True(t.IsColumnHidden("Email"));
        Assert.Equal("1 hidden", t.HiddenLabel);

        var again = Users();                                   // new instance, same view → remembered
        Assert.True(again.IsColumnHidden("Email"));
        Assert.False(new TableViewModel<AdComputer>("Computers", ViewColumns.Computers).IsColumnHidden("Email"));
    }

    [Fact]
    public void Reset_ShowsAllColumns()
    {
        var t = Users();
        t.SetColumnHidden("Email", true);
        t.SetColumnHidden("OU", true);
        t.ResetColumns();
        Assert.Equal(0, t.HiddenCount);
        Assert.Equal("All columns visible", t.HiddenLabel);
        Assert.False(Users().IsColumnHidden("Email"));
    }

    [Fact]
    public void LastVisibleColumn_CannotBeHidden_UnknownHeaderIgnored()
    {
        var t = new TableViewModel<AdDomainTrust>("Trusts", ViewColumns.Trusts);
        foreach (var c in ViewColumns.Trusts) t.SetColumnHidden(c.Header, true);
        Assert.Equal(ViewColumns.Trusts.Count - 1, t.HiddenCount);
        t.SetColumnHidden("No such column", true);
        Assert.Equal(ViewColumns.Trusts.Count - 1, t.HiddenCount);
    }

    [Fact]
    public void Export_VisibleColumnsOnly_OrAll()
    {
        var path = Path.Combine(_dir.FullName, "u.csv");
        var saved = TableViewModel.PickSavePath;
        try
        {
            TableViewModel.PickSavePath = _ => path;
            var t = Users();
            t.SetRows([new AdUser { SamAccountName = "a", Email = "a@x" }]);
            t.SetColumnHidden("Email", true);

            t.ExportVisibleOnly = true;
            t.ExportAllCommand.Execute(null);
            Assert.DoesNotContain("Email", File.ReadAllLines(path)[0]);
            Assert.Contains("columns: visible only", File.ReadAllText(ExportMetadata.MetaPathFor(path)));

            t.ExportVisibleOnly = false;
            t.ExportAllCommand.Execute(null);
            Assert.Contains("Email", File.ReadAllLines(path)[0]);
        }
        finally { TableViewModel.PickSavePath = saved; }
    }

    [Fact]
    public void CorruptLayoutFile_FallsBackToAllVisible()
    {
        var file = Path.Combine(_dir.FullName, "bad.json");
        File.WriteAllText(file, "{ nope");
        Assert.Empty(new ColumnLayoutStore(file).HiddenFor("Users"));
    }

    // ── S20-2 remaining checks ───────────────────────────────────────────────
    [Theory]
    [InlineData(null, false)] [InlineData("", false)] [InlineData("000000", false)]
    [InlineData("0000002", true)] [InlineData("0000001", false)] [InlineData("00000020010000001", true)]
    public void DsHeuristics_Anonymous(string? h, bool anon) => Assert.Equal(anon, CheckLogic.AnonymousLdapAllowed(h));

    [Fact]
    public void DsHeuristics_AdminSdExMask()
    {
        Assert.Empty(CheckLogic.AdminSdExMaskGroups("0000000001000000"));
        Assert.Equal(["Account Operators", "Server Operators"], CheckLogic.AdminSdExMaskGroups("0000000001000003"));
        Assert.Equal(4, CheckLogic.AdminSdExMaskGroups("000000000100000f").Count);
        Assert.Empty(CheckLogic.AdminSdExMaskGroups("00000"));
    }

    [Fact]
    public void LapsFilter_UsesOnlyGivenAttributes_NeverThePassword()
    {
        var f = CheckFilters.ComputersWithoutLaps(["msLAPS-PasswordExpirationTime"]);
        Assert.Contains("(!(msLAPS-PasswordExpirationTime=*))", f);
        Assert.DoesNotContain("ms-Mcs-AdmPwdExpirationTime", f);
        Assert.DoesNotContain("AdmPwd=", f);
        Assert.DoesNotContain("msLAPS-Password=", f);
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=8192))", f);   // DCs excluded
    }

    [Fact]
    public void Sprint20Checks_AreCatalogued()
    {
        foreach (var c in new[] { "LapsNotDeployed", "ComputersWithoutLaps", "MachinePasswordNotRotated", "DuplicateObjects",
                                  "AnonymousLdapAllowed", "AdminSdExMask", "SmartcardOldPassword" })
        {
            Assert.NotNull(FindingCatalog.Get(c));
            Assert.True(ScanGuard.QueryOf.ContainsKey(c), c);
        }
        Assert.True(ScoreModel.FormulaVersion >= 4);   // bumped again by later sprints (S23: 5)
    }

    // ── S20-3 ACL PoC (DCSync) ───────────────────────────────────────────────
    private const string Dom = "S-1-5-21-100-200-300";
    private static readonly IReadOnlySet<string> Defaults = AclLogic.DefaultDcSyncSids(Dom, null);
    private const string Attacker = "S-1-5-21-100-200-300-1337";

    private static ObjectAce Ext(string sid, Guid right, AceQualifier q = AceQualifier.AccessAllowed, AceFlags flags = AceFlags.None, Guid? inheritedType = null) =>
        new(flags, q, 0x100, new SecurityIdentifier(sid),
            ObjectAceFlags.ObjectAceTypePresent | (inheritedType is null ? 0 : ObjectAceFlags.InheritedObjectAceTypePresent),
            right, inheritedType ?? Guid.Empty, false, null);

    private static byte[] Sd(params GenericAce[] aces)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, aces.Length);
        for (int i = 0; i < aces.Length; i++) acl.InsertAce(i, aces[i]);
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative,
            new SecurityIdentifier("S-1-5-32-544"), new SecurityIdentifier("S-1-5-32-544"), null, acl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [Fact]
    public void DcSync_BothRights_IsFound()
    {
        var r = AclLogic.Evaluate(Sd(Ext(Attacker, AclLogic.ReplicationGetChanges), Ext(Attacker, AclLogic.ReplicationGetChangesAll)), Defaults);
        Assert.Equal(new AclLogic.DcSyncPrincipal(Attacker, AclLogic.DcSyncKind.CanDcSync), Assert.Single(r));
    }

    [Fact]
    public void DcSync_DefaultPrincipals_AreNotFlagged()
    {
        var r = AclLogic.Evaluate(Sd(
            Ext($"{Dom}-516", AclLogic.ReplicationGetChanges), Ext($"{Dom}-516", AclLogic.ReplicationGetChangesAll),
            Ext("S-1-5-9", AclLogic.ReplicationGetChanges), Ext("S-1-5-9", AclLogic.ReplicationGetChangesAll),
            Ext("S-1-5-32-544", AclLogic.ReplicationGetChanges), Ext("S-1-5-32-544", AclLogic.ReplicationGetChangesAll)), Defaults);
        Assert.Empty(r);
    }

    [Fact]
    public void DcSync_OnlyOneRight_IsNotDcSync()
    {
        Assert.Empty(AclLogic.Evaluate(Sd(Ext(Attacker, AclLogic.ReplicationGetChanges)), Defaults));
    }

    [Fact]
    public void DcSync_DenyAce_Wins()
    {
        var r = AclLogic.Evaluate(Sd(
            Ext(Attacker, AclLogic.ReplicationGetChangesAll, AceQualifier.AccessDenied),
            Ext(Attacker, AclLogic.ReplicationGetChanges), Ext(Attacker, AclLogic.ReplicationGetChangesAll)), Defaults);
        Assert.Empty(r);
    }

    [Fact]
    public void DcSync_InheritOnlyAce_DoesNotApplyToDomainObject()
    {
        var r = AclLogic.Evaluate(Sd(
            Ext(Attacker, AclLogic.ReplicationGetChanges, flags: AceFlags.InheritOnly | AceFlags.ContainerInherit),
            Ext(Attacker, AclLogic.ReplicationGetChangesAll, flags: AceFlags.InheritOnly | AceFlags.ContainerInherit)), Defaults);
        Assert.Empty(r);
    }

    [Fact]
    public void DcSync_ScopedToOtherClass_DoesNotApply_ButDomainDnsDoes()
    {
        var userClass = new Guid("bf967aba-0de6-11d0-a285-00aa003049e2");
        Assert.Empty(AclLogic.Evaluate(Sd(
            Ext(Attacker, AclLogic.ReplicationGetChanges, inheritedType: userClass),
            Ext(Attacker, AclLogic.ReplicationGetChangesAll, inheritedType: userClass)), Defaults));
        Assert.Single(AclLogic.Evaluate(Sd(
            Ext(Attacker, AclLogic.ReplicationGetChanges, inheritedType: AclLogic.DomainDnsClass),
            Ext(Attacker, AclLogic.ReplicationGetChangesAll, inheritedType: AclLogic.DomainDnsClass)), Defaults));
    }

    [Fact]
    public void DcSync_AllExtendedRights_WithoutObjectType_Counts()
    {
        var allExtended = new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x100, new SecurityIdentifier(Attacker), false, null);
        Assert.Equal(AclLogic.DcSyncKind.CanDcSync, Assert.Single(AclLogic.Evaluate(Sd(allExtended), Defaults)).Kind);
    }

    [Fact]
    public void GenericAll_IsDcSync_WriteDacl_IsCanGrantItself()
    {
        var full = new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x10000000, new SecurityIdentifier(Attacker), false, null);
        Assert.Equal(AclLogic.DcSyncKind.CanDcSync, Assert.Single(AclLogic.Evaluate(Sd(full), Defaults)).Kind);

        var writeDacl = new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x40000, new SecurityIdentifier(Attacker), false, null);
        Assert.Equal(AclLogic.DcSyncKind.CanGrantItself, Assert.Single(AclLogic.Evaluate(Sd(writeDacl), Defaults)).Kind);
    }

    [Fact]
    public void DefaultSids_UseForestRootForEnterpriseGroups()
    {
        var d = AclLogic.DefaultDcSyncSids(Dom, "S-1-5-21-9-9-9");
        Assert.Contains("S-1-5-21-9-9-9-519", d);
        Assert.Contains("S-1-5-21-9-9-9-498", d);
        Assert.Contains($"{Dom}-516", d);
        Assert.DoesNotContain($"{Dom}-519", d);
    }
}
