#!/usr/bin/env bash
set -euo pipefail

# Assigns a GitHub issue to a milestone.
# Usage: assign-milestone.sh <issue_number> <milestone_title>

if [ $# -ne 2 ]; then
  echo "Usage: assign-milestone.sh <issue_number> <milestone_title>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
MILESTONE=$2

gh issue edit "$ISSUE_NUMBER" --milestone "$MILESTONE"

echo "Issue #$ISSUE_NUMBER assigned to milestone \"$MILESTONE\"."
