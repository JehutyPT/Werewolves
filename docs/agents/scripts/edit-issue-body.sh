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

BODY_FILE=$(mktemp)
trap 'rm -f "$BODY_FILE"' EXIT

printf '%s' "$BODY" > "$BODY_FILE"

gh issue edit "$ISSUE_NUMBER" --body-file "$BODY_FILE" --remove-label ready-for-agent

echo "Issue #$ISSUE_NUMBER body updated; ready-for-agent was invalidated if present."
echo "Prepare and validate the current implementation contract before adding ready-for-agent again."
