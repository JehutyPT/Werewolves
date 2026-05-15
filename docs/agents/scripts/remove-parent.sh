#!/usr/bin/env bash
set -euo pipefail

# Removes sub-issue relationship via removeSubIssue.
# Usage: remove-parent.sh <child_number> <parent_number>

if [ $# -ne 2 ]; then
  echo "Usage: remove-parent.sh <child_number> <parent_number>" >&2
  exit 1
fi

CHILD_NUMBER=$1
PARENT_NUMBER=$2
SCRIPTS_DIR="$(dirname "$0")"

CHILD_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$CHILD_NUMBER")
PARENT_NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$PARENT_NUMBER")

gh api graphql -f query='
  mutation {
    removeSubIssue(input: {issueId: "'"$PARENT_NODE_ID"'", subIssueId: "'"$CHILD_NODE_ID"'"}) {
      issue { number }
      subIssue { number }
    }
  }'

echo "Issue #$CHILD_NUMBER is no longer a sub-issue of #$PARENT_NUMBER"
