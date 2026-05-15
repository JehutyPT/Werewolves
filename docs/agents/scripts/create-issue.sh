#!/usr/bin/env bash
set -euo pipefail

# Creates a new GitHub issue.
# Usage: create-issue.sh <title> <body> [label...]
#
# Prints the new issue URL. The issue number is the last path segment.

if [ $# -lt 2 ]; then
  echo "Usage: create-issue.sh <title> <body> [label...]" >&2
  exit 1
fi

TITLE=$1
BODY=$2
shift 2

LABEL_ARGS=()
for LABEL in "$@"; do
  LABEL_ARGS+=(--label "$LABEL")
done

gh issue create --title "$TITLE" --body "$BODY" ${LABEL_ARGS[@]+"${LABEL_ARGS[@]}"}
