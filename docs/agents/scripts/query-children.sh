#!/usr/bin/env bash
set -euo pipefail

# Prints child issue numbers, one per line.
# Usage: query-children.sh <issue_number>

if [ $# -ne 1 ]; then
  echo "Usage: query-children.sh <issue_number>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
SCRIPTS_DIR="$(dirname "$0")"

NODE_ID=$("$SCRIPTS_DIR/resolve-node-id.sh" "$ISSUE_NUMBER")

gh api graphql \
  -f query='
  query {
    node(id: "'"$NODE_ID"'") {
      ... on Issue {
        subIssues(first: 50) {
          nodes { number }
        }
      }
    }
  }' --jq '.data.node.subIssues.nodes[].number'
