#!/usr/bin/env bash
set -euo pipefail

# Removes a label from a GitHub issue.
# Usage: remove-role.sh <issue_number> <label>
#
# Map the role name to its label string via docs/agents/triage-labels.md.

if [ $# -ne 2 ]; then
  echo "Usage: remove-role.sh <issue_number> <label>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
LABEL=$2

gh issue edit "$ISSUE_NUMBER" --remove-label "$LABEL"

echo "Label \"$LABEL\" removed from issue #$ISSUE_NUMBER."
