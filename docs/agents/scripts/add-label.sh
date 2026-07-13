#!/usr/bin/env bash
set -euo pipefail

# Adds a label to a GitHub issue.
# Usage: add-label.sh <issue_number> <label>

if [ $# -ne 2 ]; then
  echo "Usage: add-label.sh <issue_number> <label>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
LABEL=$2
SCRIPTS_DIR="$(dirname "$0")"

if [ "$LABEL" = "ready-for-agent" ]; then
  ISSUE_STATE=$(gh issue view "$ISSUE_NUMBER" --json state --jq '.state')
  if [ "$ISSUE_STATE" != "OPEN" ]; then
    echo "Error: ready-for-agent can be applied only to an open issue; issue #$ISSUE_NUMBER is $ISSUE_STATE." >&2
    exit 1
  fi

  OPEN_BLOCKERS=$(
    "$SCRIPTS_DIR/query-blockers.sh" "$ISSUE_NUMBER" \
      | awk '$2 == "OPEN" { print $1 }'
  )

  if [ -n "$OPEN_BLOCKERS" ]; then
    echo "Error: cannot add ready-for-agent to issue #$ISSUE_NUMBER while these blockers are open:" >&2
    while IFS= read -r blocker; do
      echo "  - #$blocker" >&2
    done <<< "$OPEN_BLOCKERS"
    echo "Prepare and validate the current implementation contract after the open blockers are resolved." >&2
    exit 1
  fi
fi

gh issue edit "$ISSUE_NUMBER" --add-label "$LABEL"

echo "Label \"$LABEL\" added to issue #$ISSUE_NUMBER."
