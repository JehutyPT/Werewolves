#!/usr/bin/env bash
set -euo pipefail

# Lists GitHub issues with structured JSON output.
# Usage: list-issues.sh [gh-flags...]
#
# All arguments are forwarded to `gh issue list`. Common flags:
#   --label <name>       Filter by label (repeatable)
#   --state <state>      open (default), closed, all
#   --milestone <name>   Filter by milestone
#   --limit <n>          Max results (use 200 for large sets)
#   --search <query>     GitHub search qualifiers (e.g. "no:blocked-by")
#
# Examples:
#   list-issues.sh --label "ready-for-agent"
#   list-issues.sh --label "ready-for-agent" --milestone "v1"
#   list-issues.sh --label "ready-for-agent" --search "no:blocked-by"
#   list-issues.sh --state all --limit 200

gh issue list \
  --json number,title,body,labels,comments \
  --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]' \
  "$@"
