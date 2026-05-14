# OWASP Top 10 — Security Patterns for Node.js/TypeScript

## Overview

This skill covers the OWASP Top 10 (2021) vulnerability categories with detection
patterns and remediation examples in TypeScript/Node.js.

---

## A01: Broken Access Control

**Risk:** Users can act outside their intended permissions.

**Detection patterns:**
- Missing authorization checks before database queries
- Direct object references using user-supplied IDs without ownership verification
- CORS misconfiguration allowing all origins

**Vulnerable:**
```typescript
// ❌ No ownership check
app.get("/documents/:id", authenticate, async (req, res) => {
  const doc = await db.documents.findById(req.params.id);
  res.json(doc);
});
```

**Secure:**
```typescript
// ✅ Verify ownership
app.get("/documents/:id", authenticate, async (req, res) => {
  const doc = await db.documents.findOne({
    _id: req.params.id,
    ownerId: req.user.id,
  });
  if (!doc) return res.status(404).json({ error: "Not found" });
  res.json(doc);
});
```

---

## A02: Cryptographic Failures

**Risk:** Sensitive data exposed due to weak or missing encryption.

**Detection patterns:**
- `MD5` or `SHA1` for password hashing
- Sensitive data in plaintext
- Weak JWT secrets

**Vulnerable:**
```typescript
// ❌ MD5 not suitable for passwords
const hash = crypto.createHash("md5").update(password).digest("hex");
```

**Secure:**
```typescript
// ✅ Use bcrypt for passwords
import bcrypt from "bcrypt";
const hash = await bcrypt.hash(password, 12);
```

---

## A03: Injection

**Risk:** Untrusted data sent to an interpreter as part of a command or query.

**Vulnerable:**
```typescript
// ❌ SQL injection
const rows = await db.query(`SELECT * FROM users WHERE email = '${req.body.email}'`);
```

**Secure:**
```typescript
// ✅ Parameterized queries
const rows = await db.query("SELECT * FROM users WHERE email = $1", [req.body.email]);
```

---

## A04: Insecure Design

**Checklist:**
- [ ] Threat model for sensitive flows
- [ ] Rate limiting on public endpoints
- [ ] Business logic validated server-side

---

## A05: Security Misconfiguration

**Secure Express setup:**
```typescript
import helmet from "helmet";
app.use(helmet());
app.use(cors({ origin: process.env.ALLOWED_ORIGINS?.split(",") ?? [] }));
```

---

## A06: Vulnerable and Outdated Components

```bash
npm audit
fh security deps
```

---

## A07: Identification and Authentication Failures

**Secure JWT:**
```typescript
const token = jwt.sign({ userId }, JWT_SECRET, { expiresIn: "15m" });
```

---

## A08: Software and Data Integrity Failures

- Verify npm package integrity with `npm audit`
- Use lockfiles (`package-lock.json`)
- Pin dependencies in production

---

## A09: Security Logging and Monitoring Failures

```typescript
// Log security events
fh security audit
```

---

## A10: Server-Side Request Forgery (SSRF)

**Vulnerable:**
```typescript
// ❌ Fetch user-controlled URL
const data = await fetch(req.body.url);
```

**Secure:**
```typescript
// ✅ Validate URL against allowlist
const allowed = new URL(req.body.url);
if (!ALLOWED_HOSTS.includes(allowed.hostname)) throw new Error("Blocked");
```
