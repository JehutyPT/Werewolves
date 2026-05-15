#!/usr/bin/env bash
set -euo pipefail

# Prints blocker issue numbers with state, one per line.
# Format: <number> <state>
# Usage: query-blockers.sh <issue_number>

if [ $# -ne 1 ]; then
  echo "Usage: query-blockers.sh <issue_number>" >&2
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
        blockedBy(first: 50) {
          nodes { number state }
        }
      }
    }
  }' --jq '.data.node.blockedBy.nodes[] | "\(.number) \(.state)"'
