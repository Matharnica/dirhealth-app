# Code Review

## Reviewer Mindset

Your job: find issues the author missed, not rewrite their code. Be direct about problems, respectful about everything else.

## What to Check (Priority Order)

### 1. Correctness
- Does it do what it claims?
- Are edge cases handled (null, empty, concurrent)?
- Are errors handled correctly?
- Will this break in production?

### 2. Tests
- Do tests cover the behavior, not just the happy path?
- Would these tests catch a regression?
- Are new behaviors tested?

### 3. Design
- Is the change too large? Should it be split?
- Does it follow existing patterns?
- Are there simpler approaches?
- Are new abstractions justified?

### 4. Security
- User input validated?
- Secrets/PII handled correctly?
- Authorization checked?

### 5. Performance
- N+1 queries?
- Unbounded operations on large data?
- Unnecessary blocking calls?

## Comment Format

```
// Blocking — must fix before merge
[BUG] This will NPE when user has no address — add null check at line 42

// Important — should fix
[DESIGN] This 200-line function is hard to test — consider extracting validateAddress()

// Minor — optional improvement
[NIT] Variable name `d` unclear — consider `durationMs`

// Question — not blocking
[Q] Why stringify here instead of using the json field directly?

// Positive — acknowledge good work
[+] Nice — using Result type here instead of throwing makes the caller explicit
```

## Self-Review Before Submitting

Run through this before opening a PR:
- [ ] Read your own diff top to bottom
- [ ] Does the PR description explain WHY, not just what?
- [ ] Are there debug logs, TODOs, commented-out code to clean up?
- [ ] Is the PR size reviewable? (< 400 lines is the sweet spot)
- [ ] Do all tests pass locally?

## PR Size Guide

| Lines | Status |
|---|---|
| < 100 | Ideal |
| 100–400 | Acceptable |
| 400–800 | Ask to split |
| > 800 | Split — no exceptions |

## Anti-Patterns

| Avoid | Why |
|---|---|
| "LGTM" without reading | Wastes the review |
| Bikeshedding on formatting | Use a linter |
| Rewriting working code | Not your style, their code |
| Vague comments ("fix this") | Be specific |
| Approving to avoid conflict | Technical debt |
