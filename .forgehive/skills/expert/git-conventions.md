# Git Conventions

## Commit Messages (Conventional Commits)

```
<type>(<scope>): <short description>

[optional body — wrap at 72 chars]

[optional footer: BREAKING CHANGE, Closes #123]
```

**Types:**

| Type | When |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `chore` | Build, deps, config (no code change) |
| `refactor` | Code change without feature/fix |
| `test` | Adding/fixing tests |
| `docs` | Documentation only |
| `perf` | Performance improvement |
| `ci` | CI/CD changes |

**Rules:**
- Imperative mood: "add" not "added", "fix" not "fixes"
- No period at end of subject line
- Subject ≤ 72 chars
- Body explains WHY, not what (the diff shows what)

## Branch Naming

```
feat/user-authentication
fix/login-redirect-loop
chore/upgrade-dependencies
refactor/extract-auth-middleware
```

## Workflow

```
main (protected)
  └── feat/my-feature
        ├── commit: feat: add login form
        ├── commit: test: add login form tests
        └── PR → squash merge to main
```

- Work on feature branches — never directly on `main`
- Keep branches short-lived (days, not weeks)
- Squash merge for features, merge commit for releases

## Good Commit Habits

- One logical change per commit (atomic)
- Green tests before committing
- `git add -p` for partial staging when you changed multiple things
- `git commit --fixup` for small corrections before push

## PR Description Template

```markdown
## What
[1-2 sentences: what changed]

## Why
[motivation, ticket link]

## Test Plan
- [ ] Unit tests added/updated
- [ ] Smoke tested locally
```

## Anti-Patterns

| Avoid | Why |
|---|---|
| "WIP", "fix", "asdf" commits | Meaningless history |
| 1000-line commits | Hard to review, hard to bisect |
| `git push --force` on shared branches | Rewrites shared history |
| Committing generated files | Noise, conflicts |
| Committing secrets | Permanent in history |
