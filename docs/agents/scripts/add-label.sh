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

gh issue edit "$ISSUE_NUMBER" --add-label "$LABEL"

echo "Label \"$LABEL\" added to issue #$ISSUE_NUMBER."
