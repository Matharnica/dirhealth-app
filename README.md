<div align="center">
  <img src="icon-concepts/icon_128.png" alt="DirHealth Logo" width="96" />
  <h1>DirHealth</h1>
  <p><strong>Free Active Directory health scanner for Windows admins.</strong><br/>
  No license key. No subscription. No nag screens.</p>

  <a href="https://github.com/matharnica/dirhealth/releases/latest">
    <img alt="Download" src="https://img.shields.io/github/v/release/matharnica/dirhealth-app?label=Download&style=for-the-badge&color=6366f1" />
  </a>
  &nbsp;
  <a href="https://ko-fi.com/matharnica">
    <img alt="Ko-fi" src="https://img.shields.io/badge/Support-Ko--fi-ff5e5b?style=for-the-badge&logo=ko-fi&logoColor=white" />
  </a>
  &nbsp;
  <a href="https://donate.stripe.com/8x2aEP30W7So5YR3Gaawo01">
    <img alt="Support via Stripe" src="https://img.shields.io/badge/Support-Stripe-635bff?style=for-the-badge&logo=stripe&logoColor=white" />
  </a>
  &nbsp;
  <a href="https://dirhealth.app">
    <img alt="Website" src="https://img.shields.io/badge/Website-dirhealth.app-6366f1?style=for-the-badge" />
  </a>
</div>

---

![DirHealth Dashboard](landing/assets/screenshot.png)

---

## What is DirHealth?

DirHealth scans your Active Directory for stale accounts, weak password policies, and security risks — and gives you a clear hygiene score. Built by an admin, for admins.

**Admin-only access:** DirHealth requires domain admin credentials at login. Your AD data stays in the right hands.

**Read-only:** DirHealth never modifies your directory.

## What it checks

| Category | What DirHealth finds |
|---|---|
| 👤 Stale User Accounts | Accounts with no login for 90+ days |
| 🔑 Password Policy Issues | Never-expire, expired, unchanged for 1yr+, fine-grained PSO weakening |
| 🛡️ Kerberoastable Accounts | SPNs vulnerable to offline password cracking |
| 🎫 AS-REP Roasting | Accounts without Kerberos pre-auth — hash crackable without login |
| 🔓 Delegation Risks | Unconstrained delegation on computers and users |
| 🚫 Password Not Required | Accounts with `PASSWD_NOTREQD` — empty password allowed |
| 👥 Group Hygiene | Empty groups and single-member groups |
| 💻 Inactive Computers | Unseen for 90+ days, missing OS info, EOL operating systems |
| 🏛️ Privileged Groups | Domain Admins, Enterprise Admins, Schema Admins, DnsAdmins, and 5 more |
| 👑 Stale Domain Admins | Privileged accounts inactive for 30+ days |
| 🔗 Domain Trusts | All inter-domain/forest trust relationships with direction and type |
| 🕵️ SID History | Accounts carrying historical SIDs — silent privilege escalation risk |
| 📅 Timeline | All AD objects created or modified in the last 7 / 30 / 90 days |

Every finding reduces your **Hygiene Score (0–100)**. Fix issues, watch the number climb.

## Getting started

1. **Download** the latest `.exe` from [Releases](https://github.com/matharnica/dirhealth-app/releases/latest) — single file, no runtime needed
2. **Log in** with your domain admin credentials
3. **Scan & fix** — review findings, drill into categories, export a full PDF report

## Download

→ [**Latest Release**](https://github.com/matharnica/dirhealth-app/releases/latest)

Requires Windows 10/11. Self-contained .exe — no .NET runtime installation needed.

**Requires an Active Directory environment.** Run on a domain-joined Windows machine or any machine with network access to your domain controller.

## Support the project

DirHealth is free to use and source-available, maintained in spare time. If it saved you an afternoon of manual AD cleanup, consider buying a coffee:

→ [**☕ Support on Ko-fi**](https://ko-fi.com/matharnica)  
→ [**💳 Donate via Stripe**](https://donate.stripe.com/8x2aEP30W7So5YR3Gaawo01)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a full version history.

## License

[Elastic License 2.0](LICENSE) — free to use and self-host. The source code is publicly available for auditing and modification, but you may not offer DirHealth as a hosted or managed service to third parties. This differs from open source licenses in that commercial hosting is restricted; personal use, self-hosting, and private modification are fully permitted.
