#!/usr/bin/env bash
set -euo pipefail

# Marks issue as blocked by blocker via addBlockedBy.
# Usage: add-blocked-by.sh <issue_number> <blocker_number>

if [ $# -ne 2 ]; then
  echo "Usage: add-blocked-by.sh <issue_number> <blocker_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
BLOCKER_NUMBER=$2
SCRIPTS_DIR="$(dirname "$0")"

ISSUE_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$ISSUE_NUMBER")
BLOCKER_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$BLOCKER_NUMBER")

gh api graphql -f query='
  mutation {
    addBlockedBy(input: {issueId: "'"$ISSUE_NODE_ID"'", blockingIssueId: "'"$BLOCKER_NODE_ID"'"}) {
      issue { number }
      blockingIssue { number }
    }
  }'

echo "Issue #$ISSUE_NUMBER is now blocked by #$BLOCKER_NUMBER"

HAS_READY=$(gh issue view "$ISSUE_NUMBER" --json labels --jq '.labels | any(.name == "ready-for-agent")')
if [ "$HAS_READY" = "true" ]; then
  gh issue edit "$ISSUE_NUMBER" --remove-label ready-for-agent
  echo "Removed ready-for-agent from issue #$ISSUE_NUMBER; its implementation contract is provisional while blocked."
fi
