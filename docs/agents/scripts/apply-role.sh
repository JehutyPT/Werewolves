#!/usr/bin/env bash
set -euo pipefail

# Adds a label to a GitHub issue.
# Usage: apply-role.sh <issue_number> <label>
#
# Map the role name to its label string via docs/agents/triage-labels.md.

if [ $# -ne 2 ]; then
  echo "Usage: apply-role.sh <issue_number> <label>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
LABEL=$2

gh issue edit "$ISSUE_NUMBER" --add-label "$LABEL"

echo "Label \"$LABEL\" applied to issue #$ISSUE_NUMBER."
