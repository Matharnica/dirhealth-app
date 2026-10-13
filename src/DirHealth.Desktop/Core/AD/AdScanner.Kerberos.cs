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
            var filter = CheckFilters.AsRepRoastable;
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdComputer>> GetUnconstrainedDelegationComputersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION; exclude DCs (bit 8192)
            var filter = CheckFilters.UnconstrainedDelegationComputers;
            return QueryComputers(filter);
        });
    }

    public async Task<List<AdUser>> GetUnconstrainedDelegationUsersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION
            var filter = CheckFilters.UnconstrainedDelegationUsers;
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetPasswordNotRequiredAccountsAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 32 = PASSWD_NOTREQD
            var filter = CheckFilters.PasswordNotRequired;
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetKerberoastableAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = CheckFilters.Kerberoastable;
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetAdminSdHolderAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = CheckFilters.AdminSdHolder;
            return QueryUsers(filter);
        });
    }
}
