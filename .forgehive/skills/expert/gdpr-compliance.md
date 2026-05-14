# GDPR-Compliant Coding Patterns

## Core Principles (Art. 5 GDPR)

| Principle | Implementation |
|-----------|---------------|
| Lawfulness, fairness, transparency | Consent records, privacy notices |
| Purpose limitation | Separate schemas per data use |
| Data minimisation | Collect only what's needed |
| Accuracy | Update mechanisms, validation |
| Storage limitation | Retention policies, automatic deletion |
| Integrity & confidentiality | Encryption, access control |
| Accountability | Audit logs |

---

## PII Data Classification

```typescript
type PiiCategory =
  | "DIRECT_IDENTIFIER"    // name, email, NIN, passport
  | "QUASI_IDENTIFIER"     // DOB, postcode, gender
  | "SENSITIVE_SPECIAL"    // health, biometric, race, religion (Art. 9)
  | "FINANCIAL"            // IBAN, card numbers
  | "BEHAVIORAL"           // browsing history, location traces
  | "NONE";
```

---

## Right to Erasure (Art. 17)

```typescript
export async function eraseUserData(db: Database, userId: string): Promise<void> {
  // 1. Anonymize user record (don't delete if referenced by orders etc.)
  await db.users.update({
    where: { id: userId },
    data: {
      email: `deleted-${userId}@erasure.invalid`,
      name: "Deleted User",
      deletedAt: new Date(),
    },
  });
  // 2. Delete personally identifiable child records
  await db.addresses.deleteMany({ where: { userId } });
  await db.sessions.deleteMany({ where: { userId } });
  // 3. Log the erasure (keep audit trail even without PII)
  await db.erasureLog.create({ data: { userId, erasedAt: new Date() } });
}
```

---

## GDPR Checklist for New Features

- [ ] Lawful basis for processing defined
- [ ] Data minimised — only collect what's needed
- [ ] Retention period defined and enforced
- [ ] Data encrypted at rest (Art. 9 special categories)
- [ ] Access controls in place
- [ ] Processing documented in data register
- [ ] Erasure endpoint handles this data
- [ ] Privacy notice updated
- [ ] DPIA completed if high risk (Art. 35)
