# TypeScript Patterns

## Type System

**Prefer `interface` for object shapes, `type` for everything else:**
```typescript
interface UserRepository { findById(id: string): Promise<User>; }
type UserId = string;
type Result<T> = { data: T; error: null } | { data: null; error: Error };
```

**Avoid `any` — use `unknown` then narrow:**
```typescript
function parse(raw: unknown): Config {
  if (typeof raw !== "object" || raw === null) throw new Error("invalid");
  return raw as Config; // after structural check
}
```

**`satisfies` for validation without widening:**
```typescript
const config = { port: 3000, host: "localhost" } satisfies ServerConfig;
```

## Functions

- Explicit return types on public/exported functions
- Arrow functions for callbacks, function declarations for top-level
- Overloads only when return type changes by input type — otherwise use union

## Async

```typescript
// Always await — never fire-and-forget without void
void sendAnalytics(event); // intentional: mark explicitly

// Prefer explicit Promise<T> when body is trivially async
function getUser(id: string): Promise<User> {
  return db.users.findUnique({ where: { id } });
}
```

## Modules (ESM)

```typescript
// Named exports > default exports (better tree-shaking + renaming)
export function parseConfig() {}
export type { Config };

// Always include .ts extension in ESM imports
import { parseConfig } from "./config.ts";
```

## Anti-Patterns

| Avoid | Use instead |
|---|---|
| `value!` non-null assertion | Narrow: `if (value == null) throw` |
| `value as Type` cast | Runtime check + type guard |
| `// @ts-ignore` | Fix the underlying issue |
| Nested ternaries | `if/else` or early return |
| `enum` | `const` object + `keyof typeof` |
