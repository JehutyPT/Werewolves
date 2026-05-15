#!/usr/bin/env bash
set -euo pipefail

# Prints the GraphQL node ID for a given issue number.
# Usage: resolve-node-id.sh <issue_number>

if [ $# -ne 1 ]; then
  echo "Usage: resolve-node-id.sh <issue_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
OWNER=$(gh repo view --json owner -q '.owner.login')
REPO=$(gh repo view --json name -q '.name')

NODE_ID=$(gh api graphql \
  -f query="{ repository(owner:\"$OWNER\", name:\"$REPO\") { issue(number: $ISSUE_NUMBER) { id } } }" \
  -q '.data.repository.issue.id')

if [ -z "$NODE_ID" ] || [ "$NODE_ID" = "null" ]; then
  echo "Error: could not resolve node ID for issue #$ISSUE_NUMBER" >&2
  exit 1
fi

echo "$NODE_ID"
