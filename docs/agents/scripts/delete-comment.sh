#!/usr/bin/env bash
set -euo pipefail

# Deletes a comment from a GitHub issue by comment ID.
# Usage: delete-comment.sh <comment_id>
#
# The comment_id is the numeric ID returned by add-comment.sh.

if [ $# -ne 1 ]; then
  echo "Usage: delete-comment.sh <comment_id>" >&2
  exit 1
fi

COMMENT_ID=$1

OWNER=$(gh repo view --json owner -q '.owner.login')
REPO=$(gh repo view --json name -q '.name')

gh api -X DELETE "repos/$OWNER/$REPO/issues/comments/$COMMENT_ID"

echo "Comment $COMMENT_ID deleted."
