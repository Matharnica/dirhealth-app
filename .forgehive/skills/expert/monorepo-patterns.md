# Monorepo Patterns

## When to Use a Monorepo

Use when packages share code, have coordinated releases, or need atomic cross-package changes. Don't use just because you have multiple services — separate repos are fine for fully independent teams.

## Structure

```
packages/
  core/          ← shared domain types, utilities
  api/           ← backend service
  web/           ← frontend app
  cli/           ← command-line tool
apps/            ← deployable applications (thin, import from packages)
tools/           ← internal scripts, generators
package.json     ← workspaces config
turbo.json       ← build orchestration (if using Turborepo)
```

## Workspace Setup (npm/pnpm)

```json
// root package.json
{
  "workspaces": ["packages/*", "apps/*"],
  "scripts": {
    "build": "turbo build",
    "test": "turbo test",
    "lint": "turbo lint"
  }
}
```

## Package Boundaries

```typescript
// packages/core/index.ts — explicit public API
export type { User, Order, Money };
export { parseConfig } from "./config.ts";
// Don't export internal helpers

// packages/api/src/service.ts
import { User } from "@company/core"; // ✅ use workspace package
import { helper } from "../../../core/src/internal"; // ❌ bypass boundary
```

## Dependency Management

- Hoist shared dev dependencies to root
- Keep runtime deps in each package (visibility)
- Use `workspace:*` protocol for internal deps

```json
// packages/api/package.json
{
  "dependencies": {
    "@company/core": "workspace:*",
    "express": "^4.18"
  }
}
```

## Build Order

Turborepo / Nx handle this with task graphs:
```json
// turbo.json
{
  "pipeline": {
    "build": { "dependsOn": ["^build"], "outputs": ["dist/**"] },
    "test": { "dependsOn": ["build"] }
  }
}
```

## CI Strategy

- Cache build artifacts by content hash
- Run only affected packages on PR: `turbo run test --filter=[HEAD^1]`
- Full build on merge to main

## Anti-Patterns

| Avoid | Why |
|---|---|
| Circular package dependencies | Build order impossible |
| Importing internal paths across packages | Bypasses public API |
| One giant `packages/shared` | Becomes a dumping ground |
| Versioning each package independently | Defeats coordination benefits |
| No build cache | CI takes 20min for every change |
