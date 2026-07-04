using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Kerberos / delegation attack-surface queries (S12-3 split).
// Shared helpers (QueryUsers, QueryComputers) live in AdScanner.cs.
public partial class AdScanner
{
    public async Task<List<AdUser>> GetAsRepRoastableAccountsAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 4194304 = DONT_REQUIRE_PREAUTH
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=4194304))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdComputer>> GetUnconstrainedDelegationComputersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION; exclude DCs (bit 8192)
            var filter = "(&(objectClass=computer)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=8192))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=524288))";
            return QueryComputers(filter);
        });
    }

    public async Task<List<AdUser>> GetUnconstrainedDelegationUsersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=524288))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetPasswordNotRequiredAccountsAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 32 = PASSWD_NOTREQD
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=32))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetKerberoastableAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(servicePrincipalName=*)(!(samAccountName=krbtgt)))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetAdminSdHolderAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(adminCount=1))";
            return QueryUsers(filter);
        });
    }
}
