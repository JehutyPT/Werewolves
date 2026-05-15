#!/usr/bin/env bash
set -euo pipefail

# Closes a GitHub issue, optionally with a comment.
# Usage: close-issue.sh <issue_number> [comment]

if [ $# -lt 1 ]; then
  echo "Usage: close-issue.sh <issue_number> [comment]" >&2
  exit 1
fi

ISSUE_NUMBER=$1
COMMENT=${2:-}

if [ -n "$COMMENT" ]; then
  gh issue close "$ISSUE_NUMBER" --comment "$COMMENT"
else
  gh issue close "$ISSUE_NUMBER"
fi

echo "Issue #$ISSUE_NUMBER closed."
