#!/usr/bin/env bash
set -euo pipefail

# Unchecks a checkbox in the issue body matching the criterion text.
# Usage: unmark-criterion-complete.sh <issue_number> <criterion_text>

if [ $# -ne 2 ]; then
  echo "Usage: unmark-criterion-complete.sh <issue_number> <criterion_text>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
TEXT=$2

BODY=$(gh issue view "$ISSUE_NUMBER" --json body -q '.body')

D=$'\x01'
ESCAPED_TEXT=$(printf '%s' "$TEXT" | sed 's/[.[*^$\\]/\\&/g')
REPL_TEXT=$(printf '%s' "$TEXT" | sed 's/[&\\/]/\\&/g')

UPDATED=$(echo "$BODY" | sed "s${D}- \[x\] ${ESCAPED_TEXT}\$${D}- [ ] ${REPL_TEXT}${D}")

if [ "$BODY" = "$UPDATED" ]; then
  echo "Error: no checked criterion matching \"$TEXT\" found in issue #$ISSUE_NUMBER" >&2
  exit 1
fi

gh issue edit "$ISSUE_NUMBER" --body "$UPDATED"

echo "Criterion unmarked in issue #$ISSUE_NUMBER"
