#!/usr/bin/env bash
set -euo pipefail

# Reads a GitHub issue with comments, outputting structured JSON.
# Usage: read-issue.sh <issue_number>

if [ $# -ne 1 ]; then
  echo "Usage: read-issue.sh <issue_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1

gh issue view "$ISSUE_NUMBER" --comments \
  --json number,title,body,state,labels,milestone,comments \
  --jq '{number, title, body, state, labels: [.labels[].name], milestone: .milestone.title, comments: [.comments[].body]}'
