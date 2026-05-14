# API Design

## REST Resource Design

```
GET    /users          → list users (paginated)
POST   /users          → create user
GET    /users/:id      → get user
PATCH  /users/:id      → partial update
DELETE /users/:id      → delete

POST   /users/:id/activate   → actions as sub-resources
```

**Naming:** plural nouns for collections, snake_case in JSON, kebab-case in URLs.

## Request / Response Shape

```typescript
// Response envelope
interface ApiResponse<T> {
  data: T;
  meta?: { total: number; page: number; limit: number };
}

// Error envelope — always consistent
interface ApiError {
  error: { code: string; message: string; field?: string };
}
```

## Status Codes

| Code | When |
|---|---|
| 200 | Successful GET, PATCH |
| 201 | Successful POST (resource created) |
| 204 | Successful DELETE (no body) |
| 400 | Validation error (include field) |
| 401 | Not authenticated |
| 403 | Authenticated but not authorized |
| 404 | Resource not found |
| 409 | Conflict (duplicate, stale version) |
| 422 | Semantically invalid (passes schema, fails business rules) |
| 429 | Rate limited |
| 500 | Server error (never leak internals) |

## Versioning

Prefix: `/v1/users` — never break existing clients, deprecate with sunset headers.

## Pagination

```json
GET /users?page=2&limit=20

{
  "data": [...],
  "meta": { "total": 143, "page": 2, "limit": 20, "hasNext": true }
}
```

## Validation

- Validate at the boundary (route handler) — not deep in business logic
- Return all validation errors at once, not just the first
- Use JSON Schema or Zod for input validation

## Anti-Patterns

| Avoid | Use instead |
|---|---|
| Verbs in URLs (`/getUser`) | Nouns + HTTP method |
| `200` for errors | Correct 4xx/5xx |
| Leaking stack traces | Structured error codes |
| Inconsistent field naming | Single convention (snake_case) |
| Boolean flags in body | Semantic sub-resources or PATCH |
