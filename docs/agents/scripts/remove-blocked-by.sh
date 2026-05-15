#!/usr/bin/env bash
set -euo pipefail

# Removes blocking relationship via removeBlockedBy.
# Usage: remove-blocked-by.sh <issue_number> <blocker_number>

if [ $# -ne 2 ]; then
  echo "Usage: remove-blocked-by.sh <issue_number> <blocker_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
BLOCKER_NUMBER=$2
SCRIPTS_DIR="$(dirname "$0")"

ISSUE_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$ISSUE_NUMBER")
BLOCKER_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$BLOCKER_NUMBER")

gh api graphql -f query='
  mutation {
    removeBlockedBy(input: {issueId: "'"$ISSUE_NODE_ID"'", blockingIssueId: "'"$BLOCKER_NODE_ID"'"}) {
      issue { number }
    }
  }'

echo "Issue #$ISSUE_NUMBER is no longer blocked by #$BLOCKER_NUMBER"
