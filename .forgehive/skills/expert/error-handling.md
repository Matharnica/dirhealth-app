# Error Handling

## Core Principle: Errors Are Values

Handle errors explicitly at call sites. Don't let them propagate silently or get swallowed.

## Error Types

```typescript
// Domain errors — expected, recoverable
class UserNotFoundError extends Error {
  readonly code = "USER_NOT_FOUND";
  constructor(readonly userId: string) {
    super(`User ${userId} not found`);
    this.name = "UserNotFoundError";
  }
}

// Infrastructure errors — unexpected, potentially fatal
// → Let them propagate up to the boundary handler
```

## Result Pattern (for fallible operations)

```typescript
type Result<T, E extends Error = Error> =
  | { ok: true; value: T }
  | { ok: false; error: E };

function parseConfig(raw: unknown): Result<Config, ConfigError> {
  if (!isValidConfig(raw)) return { ok: false, error: new ConfigError("invalid") };
  return { ok: true, value: raw as Config };
}

// Usage — explicit at call site
const result = parseConfig(input);
if (!result.ok) {
  log.error("bad config", result.error);
  return;
}
use(result.value);
```

## Boundary Handlers

Catch at the outermost layer only:

```typescript
// HTTP handler
app.use((err, req, res, next) => {
  if (err instanceof DomainError) {
    res.status(err.httpStatus).json({ error: { code: err.code, message: err.message } });
  } else {
    log.error("unexpected", err);
    res.status(500).json({ error: { code: "INTERNAL", message: "Internal error" } });
  }
});
```

## Async Error Handling

```typescript
// Always handle rejected promises
const data = await fetchUser(id).catch(err => {
  if (err instanceof NotFoundError) return null;
  throw err; // re-throw unexpected errors
});

// Top-level: never ignore unhandled rejections
process.on("unhandledRejection", (err) => {
  log.error("unhandled rejection", err);
  process.exit(1);
});
```

## Logging

- Log at the boundary, not at every layer (avoid duplicate logs)
- Include context: `log.error("fetch failed", { userId, err })`
- Never log secrets or PII

## Anti-Patterns

| Avoid | Use instead |
|---|---|
| Empty `catch {}` | At minimum log the error |
| `catch (e: any)` | `catch (e: unknown)` then narrow |
| Throwing strings | Throw Error instances |
| Catching everything | Only catch what you can handle |
| Error codes as numbers | String codes (searchable) |
