#!/usr/bin/env bash
set -euo pipefail

# Adds a comment to a GitHub issue.
# Usage: add-comment.sh <issue_number> <body>
#
# Prints the comment ID (numeric) to stdout for use with delete-comment.sh.

if [ $# -ne 2 ]; then
  echo "Usage: add-comment.sh <issue_number> <body>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
BODY=$2

COMMENT_URL=$(gh issue comment "$ISSUE_NUMBER" --body "$BODY" 2>&1)
COMMENT_ID="${COMMENT_URL##*issuecomment-}"

echo "$COMMENT_ID"
