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
    public const int TrustedToAuthForDelegation = 0x1000000; // 16777216 TRUSTED_TO_AUTH_FOR_DELEGATION (protocol transition)

    /// <summary>
    /// Flags actually stored in userAccountControl, in documentation order (S14-4 export
    /// writes one TRUE/FALSE column each). LOCKOUT, PASSWORD_EXPIRED and PASSWD_CANT_CHANGE
    /// are omitted on purpose: AD does not maintain them in this attribute (they live in
    /// msDS-User-Account-Control-Computed or the ACL), so a column would always read FALSE.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, int Bit)> StoredFlags =
    [
        ("SCRIPT",                         0x0001),
        ("ACCOUNTDISABLE",                 0x0002),
        ("HOMEDIR_REQUIRED",               0x0008),
        ("PASSWD_NOTREQD",                 0x0020),
        ("ENCRYPTED_TEXT_PWD_ALLOWED",     0x0080),
        ("TEMP_DUPLICATE_ACCOUNT",         0x0100),
        ("NORMAL_ACCOUNT",                 0x0200),
        ("INTERDOMAIN_TRUST_ACCOUNT",      0x0800),
        ("WORKSTATION_TRUST_ACCOUNT",      0x1000),
        ("SERVER_TRUST_ACCOUNT",           0x2000),
        ("DONT_EXPIRE_PASSWORD",           0x10000),
        ("MNS_LOGON_ACCOUNT",              0x20000),
        ("SMARTCARD_REQUIRED",             0x40000),
        ("TRUSTED_FOR_DELEGATION",         0x80000),
        ("NOT_DELEGATED",                  0x100000),
        ("USE_DES_KEY_ONLY",               0x200000),
        ("DONT_REQ_PREAUTH",               0x400000),
        ("TRUSTED_TO_AUTH_FOR_DELEGATION", 0x1000000),
        ("PARTIAL_SECRETS_ACCOUNT",        0x4000000),
    ];

    public static bool Has(int uac, int flag)      => (uac & flag) != 0;

    public static bool IsDisabled(int uac)         => Has(uac, Disabled);
    public static bool IsEnabled(int uac)          => !IsDisabled(uac);
    public static bool IsDomainController(int uac) => Has(uac, ServerTrustAccount);
    public static bool PasswordNeverExpires(int uac) => Has(uac, DontExpirePassword);
    public static bool PasswordNotRequired(int uac)  => Has(uac, PasswdNotReqd);
    public static bool TrustedForUnconstrainedDelegation(int uac) => Has(uac, TrustedForDelegation);
    public static bool AsRepRoastable(int uac)     => Has(uac, DontRequirePreauth);
}
