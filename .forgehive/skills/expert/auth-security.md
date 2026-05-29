# Authentication & Session Security

## Secure Password Handling

```typescript
import bcrypt from "bcrypt";

const SALT_ROUNDS = 12;

export async function hashPassword(plain: string): Promise<string> {
  return bcrypt.hash(plain, SALT_ROUNDS);
}

export async function verifyPassword(plain: string, hash: string): Promise<boolean> {
  return bcrypt.compare(plain, hash);
}
```

## JWT Best Practices

```typescript
import jwt from "jsonwebtoken";

const JWT_SECRET = process.env.JWT_SECRET;
if (!JWT_SECRET || JWT_SECRET.length < 32) {
  throw new Error("JWT_SECRET must be at least 32 characters");
}

export function signToken(userId: string): string {
  return jwt.sign({ userId }, JWT_SECRET!, {
    expiresIn: "15m",
    issuer: "myapp",
    audience: "myapp-client",
  });
}

export function verifyToken(token: string): { userId: string } {
  return jwt.verify(token, JWT_SECRET!, {
    issuer: "myapp",
    audience: "myapp-client",
  }) as { userId: string };
}
```

## Session Security

- Use `httpOnly` and `secure` cookie flags
- Set `SameSite=Strict` or `SameSite=Lax`
- Rotate session tokens after privilege escalation
- Implement absolute session timeout (e.g., 8 hours)
- Implement idle timeout (e.g., 30 minutes)

```typescript
app.use(session({
  secret: process.env.SESSION_SECRET!,
  resave: false,
  saveUninitialized: false,
  cookie: {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "strict",
    maxAge: 8 * 60 * 60 * 1000, // 8 hours
  },
}));
```

## Multi-Factor Authentication Checklist

- [ ] TOTP (Time-based One-Time Password) support
- [ ] Recovery codes generated and stored (hashed)
- [ ] Rate limit MFA attempts
- [ ] MFA bypass audit logging

## Security Checklist

- [ ] Passwords hashed with bcrypt/argon2 (not MD5/SHA1)
- [ ] JWT uses short expiry + refresh token pattern
- [ ] Session tokens regenerated on login
- [ ] Brute force protection (rate limiting + account lockout)
- [ ] Secure password reset flow (time-limited tokens)
