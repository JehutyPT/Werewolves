#!/usr/bin/env bash
set -euo pipefail

# Removes a label from a GitHub issue.
# Usage: remove-label.sh <issue_number> <label>

if [ $# -ne 2 ]; then
  echo "Usage: remove-label.sh <issue_number> <label>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
LABEL=$2

gh issue edit "$ISSUE_NUMBER" --remove-label "$LABEL"

echo "Label \"$LABEL\" removed from issue #$ISSUE_NUMBER."
