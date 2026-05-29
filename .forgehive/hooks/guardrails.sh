#!/usr/bin/env bash
# ForgeHive Guardrails — PreToolUse hook for Bash
# Receives tool input as JSON on stdin. Exit 1 to block, exit 0 to allow.

INPUT=$(cat 2>/dev/null || echo "{}")
COMMAND=$(node -e "let d='';process.stdin.on('data',c=>d+=c);process.stdin.on('end',()=>{try{process.stdout.write(JSON.parse(d).command||'')}catch{process.stdout.write('')}})" 2>/dev/null <<< "$INPUT" || echo "")

# Secret scanning for staged files (git pre-commit equivalent)
STAGED_FILES=$(git diff --cached --name-only 2>/dev/null || echo "")
SECRET_PATTERNS=("sk-ant-" "ghp_[A-Za-z0-9]{36}" "AKIA[0-9A-Z]{16}" "xoxb-" "-----BEGIN.*PRIVATE KEY")

if [ -n "$STAGED_FILES" ]; then
  for FILE in $STAGED_FILES; do
    if [ -f "$FILE" ]; then
      for PATTERN in "${SECRET_PATTERNS[@]}"; do
        if grep -qE "$PATTERN" "$FILE" 2>/dev/null; then
          echo "🛡 ForgeHive Secret Guard: Mögliches Secret in $FILE ($PATTERN). Commit geblockt."
          exit 1
        fi
      done
    fi
  done
fi

DANGEROUS=("git push --force" "git push -f " "git reset --hard" "rm -rf /" "DROP TABLE" "DROP DATABASE")

for PATTERN in "${DANGEROUS[@]}"; do
  if echo "$COMMAND" | grep -qi "$PATTERN"; then
    echo "🛡 ForgeHive Guardrail: '$PATTERN' geblockt. Erkläre die Absicht und versuche erneut."
    exit 1
  fi
done

exit 0
