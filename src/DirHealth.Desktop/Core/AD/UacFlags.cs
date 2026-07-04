namespace DirHealth.Desktop.Core.AD;

// userAccountControl bit helpers — pure, so the scanner's account classification is unit-testable.
public static class UacFlags
{
    public const int Disabled             = 0x0002;      // 2       ACCOUNTDISABLE
    public const int PasswdNotReqd         = 0x0020;      // 32      PASSWD_NOTREQD
    public const int ServerTrustAccount    = 0x2000;      // 8192    SERVER_TRUST_ACCOUNT (domain controller)
    public const int DontExpirePassword    = 0x10000;     // 65536   DONT_EXPIRE_PASSWORD
    public const int TrustedForDelegation  = 0x80000;     // 524288  TRUSTED_FOR_DELEGATION (unconstrained)
    public const int DontRequirePreauth    = 0x400000;    // 4194304 DONT_REQ_PREAUTH (AS-REP roastable)

    public static bool Has(int uac, int flag)      => (uac & flag) != 0;

    public static bool IsDisabled(int uac)         => Has(uac, Disabled);
    public static bool IsEnabled(int uac)          => !IsDisabled(uac);
    public static bool IsDomainController(int uac) => Has(uac, ServerTrustAccount);
    public static bool PasswordNeverExpires(int uac) => Has(uac, DontExpirePassword);
    public static bool PasswordNotRequired(int uac)  => Has(uac, PasswdNotReqd);
    public static bool TrustedForUnconstrainedDelegation(int uac) => Has(uac, TrustedForDelegation);
    public static bool AsRepRoastable(int uac)     => Has(uac, DontRequirePreauth);
}
