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

# GraphQL variables are intentionally literal inside the query string.
# shellcheck disable=SC2016
gh api graphql \
  --paginate \
  -f nodeId="$NODE_ID" \
  -f query='
  query($nodeId: ID!, $endCursor: String) {
    node(id: $nodeId) {
      ... on Issue {
        blockedBy(first: 100, after: $endCursor) {
          nodes { number state }
          pageInfo { hasNextPage endCursor }
        }
      }
    }
  }' --jq '.data.node.blockedBy.nodes[] | "\(.number) \(.state)"'
