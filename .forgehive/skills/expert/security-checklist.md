# Security Checklist

## Input Validation (OWASP Top 10 — A03)

- [ ] Validate all input at the boundary (type, length, format, range)
- [ ] Use allowlist validation, not denylist
- [ ] Sanitize before storing, escape before rendering
- [ ] Never trust user-controlled data in SQL, shell commands, file paths

```typescript
// Parameterized queries — never string interpolation
const user = await db.query("SELECT * FROM users WHERE id = $1", [userId]);

// Path traversal prevention
const safe = path.resolve(baseDir, userInput);
if (!safe.startsWith(baseDir)) throw new ForbiddenError();
```

## Authentication & Authorization

- [ ] Passwords: bcrypt/argon2 (never MD5/SHA1), min 12 chars
- [ ] Sessions: HttpOnly, Secure, SameSite=Strict cookies
- [ ] JWT: verify signature + expiry, short-lived access tokens
- [ ] Check authorization on every request — don't rely on UI hiding
- [ ] Rate limit auth endpoints (login, password reset, OTP)

## Secrets Management

- [ ] Never hardcode secrets in source code
- [ ] Use env vars for config — never `.env` files in production
- [ ] Rotate secrets regularly; revoke on any exposure
- [ ] Secrets in logs? Redact them.

```typescript
// ✅ Safe logging
log.info("request", { userId, endpoint }); // no token

// ❌ Never
log.info("auth", { token: req.headers.authorization });
```

## Dependencies

- [ ] `npm audit` before every release
- [ ] Pin major versions in production
- [ ] Review changelogs for security advisories when upgrading
- [ ] Remove unused dependencies

## HTTP Security Headers

```typescript
// Minimum set for web APIs
res.setHeader("X-Content-Type-Options", "nosniff");
res.setHeader("X-Frame-Options", "DENY");
res.setHeader("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
res.setHeader("Content-Security-Policy", "default-src 'none'");
```

## Error Responses

- [ ] Never expose stack traces to clients
- [ ] Generic error messages in production (log details server-side)
- [ ] Consistent error format (don't leak internal structure)

## Data

- [ ] Encrypt sensitive data at rest (PII, payment info)
- [ ] TLS everywhere in transit
- [ ] Minimal data collection — don't store what you don't need
- [ ] GDPR: deletion capability, data export capability
