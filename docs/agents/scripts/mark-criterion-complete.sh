#!/usr/bin/env bash
set -euo pipefail

# Checks a checkbox in the issue body matching the criterion text.
# Usage: mark-criterion-complete.sh <issue_number> <criterion_text>

if [ $# -ne 2 ]; then
  echo "Usage: mark-criterion-complete.sh <issue_number> <criterion_text>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
TEXT=$2

BODY=$(gh issue view "$ISSUE_NUMBER" --json body -q '.body')

# Escape regex metacharacters in the criterion text for safe sed matching.
ESCAPED_TEXT=$(printf '%s' "$TEXT" | sed 's/[.[\(*^$+?{|\\]/\\&/g')
# Escape & and \ in replacement portion to prevent sed expansion.
REPL_TEXT=$(printf '%s' "$TEXT" | sed 's/[&\\]/\\&/g')

UPDATED=$(echo "$BODY" | sed "s/- \[ \] ${ESCAPED_TEXT}/- [x] ${REPL_TEXT}/")

if [ "$BODY" = "$UPDATED" ]; then
  echo "Error: no unchecked criterion matching \"$TEXT\" found in issue #$ISSUE_NUMBER" >&2
  exit 1
fi

gh issue edit "$ISSUE_NUMBER" --body "$UPDATED"

echo "Criterion marked complete in issue #$ISSUE_NUMBER"
