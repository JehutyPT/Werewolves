#!/usr/bin/env bash
set -euo pipefail

SCRIPT_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

LOG_FILE=$TMP/gh.log
OUT_FILE=$TMP/out.txt
ERR_FILE=$TMP/err.txt
export GH_LOG_FILE=$LOG_FILE

cat > "$TMP/gh" <<'GH'
#!/usr/bin/env bash
set -euo pipefail

contains_arg() {
  local expected=$1
  shift

  local arg
  for arg in "$@"; do
    if [ "$arg" = "$expected" ]; then
      return 0
    fi
  done

  return 1
}

arg_after() {
  local expected=$1
  shift

  while [ $# -gt 0 ]; do
    if [ "$1" = "$expected" ]; then
      printf '%s' "$2"
      return 0
    fi
    shift
  done

  return 1
}

if [ "$1" = "repo" ] && [ "$2" = "view" ]; then
  if contains_arg '.owner.login' "$@"; then
    echo "example-owner"
  else
    echo "example-repo"
  fi
  exit 0
fi

if [ "$1" = "api" ] && [ "$2" = "graphql" ]; then
  query=$(arg_after -f "$@")

  if contains_arg -q "$@"; then
    echo "NODE_${FAKE_NODE_NUMBER:-123}"
    exit 0
  fi

  if contains_arg --jq "$@"; then
    printf '%s' "${FAKE_BLOCKERS:-}"
    exit 0
  fi

  case "$query" in
    *addBlockedBy*)
      echo "add-blocked-by" >> "$GH_LOG_FILE"
      ;;
    *removeBlockedBy*)
      echo "remove-blocked-by" >> "$GH_LOG_FILE"
      ;;
    *)
      echo "unexpected GraphQL query" >&2
      exit 2
      ;;
  esac

  echo '{}'
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "view" ]; then
  if contains_arg state "$@" || contains_arg '.state' "$@"; then
    echo "${FAKE_ISSUE_STATE:-OPEN}"
  else
    echo "${FAKE_HAS_READY:-false}"
  fi
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "create" ]; then
  echo "create-issue" >> "$GH_LOG_FILE"
  echo "https://github.com/example-owner/example-repo/issues/123"
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "edit" ]; then
  issue_number=$3

  if contains_arg --add-label "$@"; then
    label=$(arg_after --add-label "$@")
    echo "add-label:$issue_number:$label" >> "$GH_LOG_FILE"
  fi

  if contains_arg --remove-label "$@"; then
    label=$(arg_after --remove-label "$@")
    echo "remove-label:$issue_number:$label" >> "$GH_LOG_FILE"
  fi

  if contains_arg --body "$@"; then
    body=$(arg_after --body "$@")
    echo "edit-body:$issue_number:$body" >> "$GH_LOG_FILE"
  fi

  exit 0
fi

echo "unexpected gh args: $*" >&2
exit 2
GH
chmod +x "$TMP/gh"

reset_case() {
  rm -f "$LOG_FILE" "$OUT_FILE" "$ERR_FILE"
  unset FAKE_BLOCKERS FAKE_HAS_READY FAKE_ISSUE_STATE FAKE_NODE_NUMBER
}

fail() {
  echo "FAIL $1"
  if [ -s "$OUT_FILE" ]; then
    echo "stdout:"
    cat "$OUT_FILE"
  fi
  if [ -s "$ERR_FILE" ]; then
    echo "stderr:"
    cat "$ERR_FILE"
  fi
  if [ -s "$LOG_FILE" ]; then
    echo "gh operations:"
    cat "$LOG_FILE"
  fi
  exit 1
}

assert_log_exact() {
  local expected=$1
  local name=$2

  if ! cmp -s "$LOG_FILE" <(printf '%s' "$expected"); then
    fail "$name: unexpected GitHub mutations"
  fi
}

reset_case
if PATH="$TMP:$PATH" "$SCRIPT_ROOT/create-issue.sh" \
  "Title" "Body" feature ready-for-agent > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "create rejects ready-for-agent: command succeeded"
fi
if [ -e "$LOG_FILE" ]; then
  fail "create rejects ready-for-agent: issue was created"
fi
if ! grep -qi 'ready-for-agent.*creation' "$ERR_FILE"; then
  fail "create rejects ready-for-agent: missing guidance"
fi
echo "PASS create rejects ready-for-agent until body and relationships exist"

reset_case
export FAKE_BLOCKERS=$'41 CLOSED\n42 OPEN\n'
if PATH="$TMP:$PATH" "$SCRIPT_ROOT/add-label.sh" \
  123 ready-for-agent > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "ready label rejects open blockers: command succeeded"
fi
if [ -e "$LOG_FILE" ]; then
  fail "ready label rejects open blockers: label was added"
fi
if ! grep -q '#42' "$ERR_FILE"; then
  fail "ready label rejects open blockers: missing blocker number"
fi
echo "PASS ready-for-agent is refused while an open blocker exists"

reset_case
export FAKE_HAS_READY=true
PATH="$TMP:$PATH" "$SCRIPT_ROOT/add-blocked-by.sh" \
  123 42 > "$OUT_FILE" 2> "$ERR_FILE" \
  || fail "adding blocker invalidates readiness: command failed"
assert_log_exact $'add-blocked-by\nremove-label:123:ready-for-agent\n' \
  "adding blocker invalidates readiness"
echo "PASS blocker relationship is added before readiness is removed"

reset_case
PATH="$TMP:$PATH" "$SCRIPT_ROOT/edit-issue-body.sh" \
  123 "Replacement body" > "$OUT_FILE" 2> "$ERR_FILE" \
  || fail "body edit invalidates readiness: command failed"
assert_log_exact $'remove-label:123:ready-for-agent\nedit-body:123:Replacement body\n' \
  "body edit invalidates readiness"
echo "PASS generic body edits invalidate readiness"

reset_case
PATH="$TMP:$PATH" "$SCRIPT_ROOT/remove-blocked-by.sh" \
  123 42 > "$OUT_FILE" 2> "$ERR_FILE" \
  || fail "removing blocker does not promote: command failed"
assert_log_exact $'remove-blocked-by\n' \
  "removing blocker does not promote"
if ! grep -qi 'prepare.*contract' "$OUT_FILE"; then
  fail "removing blocker does not promote: missing preparation guidance"
fi
echo "PASS removing the last blocker never promotes without preparation"
