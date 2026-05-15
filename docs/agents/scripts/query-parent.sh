#!/usr/bin/env bash
set -euo pipefail

# Prints the parent issue number, or nothing if no parent.
# Usage: query-parent.sh <issue_number>

if [ $# -ne 1 ]; then
  echo "Usage: query-parent.sh <issue_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
SCRIPTS_DIR="$(dirname "$0")"

NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$ISSUE_NUMBER")

PARENT_NUMBER=$(gh api graphql \
  -f query='
  query {
    node(id: "'"$NODE_ID"'") {
      ... on Issue {
        parent { number }
      }
    }
  }' --jq '.data.node.parent.number // empty')

if [ -n "$PARENT_NUMBER" ]; then
  echo "$PARENT_NUMBER"
fi
