# Performance Patterns

## Measure First

Never optimize without data. Profile before you guess.

```bash
# Node.js profiling
node --prof app.js
node --prof-process isolate-*.log

# Simple timing
console.time("operation");
await heavyOperation();
console.timeEnd("operation");
```

## Database

**The N+1 Problem:**
```typescript
// ❌ N+1: 1 query for posts + N queries for authors
const posts = await db.posts.findMany();
for (const post of posts) {
  post.author = await db.users.findById(post.authorId); // N queries
}

// ✅ 1 query with JOIN or 2 queries with batch load
const posts = await db.posts.findMany({ include: { author: true } });
```

**Indexing:**
- Index foreign keys and columns used in WHERE, ORDER BY
- Composite indexes: column order matters (most selective first)
- Check `EXPLAIN ANALYZE` for slow queries

**Pagination:** Always paginate large result sets — never `SELECT *` without `LIMIT`.

## Caching

```typescript
// Cache expensive computations
const cache = new Map<string, { data: T; expires: number }>();

function cached<T>(key: string, ttlMs: number, fn: () => Promise<T>): Promise<T> {
  const hit = cache.get(key);
  if (hit && Date.now() < hit.expires) return Promise.resolve(hit.data);
  return fn().then(data => { cache.set(key, { data, expires: Date.now() + ttlMs }); return data; });
}
```

Cache at the right layer: HTTP (CDN/ETag), application (Redis), DB (query cache).

## Async / Concurrency

```typescript
// ❌ Sequential when parallel is possible
const user = await getUser(id);
const prefs = await getPrefs(id);

// ✅ Parallel
const [user, prefs] = await Promise.all([getUser(id), getPrefs(id)]);
```

## Bundle Size (Frontend)

- Code splitting: lazy-load routes and heavy components
- Tree shaking: named imports only (`import { fn } from "lib"`)
- Analyze: `npx source-map-explorer dist/*.js`

## Anti-Patterns

| Avoid | Why |
|---|---|
| Premature optimization | Adds complexity for imaginary gains |
| Synchronous I/O in async context | Blocks event loop |
| Loading all records into memory | OOM on large datasets |
| Polling every 100ms | Use webhooks or SSE instead |
| Re-fetching uncached data on every render | Stale-while-revalidate pattern |
