# DirHealth – Claude Code Project Briefing

> This document is the complete briefing for Claude Code.
> Save it as `CLAUDE.md` in the project root.

---

## Project Overview

**DirHealth** is a Windows desktop tool (C# WPF, single .exe) for automated analysis and health scoring of Active Directory environments. The tool runs locally inside the company network, connects read-only to AD, and displays a hygiene dashboard with findings, compliance score, and export functionality.

**Target audience:** Global IT admins and MSPs managing SMB environments (20–500 users).
**Language:** English — all UI, reports, landing page, emails, and API responses.
**Monetization:** 14-day trial → purchase → license key. License validation against online license server (VPS).

---

## Infrastructure

| Component | Value |
|---|---|
| **Domain** | dirhealth.app |
| **Landing Page** | https://dirhealth.app |
| **License API** | https://license.dirhealth.app |
| **Admin Panel** | https://admin.dirhealth.app |
| **VPS IP** | 213.165.72.148 |
| **VPS OS** | Ubuntu 24.04 LTS |
| **VPS Provider** | Strato |

---

## Repository Structure

```
dirhealth/
├── CLAUDE.md                          ← this file
├── README.md
├── .gitignore
├── .github/
│   └── workflows/
│       └── build.yml                  ← GitHub Actions: Build + Release .exe
│
├── src/
│   ├── DirHealth.Desktop/             ← C# WPF Desktop App
│   │   ├── DirHealth.Desktop.csproj
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   │
│   │   ├── Core/
│   │   │   ├── HWID/
│   │   │   │   └── HwidManager.cs     ← compute HWID (CPU+MB+Disk SHA256)
│   │   │   ├── License/
│   │   │   │   ├── LicenseManager.cs  ← main logic: Trial + License
│   │   │   │   ├── TrialManager.cs    ← Trial stores (Registry + AppData + ProgramData)
│   │   │   │   ├── LicenseValidator.cs← online check against API
│   │   │   │   └── Models/
│   │   │   │       ├── LicenseStatus.cs
│   │   │   │       └── ValidationResult.cs
│   │   │   ├── AD/
│   │   │   │   ├── AdScanner.cs       ← all AD queries (read-only)
│   │   │   │   ├── AdConnector.cs     ← LDAP connection
│   │   │   │   └── Models/
│   │   │   │       ├── AdFinding.cs
│   │   │   │       ├── AdUser.cs
│   │   │   │       ├── AdGroup.cs
│   │   │   │       └── AdComputer.cs
│   │   │   ├── Crypto/
│   │   │   │   └── CryptoHelper.cs    ← AES-256 Encrypt/Decrypt + HMAC
│   │   │   └── Export/
│   │   │       ├── CsvExporter.cs
│   │   │       └── PdfExporter.cs
│   │   │
│   │   ├── ViewModels/                ← MVVM
│   │   │   ├── BaseViewModel.cs
│   │   │   ├── MainViewModel.cs
│   │   │   ├── DashboardViewModel.cs
│   │   │   ├── FindingsViewModel.cs
│   │   │   ├── ActivationViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   │
│   │   ├── Views/
│   │   │   ├── Dashboard/
│   │   │   │   └── DashboardView.xaml
│   │   │   ├── Findings/
│   │   │   │   └── FindingsView.xaml
│   │   │   ├── Activation/
│   │   │   │   └── ActivationView.xaml  ← Trial expiry / Key activation
│   │   │   └── Settings/
│   │   │       └── SettingsView.xaml
│   │   │
│   │   ├── Controls/                  ← reusable UI controls
│   │   │   ├── ScoreRing.xaml         ← circular compliance score chart
│   │   │   ├── FindingCard.xaml
│   │   │   ├── TrialBanner.xaml       ← "X days left in trial" banner
│   │   │   └── SeverityBadge.xaml
│   │   │
│   │   ├── Resources/
│   │   │   ├── Styles.xaml            ← Dark theme, colors, fonts
│   │   │   ├── Icons.xaml
│   │   │   └── Strings.xaml           ← strings (for future i18n)
│   │   │
│   │   └── appsettings.json           ← API URL, trial duration etc.
│   │
│   └── DirHealth.Tests/               ← Unit Tests
│       ├── DirHealth.Tests.csproj
│       ├── HwidTests.cs
│       ├── TrialManagerTests.cs
│       └── LicenseValidatorTests.cs
│
├── server/                            ← Node.js License Server (for VPS)
│   ├── package.json
│   ├── server.js                      ← Express App Entry
│   ├── .env.example
│   │
│   ├── routes/
│   │   ├── trial.js                   ← POST /v1/trial/check
│   │   ├── license.js                 ← POST /v1/license/validate + /activate
│   │   └── admin.js                   ← Admin API (protected)
│   │
│   ├── middleware/
│   │   ├── auth.js                    ← JWT + Admin-Token validation
│   │   └── rateLimit.js               ← Rate Limiting
│   │
│   ├── db/
│   │   ├── schema.sql                 ← PostgreSQL Schema
│   │   └── client.js                  ← DB connection (pg)
│   │
│   └── utils/
│       ├── jwt.js
│       └── crypto.js
│
├── admin-panel/                       ← React Admin Panel (for VPS)
│   ├── package.json
│   ├── vite.config.js
│   ├── index.html
│   └── src/
│       ├── main.jsx
│       ├── App.jsx
│       ├── components/
│       │   ├── LicenseTable.jsx
│       │   ├── LicenseDetail.jsx
│       │   ├── CreateLicenseModal.jsx
│       │   ├── Dashboard.jsx
│       │   └── ValidationLog.jsx
│       ├── hooks/
│       │   └── useApi.js
│       └── api/
│           └── client.js              ← Fetch wrapper for Admin API
│
├── landing/                           ← Static Landing Page (nginx)
│   ├── index.html
│   ├── css/
│   ├── js/
│   └── assets/
│
└── deploy/                            ← VPS Setup
    ├── docker-compose.yml
    ├── nginx/
    │   └── nginx.conf
    ├── .env.example
    └── setup.sh                       ← one-time VPS setup script
```

---

## Tech Stack

### Desktop App (C# WPF)
- **.NET 8** – Self-contained publish → Single .exe, no runtime required
- **WPF** – Native Windows UI, Dark Theme
- **MVVM** – CommunityToolkit.Mvvm
- **AD-Abfragen** – `System.DirectoryServices.AccountManagement` + `DirectorySearcher`
- **HTTP** – `HttpClient` for license server communication
- **Crypto** – `System.Security.Cryptography` (AES-256, HMAC-SHA256)
- **Registry** – `Microsoft.Win32.Registry`
- **Export** – CsvHelper + PdfSharp

### Lizenzserver (Node.js auf VPS)
- **Express** – REST API
- **PostgreSQL** – pg-Paket
- **JWT** – jsonwebtoken (RS256 mit Private Key)
- **bcrypt** – password hashing for admin login
- **express-rate-limit** – brute-force protection
- **helmet** – security headers

### Admin-Panel (React)
- **React 18 + Vite**
- **Tailwind CSS** (via CDN, no build step needed for static files)
- The finished admin panel from the briefing is already available as JSX

### VPS (Strato Ubuntu 24.04)
- **Docker + Docker Compose**
- **Nginx** – Reverse Proxy, SSL-Terminierung
- **Certbot** – Let's Encrypt SSL
- **PostgreSQL 16** – in Docker

---

## Environment Variables (.env on VPS)

```env
# Database
DB_PASSWORD=sicheres_passwort_hier
DB_URL=postgresql://licenseuser:${DB_PASSWORD}@db/licensedb

# JWT (RS256 – generate with: openssl genrsa -out private.pem 2048)
JWT_PRIVATE_KEY_PATH=/run/secrets/jwt_private
JWT_PUBLIC_KEY_PATH=/run/secrets/jwt_public

# Admin Panel Login
ADMIN_EMAIL=deine@email.de
ADMIN_PASSWORD_HASH=bcrypt_hash_hier

# App
PORT=3000
TRIAL_DAYS=14
APP_NAME=DirHealth
```

---

## API Endpoints (License Server)

### Public (called by Desktop App)

```
POST /v1/trial/check
  Body:  { hwid, app_version }
  Reply: { status, trial_start, days_remaining }

POST /v1/license/activate
  Body:  { license_key, hwid }
  Reply: { success, token, message }

POST /v1/license/validate
  Body:  { license_key, hwid }
  Reply: { valid, token, expires_at, plan }
```

### Admin (requires Admin JWT)

```
POST   /admin/auth/login
GET    /admin/licenses
POST   /admin/licenses
GET    /admin/licenses/:id
PUT    /admin/licenses/:id
DELETE /admin/licenses/:id
GET    /admin/licenses/:id/hwids
DELETE /admin/licenses/:id/hwids/:hwid_id
GET    /admin/logs
GET    /admin/dashboard/stats
```

---

## Database Schema (PostgreSQL)

```sql
-- Licenses
CREATE TABLE licenses (
    id              SERIAL PRIMARY KEY,
    license_key     TEXT UNIQUE NOT NULL,
    customer_name   TEXT NOT NULL,
    email           TEXT NOT NULL,
    plan            TEXT NOT NULL CHECK (plan IN ('single','team','msp','enterprise')),
    hwid_slots      INTEGER NOT NULL DEFAULT 1,
    valid_until     TIMESTAMP NULL,          -- NULL = lifetime/permanent
    active          BOOLEAN DEFAULT true,
    discount        INTEGER DEFAULT 0,
    discount_note   TEXT DEFAULT '',
    billing         TEXT DEFAULT 'yearly',
    revenue         INTEGER DEFAULT 0,
    notes           TEXT DEFAULT '',
    created_at      TIMESTAMP DEFAULT NOW()
);

-- Registered Devices
CREATE TABLE hwid_registrations (
    id          SERIAL PRIMARY KEY,
    license_id  INTEGER REFERENCES licenses(id) ON DELETE CASCADE,
    hwid        TEXT NOT NULL,
    label       TEXT DEFAULT '',
    first_seen  TIMESTAMP DEFAULT NOW(),
    last_seen   TIMESTAMP DEFAULT NOW(),
    blocked     BOOLEAN DEFAULT false,
    UNIQUE(license_id, hwid)
);

-- Trial Tracking
CREATE TABLE trial_hwids (
    id          SERIAL PRIMARY KEY,
    hwid        TEXT UNIQUE NOT NULL,
    started_at  TIMESTAMP DEFAULT NOW(),
    app_version TEXT DEFAULT '',
    converted   BOOLEAN DEFAULT false,
    license_id  INTEGER REFERENCES licenses(id) NULL
);

-- Validation Log
CREATE TABLE validation_log (
    id          SERIAL PRIMARY KEY,
    license_id  INTEGER NULL,
    license_key TEXT,
    hwid        TEXT,
    app_version TEXT,
    result      TEXT NOT NULL,   -- ok|invalid_key|expired|hwid_limit|blocked
    ip_address  TEXT,
    timestamp   TIMESTAMP DEFAULT NOW()
);

-- Admin Users
CREATE TABLE admin_users (
    id          SERIAL PRIMARY KEY,
    email       TEXT UNIQUE NOT NULL,
    password    TEXT NOT NULL,   -- bcrypt
    totp_secret TEXT NULL,       -- 2FA
    created_at  TIMESTAMP DEFAULT NOW()
);

-- Indexes
CREATE INDEX idx_licenses_key ON licenses(license_key);
CREATE INDEX idx_hwid_reg_license ON hwid_registrations(license_id);
CREATE INDEX idx_trial_hwid ON trial_hwids(hwid);
CREATE INDEX idx_log_timestamp ON validation_log(timestamp DESC);
```

---

## Desktop App: Startup Flow

```
1. App startet
2. HWID berechnen (CPU + Mainboard + Disk → SHA256)
3. Check cached JWT (locally encrypted)
   → Valid (>1h remaining)? → continue to step 7
4. Online: POST /v1/license/validate (License key from settings)
   → License valid? → cache JWT → continue to step 7
   → Kein License-Key? → Weiter zu Schritt 5
5. Trial-Check: POST /v1/trial/check
   → Trial aktiv? → TrialBanner anzeigen → Weiter zu Schritt 7
   → Trial expired? → show activation screen → STOP
   → Offline? → check local trial stores (72h grace period)
6. Aktivierungsscreen
   → Key eingeben → POST /v1/license/activate
   → Erfolg → JWT speichern → Hauptfenster
7. Hauptfenster laden
   → Start AD scan (background)
   → Dashboard anzeigen
```

---

## Desktop App: HWID Calculation

```csharp
// HwidManager.cs
public static string ComputeHWID()
{
    var cpu   = GetWmiValue("Win32_Processor", "ProcessorId");
    var mb    = GetWmiValue("Win32_BaseBoard", "SerialNumber");
    var disk  = GetWmiValue("Win32_DiskDrive", "SerialNumber");
    var raw   = $"{cpu}-{mb}-{disk}";
    using var sha = SHA256.Create();
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes);
}
```

---

## Trial Storage Locations (all 3 simultaneously, earliest date wins)

```
1. Registry:    HKCU\SOFTWARE\DirHealth\Core  → "InstallToken"
2. AppData:     %APPDATA%\ADHygiene\.cfg
3. ProgramData: %PROGRAMDATA%\ADHygiene\.sys
```

All values: AES-256 encrypted using HWID as key base + HMAC for integrity verification.

---

## AD Scan Categories

```csharp
// AdScanner.cs – implement these methods:

Task<List<AdUser>>     GetInactiveUsers(int daysThreshold = 90)
Task<List<AdUser>>     GetNeverExpiresUsers()
Task<List<AdUser>>     GetExpiredPasswordUsers(int daysThreshold = 365)
Task<List<AdGroup>>    GetEmptyGroups()
Task<List<AdGroup>>    GetSingleMemberGroups()
Task<List<AdComputer>> GetInactiveComputers(int daysThreshold = 90)
Task<List<AdComputer>> GetComputersWithoutOS()
Task<int>              ComputeComplianceScore()
```

---

## Build & Release (GitHub Actions)

```yaml
# .github/workflows/build.yml
# Trigger: Push auf main oder neues Tag (v*)
# Output: DirHealth-Setup.exe (Inno Setup Installer)

steps:
  - dotnet publish -c Release -r win-x64 --self-contained true
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
  - Inno Setup → DirHealth-Setup.exe
  - Upload als GitHub Release Asset
```

---

## VPS: Docker Compose (deploy/docker-compose.yml)

```yaml
services:
  api:
    build: ./server
    restart: unless-stopped
    environment:
      - DB_URL=postgresql://licenseuser:${DB_PASSWORD}@db/licensedb
      - JWT_SECRET=${JWT_SECRET}
      - TRIAL_DAYS=14
    depends_on: [db]
    networks: [internal]

  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      - POSTGRES_DB=licensedb
      - POSTGRES_USER=licenseuser
      - POSTGRES_PASSWORD=${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./server/db/schema.sql:/docker-entrypoint-initdb.d/schema.sql
    networks: [internal]

  admin:
    image: nginx:alpine
    restart: unless-stopped
    volumes:
      - ./admin-panel/dist:/usr/share/nginx/html:ro
    networks: [internal]

  landing:
    image: nginx:alpine
    restart: unless-stopped
    volumes:
      - ./landing:/usr/share/nginx/html:ro
    networks: [internal]

  nginx:
    image: nginx:alpine
    restart: unless-stopped
    ports: ["80:80", "443:443"]
    volumes:
      - ./deploy/nginx/nginx.conf:/etc/nginx/nginx.conf:ro
      - /etc/letsencrypt:/etc/letsencrypt:ro
    networks: [internal]

volumes:
  pgdata:

networks:
  internal:
    driver: bridge
```

---

## VPS: Nginx Config (deploy/nginx/nginx.conf)

```nginx
# Subdomains – passe dirhealth.app an!

# License Server API
server {
    listen 443 ssl;
    server_name license.dirhealth.app;
    ssl_certificate /etc/letsencrypt/live/dirhealth.app/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/dirhealth.app/privkey.pem;
    location / {
        proxy_pass http://api:3000;
        proxy_set_header X-Real-IP $remote_addr;
        limit_req zone=api burst=20 nodelay;
    }
}

# Admin Panel
server {
    listen 443 ssl;
    server_name admin.dirhealth.app;
    ssl_certificate /etc/letsencrypt/live/dirhealth.app/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/dirhealth.app/privkey.pem;
    location / { proxy_pass http://admin:80; }
    location /api/ { proxy_pass http://api:3000/admin/; }
}

# Landing Page
server {
    listen 443 ssl;
    server_name dirhealth.app www.dirhealth.app;
    ssl_certificate /etc/letsencrypt/live/dirhealth.app/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/dirhealth.app/privkey.pem;
    location / { proxy_pass http://landing:80; }
}

# HTTP → HTTPS Redirect
server {
    listen 80;
    server_name _;
    return 301 https://$host$request_uri;
}

# Rate Limiting
limit_req_zone $binary_remote_addr zone=api:10m rate=10r/m;
```

---

## VPS: Setup Script (deploy/setup.sh)

```bash
#!/bin/bash
# One-time setup on Strato VPS
# Run with: sudo bash setup.sh

set -e

echo "=== DirHealth VPS Setup ==="

# 1. Update system
apt update && apt upgrade -y

# 2. Install Docker
curl -fsSL https://get.docker.com | sh
usermod -aG docker $USER

# 3. Nginx + Certbot
apt install nginx certbot python3-certbot-nginx -y

# 4. Fail2Ban
apt install fail2ban -y

# 5. UFW Firewall
ufw allow 22
ufw allow 80
ufw allow 443
ufw --force enable

# 6. Project folder
mkdir -p /opt/dirhealth
cd /opt/dirhealth

echo ""
echo "=== Setup abgeschlossen ==="
echo "Next steps:"
 echo "1. Clone repo: git clone https://github.com/YOUR_USER/dirhealth ."
echo "2. Create .env: cp .env.example .env && nano .env"
echo "3. SSL: certbot --nginx -d dirhealth.app -d license.dirhealth.app -d admin.dirhealth.app"
echo "4. Build admin panel: cd admin-panel && npm install && npm run build"
echo "5. Start: docker compose up -d"
```

---

## GitHub Repository Setup

```bash
# On your local PC:
git init dirhealth
cd adhygiene
git remote add origin https://github.com/DEIN_USER/dirhealth.git

# .gitignore (important!)
echo "
**/bin/
**/obj/
**/.env
**/node_modules/
**/dist/
*.user
.vs/
private.pem
*.key
" > .gitignore
```

---

## Claude Code: Task Order

When you open Claude Code, give these tasks in this exact order:

### Phase 1: VPS & Server (tonight)
```
1. "Create server/server.js with all API endpoints from CLAUDE.md"
2. "Create server/db/schema.sql with the complete database schema from CLAUDE.md"
3. "Create deploy/docker-compose.yml and deploy/nginx/nginx.conf for dirhealth.app"
4. "Create deploy/setup.sh for Ubuntu 24.04"
5. "Create admin-panel with React + Vite, integrate the admin panel JSX from CLAUDE.md"
```

### Phase 2: Desktop App
```
6.  "Create DirHealth.Desktop.csproj with all required NuGet packages"
7.  "Implement HwidManager.cs"
8.  "Implement CryptoHelper.cs (AES-256 + HMAC)"
9.  "Implement TrialManager.cs with all 3 store implementations"
10. "Implement LicenseValidator.cs"
11. "Implement AdScanner.cs with all scan methods"
12. "Create WPF UI with dark theme"
13. "Implement ActivationView.xaml (trial expiry + key activation screen)"
14. "Create GitHub Actions workflow for Build + Release"
```

---

## Open Decisions (clarify before building)

- [x] Domain: dirhealth.app → already set in nginx.conf
- [ ] GitHub Username? → update in setup.sh + Actions
- [x] Trial duration: 14 days
- [ ] Admin email for first login? → set in .env

---

## Developer Notes

**Setup:** Create `.gitignore` before first `git add` — the spec content is in this file under "GitHub Repository Setup". Without it, `node_modules` gets committed.

**Credential persistence:** `AdConnector` holds credentials in memory; `CredentialStore.Save()` writes them encrypted to `%APPDATA%\DirHealth\credentials.dat`. Any code path that updates credentials (Settings Save, login flow) must call both — updating only `AdConnector` loses the values on next startup.

**WPF PasswordBox pre-population:** Data binding cannot pre-fill a PasswordBox. Any View with a PasswordBox must handle `DataContextChanged` and set `PasswordBox.Password = vm.Password` if non-empty. See `LoginWindow.xaml.cs` and `SettingsView.xaml.cs` for the pattern.

**Async AD queries must catch exceptions:** `try/finally` without `catch` in async ViewModels lets LDAP exceptions reach `DispatcherUnhandledException`, causing cascading crash dialogs. Every `LoadAsync` / `SelectAsync` method that touches AD must have `catch (Exception ex) { StatusMessage = $"...: {ex.Message}"; }`.

**Server tests:** `cd server && npx jest --no-coverage` (run from `server/`, not repo root)

**JWT RS256:** `jsonwebtoken` enforces a 2048-bit minimum key size — including in tests. Generating 1024-bit test keys produces a runtime error, not a compile error.

**Jest mock pattern:** Put `jest.mock('../db/client')` at the top of each test file (hoisted). Set `process.env` vars at module level before `require()`. Share a single `app` instance across `describe` blocks. Avoid `jest.resetModules()` inside `beforeAll` — it breaks mock isolation.

**Admin panel build:** `cd admin-panel && npm install && npm run build` — output in `admin-panel/dist/` (served by nginx)

**Docker Compose:** Run from `deploy/` dir: `cd deploy && docker compose up -d` (volume paths are relative to that dir)

**JWT keys for Docker:** Place at `deploy/secrets/private.pem` + `deploy/secrets/public.pem` (mounted as Docker secrets, not env vars)

**Seed first admin user** (run after `docker compose up -d`):
```bash
docker compose exec api node -e "const {query}=require('./db/client');const bcrypt=require('bcrypt');query('INSERT INTO admin_users(email,password) VALUES(\$1,\$2)',[process.env.ADMIN_EMAIL,bcrypt.hashSync(process.env.ADMIN_PASSWORD,12)]).then(()=>process.exit())"
```

**C# JSON deserialization:** Server returns snake_case (`expires_at`, `days_remaining`). All response records in `LicenseValidator.cs` must use `[JsonPropertyName("...")]` attributes — omitting them causes silent null/default values, not exceptions.

**Icon placeholder:** `Resources/icon.ico` is referenced in the `.csproj` via `<ApplicationIcon>`. A valid `.ico` file must exist or the build fails. Replace the placeholder with real branding before release.

**Building the .exe:** `dotnet build` does NOT produce the single-file exe. Use `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o ./publish` — or push a `v*` tag to trigger GitHub Actions.

**VPS repo path:** Repo liegt unter `/root/dirhealth` (nicht `/opt/dirhealth` wie in installation.md — alle Pfade entsprechend ersetzen).

**Dockerfile:** `server/Dockerfile` muss existieren. Entrypoint ist `start.js` (nicht `server.js` — server.js exportiert nur die App, start.js startet den Listener).

**Admin user seeding:** Bcrypt-Hash niemals per `psql -c "... '$2b$12$...'"` einfügen — `$`-Zeichen werden von der Shell expandiert. Stattdessen Node-Script verwenden: `docker compose run --rm api node -e "const {query}=require('./db/client');const bcrypt=require('bcrypt');bcrypt.hash('PW',12).then(h=>query('INSERT INTO admin_users(email,password) VALUES(\$1,\$2)',['email',h])).then(()=>process.exit())"`

**npm auf VPS:** Nicht vorinstalliert — `apt install -y nodejs npm` vor dem Admin-Panel-Build ausführen.

**Admin-Panel Login:** Bei Login-Problemen zuerst Incognito-Fenster testen — Browser-Cache/localStorage kann alten ungültigen Token enthalten.

**Admin panel redeploy after JSX changes:** `cd ~/dirhealth/admin-panel && npm run build && cd ../deploy && docker compose restart admin`

**UseWindowsForms:** Add `<UseWindowsForms>true</UseWindowsForms>` to `.csproj` when using `System.Windows.Forms.SystemInformation` (e.g., `TerminalServerSession`). WPF projects don't include it by default.

**DynamicResource in ControlTemplate Triggers:** Cannot use `{DynamicResource Foo}` shorthand inside a `<Trigger>` setter. Use verbose form: `<Setter Property="X"><Setter.Value><DynamicResource ResourceKey="Foo" /></Setter.Value></Setter>`

**CommunityToolkit.Mvvm async commands:** `[RelayCommand]` on `Task FooAsync()` generates `FooCommand` (strips "Async"). Bind as `{Binding FooCommand}` in XAML, not `FooAsyncCommand`.

**dotnet nicht auf Dev-Maschine:** Build nur via GitHub Actions — Tag pushen: `git tag v1.0.0 && git push origin v1.0.0` (PowerShell: `;` statt `&&`).

**WPF Run.Text Binding:** `Run.Text` hat TwoWay als Default — `{Binding}` ohne Path oder `{Binding Foo.Count}` auf readonly-Properties crasht mit XamlParseException. Immer `Mode=OneWay` angeben bei Run-Elementen und Count-Bindings in DataTriggers.

**AD Range Retrieval:** `member`-Attribut in AD hat ein konfigurierbares Limit (MaxValRange, oft 15–1500). Gruppen mit mehr Mitgliedern als das Limit müssen per Range-Retrieval abgefragt werden (`member;range=0-*`). Ohne Range-Retrieval werden nur die ersten N Mitglieder zurückgegeben.

**WPF Transparent Splash:** Bei `WindowStyle=None` + `AllowsTransparency=True` muss auch `Background=Transparent` am Window gesetzt werden, sonst bleibt ein weißer/grauer Rand sichtbar.

**DispatcherTimer Tick-Handler:** Einmalig im Konstruktor registrieren, nicht in `Start()` — sonst akkumuliert der Handler bei jedem `Start()`-Aufruf und der Scan-Job feuert N-fach.

**DispatcherUnhandledException:** File-Operationen im Handler in try-catch wrappen. `MessageBox.Show` darin erzeugt nested Dispatcher-Loop — wartende Exceptions lösen kaskadierende Dialoge aus. Crash-Log liegt auf dem Windows-Desktop: `dirhealth-crash.log`.

**WPF ListBox in StackPanel:** StackPanel gibt ListBox infinite Height — sie scrollt nie. Fix: Grid mit RowDefinitions verwenden, ListBox bekommt `<RowDefinition Height="*" />` und entsprechendes `Grid.Row`.

**WPF GridViewColumnHeader dark theme:** GridView-Header erben nicht das Dark Theme — bleiben weiß. Fix: Globalen `<Style TargetType="GridViewColumnHeader">` und `<Style TargetType="ListViewItem">` in `Styles.xaml` definieren. Gilt dann für alle ListViews im gesamten Projekt.

**WPF PropertyChanged Subscription Leak:** `DataContextChanged` feuert bei jeder Navigation. Alten Handler vor dem neuen Abonnieren entfernen — alten VM tracken und `vm.PropertyChanged -= _handler` aufrufen. Siehe `DashboardView.xaml.cs`.

**WPF clickable Border:** `Border` hat kein `Command`-Property. Wrapping-Button verwenden: `<Button BorderThickness="0" Padding="0" FocusVisualStyle="{x:Null}" Cursor="Hand" Command="...">` mit `<Border>` als Content. Background auf Button, nicht auf Border.

**AD OU Load Performance:** OU-Counts niemals beim initialen Laden abfragen (N×3 LDAP Queries → extrem langsam). Counts erst bei Auswahl laden via `GetOUCountsAsync(dn)` → `(int Users, int Computers, int Groups)`. Kein UserCount/ComputerCount/GroupCount mehr auf dem `AdOU`-Modell.

**IsBusy CanExecute from subclass:** Cannot use `[NotifyCanExecuteChangedFor]` on `_isBusy` in BaseViewModel from a subclass — add `partial void OnIsBusyChanged(bool value) => MyCommand.NotifyCanExecuteChanged();` in the subclass instead.

**Stale-cache CanExecute guard:** `LastScanTime != "Never"` is insufficient — if the app loads with a persisted cache, `LastScanTime` is set but in-memory data lists are empty. Use a private `bool _liveScanCompleted` flag (set only in `RunScanAsync` after data is fetched) as the CanExecute guard.

**PdfSharp page breaks:** Column headers must be reprinted after every page break. Extract a local `Action drawHeaders` and call it both before the first row and after every `NewPage()` call.

**Export commands need try/catch:** Wrap all exporter calls in try/catch with `StatusMessage = $"Export failed: {ex.Message}"` — file locks and network-share failures otherwise bubble to the global crash handler instead of showing an inline message.

**Auto-Update Check — Startup vs. manuell trennen:** `App.xaml.cs` hat zwei separate Methoden: `StartupUpdateCheckAsync` (mit `Task.Delay(5000)`, fire-and-forget) und `ManualUpdateCheckAsync` (kein Delay, gibt `Task<string>` mit Diagnostic zurück). `OnCheckForUpdates` in `SettingsViewModel` ist `Func<Task<string>>?` — der Rückgabewert wird direkt als `UpdateStatusText` angezeigt, damit der User sieht ob er aktuell ist (`"Up to date (installed=1.0.1, latest=1.0.1)"`) oder ein Update gefunden wurde. Exceptions im manuellen Check werden nicht geschluckt sondern als Fehlermeldung zurückgegeben.

**Auto-Update: `/releases/latest` vs. `/releases`:** GitHub API `/releases/latest` gibt 404 zurück wenn Releases als pre-release markiert sind — auch wenn sie sichtbar auf der Releases-Seite erscheinen. Fix: `/releases`-Endpoint verwenden und ersten non-draft Eintrag nehmen. Im `build.yml` explizit `prerelease: false` und `draft: false` bei `softprops/action-gh-release` setzen. Repo-Name im API-URL muss exakt mit `git remote -v` übereinstimmen — Tippfehler im Repo-Namen geben ebenfalls 404.

**Auto-Update Download-Progress:** GitHub CDN liefert kein `Content-Length`-Header → `total = -1` → Prozentanzeige bleibt leer. Fix: Dateigröße aus `assets[].size` im GitHub API Response lesen und in `UpdateInfo.FileSize` speichern. Im Download `_updateFileSize` als Fallback verwenden.

**Auto-Update Neustart nach Installation:** Inno Setup `[Run]`-Eintrag mit `skipifsilent` wird bei `/SILENT`-Updates übersprungen. Fix: zweiten Eintrag ohne `skipifsilent` und mit `Check: WizardSilent` hinzufügen — läuft nur im Silent-Modus, erster Eintrag nur im interaktiven Modus. `/RESTARTAPP` ist kein gültiger Inno Setup Switch — nicht verwenden.

**Auto-Update File Lock:** `using var dest = File.Create(tmp)` mit `Process.Start(tmp, ...)` im selben `try`-Block → Datei ist beim Installer-Start noch gelockt → "Der Prozess kann nicht auf die Datei zugreifen". Fix: expliziten `using(...) { }` Block verwenden damit der FileStream vor `Process.Start` disposed wird.

**Auto-Update vollständiger Flow (bestätigt funktionierend ab v2.1.3):** Check beim Start (5s Delay, fire-and-forget) + manuell in Settings → Streaming-Download mit Prozentanzeige (% aus `assets[].size`) → Inno Setup `/SILENT /CLOSEAPPLICATIONS` → App öffnet automatisch nach Installation (`Check: WizardSilent` in `setup.iss`). Alle fünf Bugfixes oben waren nötig damit der Flow end-to-end funktioniert.

---

## Pläne

Implementation plans are stored in `docs/superpowers/plans/`. Each plan covers one feature or refactor scope and is written for agentic execution.

| Datum | Plan | Status |
|-------|------|--------|
| 2026-04-25 | [Remove Licensing](docs/superpowers/plans/2026-04-25-remove-licensing.md) | ✅ Done |
| 2026-04-25 | [Landing Page](docs/superpowers/plans/2026-04-25-landing-page.md) | ✅ Done |

---

## Feature Roadmap

Alle geplanten read-only AD Features sind in [`docs/feature-roadmap.md`](docs/feature-roadmap.md) dokumentiert — mit vollständigen LDAP-Filtern, AD-Attributen, Severity-Logik und UI-Hinweisen pro Feature.

**Aktuelle Phase:** Phase 1 — Option B

| Phase | Features | Status |
|-------|----------|--------|
| **1** | EOL-Betriebssysteme (Finding) + DC Inventory View | ✅ Done |
| **2 — Next** | AS-REP Roasting, Unconstrained Delegation, Passwort nicht erforderlich | 🔜 Next |
| 3 | Stale Privileged Accounts, Fine-Grained Password Policies, Privileged Groups Overview | Planned |
| 4 | Domain Trust View, SID History, Timeline / Recent Changes | Planned |
| 5 | GPO Browser, AD Data Quality Report | Planned |

Details, LDAP-Filter und Implementierungshinweise → [`docs/feature-roadmap.md`](docs/feature-roadmap.md)

---

## Offene Punkte (Stand 2026-04-25)

> **Hinweis (2026-04-25):** Lizenzierung wurde aus dem Desktop-Tool entfernt. Der License-Server (`server/`) und das Admin-Panel bleiben deployed, werden aber client-seitig nicht mehr erzwungen. MSP/Consultant-Preispläne sind zurückgestellt bis zur Go-to-Market-Entscheidung.

### Bugs
- [x] App-Crash nach Scan: kaskadierende "DirHealth Error"-Dialoge — behoben (XamlParseException durch TwoWay-Bindings), in v1.0.12 bestätigt
- [x] PropertyChanged-Subscription-Leak — behoben in `DashboardView.xaml.cs`: alten Handler vor Subscribe entfernen

### Features
- [x] **Direkte Lizenz-Aktivierung in der App** — License-Karte in Settings: Key eingeben, "Activate"-Button, Status-Anzeige
- [x] **Landing Page `index.html`:** implementiert — dark-themed single-file static page mit Nav, Hero, Features, Screenshot, How It Works, Donate, Footer
- [ ] **Admin-Panel Erweiterungen:** (konkrete Wünsche folgen später)
- [x] **Sicherheitschecks:** Kerberoastable Accounts, AdminSDHolder-Anomalien, GPO-Analyse (fehlende Passwort-Policy) — alle drei in AdScanner + RunFullScanAsync + ComputeComplianceScoreAsync
- [x] **Findings als erledigt markieren** — lokal implementiert mit Kommentar; Team-Sync als späteres Premium-Feature (erfordert Server-State, zu komplex für jetzt)
- [x] **Score-Verlauf/Trend** — lokal gespeichert, Chart in Dashboard
- [x] **PDF-Export** — implementiert via PdfSharp
- [x] **Trial Banner klickbar** — öffnet Settings zur Lizenzaktivierung
- [x] **ListBox Scrolling (Users / Computers / OUs)** — StackPanel → Grid-Layout
- [x] **OU-Browser Performance** — Counts nicht mehr beim Laden abfragen, nur bei Auswahl
- [x] **Dark-Theme für ListView-Header** — globale Styles in `Styles.xaml`
- [ ] **Domain Admins View** — Tab der direkt alle Domain-Admins anzeigt ohne Suche (Idee aus Session)
- [x] **Phase 1 — EOL-Betriebssysteme + DC Inventory View** — Implementiert: `GetEolComputersAsync()` + `GetAllDomainControllersAsync()` in `AdScanner.cs`, Score-Penalty, `DcInventoryViewModel`, `DcInventoryView.xaml`, Nav-Button "DC Inventory". Details: [`docs/feature-roadmap.md`](docs/feature-roadmap.md)
- [ ] **🔜 Phase 2 — AS-REP Roasting, Unconstrained Delegation, PASSWD_NOTREQD** — Nächstes Feature-Paket, Details siehe [`docs/feature-roadmap.md`](docs/feature-roadmap.md)
- [ ] **One-Click Remediation** *(Vorschlag, vorerst zurückgestellt)* — Direkte Behebung von Findings aus der App: Accounts deaktivieren, Passwort-Ablauf erzwingen, leere Gruppen löschen etc. Mit Bestätigungsdialog + optionalem Reason-Feld. Technisch vollständig implementiert (`AdRemediator.cs`, `ConfirmActionViewModel`, `ConfirmActionDialog`), nach Kollegen-Feedback aber vorerst entfernt — AD-Änderungen werden lieber manuell gemacht. Wiederherstellen via `git revert 49a70b6` (oder Commit `e12d2cd` direkt cherry-picken).

### Consultant-Lizenzierung (Systemhaus / ISA)
- [x] **Modell entschieden:** Systemhäuser die ISAs (IT Security Assessments) durchführen, nutzen DirHealth auf ihrem eigenen Laptop/VM — dieser wird ins Kundennetzwerk gesteckt. Beim Start erscheint der Login-Dialog: Kundendomain + Domain-Admin-Credentials eingeben → vollständiger AD-Scan + PDF-Export für den ISA-Bericht. Da immer dasselbe Gerät des Beraters genutzt wird, bleibt die HWID konstant → **eine Einzellizenz reicht für beliebig viele Kunden**, keine extra HWID-Slots nötig.
- [ ] **Consultant-Plan einführen:** Eigener Plan `consultant` in der Lizenz-Datenbank (neben `single`, `team`, `msp`, `enterprise`). Höherer Preis als Single (Mehrwert: professioneller Einsatz bei Kunden), aber technisch identisch mit Single (1 HWID-Slot). Marketingbotschaft: "Unbegrenzte Kunden-Assessments mit einer Lizenz."
- [ ] **Preise festlegen:** Überlegung — Single ~49€/Jahr, Consultant ~149€/Jahr, MSP nach Slots

### MSP-Lizenzierung
- [x] **Modell entschieden:** MSP läuft DirHealth direkt auf Kunden-DCs (per RDP oder Fernwartungstool) → jeder Kunde = andere HWID → Lizenz mit mehreren HWID-Slots. Bereits im Schema: `hwid_slots INTEGER` in `licenses`. MSP-Plan = License Key mit z.B. 25 Slots. Slots werden beim ersten Start auf neuem Gerät automatisch belegt; Admin kann einzelne HWIDs löschen wenn Kunde abspringt.
- [ ] **Preise festlegen:** Tier-Struktur (Single / Team / MSP / Enterprise / Consultant) — Preise noch offen

### MSP Self-Service Portal (`portal.dirhealth.app`)
Separates Portal für MSP-Kunden — komplett getrennt vom internen Admin-Panel. MSPs verwalten dort ihre eigene Lizenz und alle Kunden-Instanzen selbst.

**Arbeitspakete:**

**1. App-Änderungen:**
- Neues Feld "Customer Label" in Settings (z.B. "Müller GmbH – DC01") — frei editierbar, wird beim Scan an den Server gesendet und als HWID-Label gespeichert
- Nach jedem Scan: kompakte Zusammenfassung (Score, FindingsCount nach Kategorie, Timestamp) an neuen Server-Endpoint senden — keine echten AD-Objekte/Namen, nur Zahlen
- HWID-Label wird bei jeder License-Validate-Anfrage mitgesendet (bereits implementiert mit `MachineName`, muss auf konfigurierbares Label umgestellt werden)

**2. Server-Änderungen (`server/routes/portal.js`):**
- `POST /portal/auth` — Auth per License Key + E-Mail, gibt JWT zurück (kein Admin-Zugang, nur eigene Lizenzdaten)
- `GET /portal/license` — eigene Lizenzdetails (Plan, Slots, valid_until, created_at)
- `GET /portal/hwids` — alle registrierten HWIDs mit Label, last_seen, letztem Score, letztem Scan-Timestamp
- `DELETE /portal/hwids/:hwid_id` — eigene HWID-Slots freigeben (Kunde abgesprungen)
- `POST /portal/hwids/:hwid_id/label` — HWID umbenennen
- `POST /v1/scan/summary` — von der App aufgerufen nach jedem Scan, speichert Score + Finding-Counts pro HWID

**3. Datenbank-Änderungen:**
- Neue Tabelle `scan_summaries`: `(id, hwid_registration_id, score, findings_json, scanned_at)`
- `findings_json`: `{"inactive_users": 5, "never_expires": 12, "kerberoastable": 1, ...}` — nur Counts, keine Namen

**4. Portal-Frontend (`portal/`):**
- Neues React/Vite-Projekt unter `portal/` (analog zu `admin-panel/`)
- Login-Seite: License Key + E-Mail
- Übersicht: Kacheln pro HWID-Slot mit Score (Farbe nach Wert), Customer Label, letzter Scan, Top-3 Findings
- HWID-Detail: Score-Verlauf (letzte 10 Scans), alle Finding-Counts, Aktionen (umbenennen, entfernen)
- Lizenz-Seite: Plan, Slots (belegt/gesamt), Laufzeit
- Subdomain: `portal.dirhealth.app` — eigener nginx-Block, eigener Docker-Service

### Infrastruktur / Pre-Launch
- [ ] GitHub Username in `setup.sh` + `build.yml` noch als Platzhalter
- [ ] Admin-Email + Passwort für VPS `.env` setzen
- [ ] Gewerbe anmelden (vor erstem Verkauf)
- [ ] Icon `icon.ico` — Platzhalter ersetzen durch echtes Branding (Entwürfe in `icon-concepts/`)
- [ ] `/var/www/downloads/` regelmäßig aufräumen — Release-Script löscht bereits alte Versionen automatisch, aber manuell hinterlassene Dateien ggf. bereinigen: `find /var/www/downloads/ -name "DirHealth-Setup*.exe" ! -name "DirHealth-Setup-${LATEST}.exe" -delete`
- [ ] **Admin-Panel Optimierungen** — konkrete Wünsche noch offen, bei nächster Session spezifizieren
- [ ] **Installer Performance** — Installationsgröße und -geschwindigkeit optimieren (konkrete Maßnahmen noch offen)
