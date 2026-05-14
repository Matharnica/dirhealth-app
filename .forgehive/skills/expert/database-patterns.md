# Database Patterns

## Schema Design

**Naming conventions:**
- Tables: `snake_case`, plural (`users`, `order_items`)
- Columns: `snake_case` (`created_at`, `user_id`)
- Primary keys: `id` (UUID or serial)
- Foreign keys: `<table_singular>_id` (`user_id`, `order_id`)
- Booleans: `is_` or `has_` prefix (`is_active`, `has_verified_email`)

**Always include:**
```sql
CREATE TABLE users (
  id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

## Migrations

- One logical change per migration file
- Migrations are **append-only** — never edit a committed migration
- Always test `UP` and `DOWN` before merging
- Non-destructive first: add nullable column → backfill → add NOT NULL constraint

```sql
-- ✅ Safe migration for adding NOT NULL column
ALTER TABLE users ADD COLUMN locale TEXT;          -- 1. add nullable
UPDATE users SET locale = 'en' WHERE locale IS NULL; -- 2. backfill
ALTER TABLE users ALTER COLUMN locale SET NOT NULL; -- 3. constrain
```

## Indexing Strategy

```sql
-- Foreign keys (always index)
CREATE INDEX idx_orders_user_id ON orders(user_id);

-- Common query patterns
CREATE INDEX idx_users_email ON users(email);

-- Composite: put most selective column first
CREATE INDEX idx_orders_status_created ON orders(status, created_at DESC);

-- Partial index for common filtered queries
CREATE INDEX idx_active_users ON users(email) WHERE is_active = true;
```

## Query Patterns

```typescript
// Batch load instead of N+1
const userIds = posts.map(p => p.authorId);
const users = await db.users.findMany({ where: { id: { in: userIds } } });

// Cursor pagination (stable, works with inserts)
const posts = await db.posts.findMany({
  where: { id: { lt: cursor } },
  orderBy: { id: "desc" },
  take: 20,
});

// Use transactions for multi-step mutations
await db.$transaction(async (tx) => {
  await tx.orders.create({ data: order });
  await tx.inventory.decrement({ where: { id: item.id }, by: qty });
});
```

## Anti-Patterns

| Avoid | Why |
|---|---|
| `SELECT *` | Pulls unused columns, breaks on schema change |
| No migrations | Schema drift between environments |
| Storing arrays as comma-separated strings | Use proper array column or join table |
| Soft-delete without index | Full table scan on active queries |
| Logic in triggers | Invisible side effects |
| ORM for complex reports | Write SQL — it's fine |
