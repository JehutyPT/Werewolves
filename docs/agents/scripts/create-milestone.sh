#!/usr/bin/env bash
set -euo pipefail

# Creates a GitHub milestone.
# Usage: create-milestone.sh <title>
#
# Prints the milestone number to stdout.

if [ $# -ne 1 ]; then
  echo "Usage: create-milestone.sh <title>" >&2
  exit 1
fi

TITLE=$1

OWNER=$(gh repo view --json owner -q '.owner.login')
REPO=$(gh repo view --json name -q '.name')

MILESTONE_NUMBER=$(gh api "repos/$OWNER/$REPO/milestones" \
  -f title="$TITLE" \
  -f state="open" \
  --jq '.number')

echo "$MILESTONE_NUMBER"
