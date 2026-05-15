#!/usr/bin/env bash
set -euo pipefail

# Replaces the body of a GitHub issue.
# Usage: edit-issue-body.sh <issue_number> <body>

if [ $# -ne 2 ]; then
  echo "Usage: edit-issue-body.sh <issue_number> <body>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
BODY=$2

gh issue edit "$ISSUE_NUMBER" --body "$BODY"

echo "Issue #$ISSUE_NUMBER body updated."
