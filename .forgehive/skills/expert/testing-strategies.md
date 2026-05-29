# Testing Strategies

## Core Principle: Test Behavior, Not Implementation

Tests should verify what a unit does, not how it does it. If a refactor breaks tests without changing behavior, the tests are wrong.

## Test Structure (AAA)

```typescript
it("returns null for unknown user", async () => {
  // Arrange
  const repo = new UserRepository(testDb);

  // Act
  const result = await repo.findById("nonexistent-id");

  // Assert
  assert.equal(result, null);
});
```

## Test Naming

Format: **`[unit] [condition] [expected outcome]`**

```
✅ "findById returns null for unknown id"
✅ "parseConfig throws when port is negative"
❌ "test findById"
❌ "should work correctly"
```

## What to Test

| Test | What |
|---|---|
| Unit | Pure functions, transformations, business logic |
| Integration | DB queries, file I/O, external API clients |
| Contract | API response shapes, event schemas |

**Don't test:** framework behavior, library internals, private methods via backdoor.

## Test Isolation

- Each test owns its state — no shared mutable variables between tests
- `beforeEach` creates fresh instances, `afterEach` cleans up
- Integration tests use real dependencies (test DB, temp files) — not mocks
- Mock only things you don't own (3rd party APIs, time, random)

## Edge Cases to Always Cover

1. Empty / zero / null input
2. Single item (off-by-one)
3. Concurrent/duplicate calls (idempotency)
4. Error paths (not just happy path)

## TDD Cycle

```
Red → write failing test
Green → minimal code to pass
Refactor → clean up, tests still green
Commit → tests are the safety net
```

## Anti-Patterns

| Avoid | Why |
|---|---|
| Mocking internal modules | Couples tests to implementation |
| `expect(x).toBeDefined()` only | Doesn't verify actual value |
| Tests that only pass in sequence | Hidden shared state |
| Snapshot tests for logic | Fragile, hides intent |
| Testing mocks | You're testing the mock, not the code |
