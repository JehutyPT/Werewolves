#!/usr/bin/env bash
set -euo pipefail

# Checks a checkbox in the issue body matching the criterion text.
# Usage: mark-criterion-complete.sh <issue_number> <criterion_text>

if [ $# -ne 2 ]; then
  echo "Usage: mark-criterion-complete.sh <issue_number> <criterion_text>" >&2
  exit 1
fi

ISSUE_NUMBER=$1
TEXT=$2

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
"$SCRIPT_DIR/update-criterion-state.sh" mark "$ISSUE_NUMBER" "$TEXT"
