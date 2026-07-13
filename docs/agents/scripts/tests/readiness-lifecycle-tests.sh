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

  if [ -n "${FAKE_BLOCKING_PAGES:-}" ]; then
    if ! contains_arg --paginate "$@" || ! contains_arg --slurp "$@"; then
      echo "blocking traversal must use gh GraphQL pagination" >&2
      exit 2
    fi
    if [[ "$*" != *'blocking(first: 100, after: $endCursor)'* ]] \
      || [[ "$*" != *'pageInfo { hasNextPage endCursor }'* ]]; then
      echo "blocking traversal query is missing cursor pagination" >&2
      exit 2
    fi
    printf '%s\n' "$FAKE_BLOCKING_PAGES"
    exit 0
  fi

  if [ -n "${FAKE_BLOCKED_BY_PAGES:-}" ]; then
    if ! contains_arg --paginate "$@" || ! contains_arg --slurp "$@"; then
      echo "blockedBy traversal must use gh GraphQL pagination" >&2
      exit 2
    fi
    if [[ "$*" != *'blockedBy(first: 100, after: $endCursor)'* ]] \
      || [[ "$*" != *'pageInfo { hasNextPage endCursor }'* ]]; then
      echo "blockedBy traversal query is missing cursor pagination" >&2
      exit 2
    fi
    printf '%s\n' "$FAKE_BLOCKED_BY_PAGES"
    exit 0
  fi

  if contains_arg -q "$@"; then
    echo "NODE_${FAKE_NODE_NUMBER:-123}"
    exit 0
  fi

  if contains_arg --jq "$@"; then
    if [ -n "${FAKE_BLOCKERS:-}" ]; then
      if ! contains_arg --paginate "$@" \
        || [[ "$*" != *'blockedBy(first: 100, after: $endCursor)'* ]] \
        || [[ "$*" != *'pageInfo { hasNextPage endCursor }'* ]]; then
        echo "blocker query must use cursor pagination" >&2
        exit 2
      fi
    fi
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
    issue_number=$3
    has_ready=${FAKE_HAS_READY:-false}
    for ready_number in ${FAKE_READY_NUMBERS:-}; do
      if [ "$issue_number" = "$ready_number" ]; then
        has_ready=true
      fi
    done
    echo "$has_ready"
  fi
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "list" ]; then
  printf '%s' "${FAKE_READY_ISSUES:-}"
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
  unset FAKE_BLOCKERS FAKE_BLOCKED_BY_PAGES FAKE_BLOCKING_PAGES FAKE_HAS_READY \
    FAKE_ISSUE_STATE FAKE_NODE_NUMBER FAKE_READY_ISSUES FAKE_READY_NUMBERS
}

write_edited_event() {
  local previous_body=$1
  local current_body=$2
  local has_ready=$3
  local labels='[]'

  if [ "$has_ready" = "true" ]; then
    labels='[{"name":"ready-for-agent"}]'
  fi

  jq -n \
    --arg previous_body "$previous_body" \
    --arg current_body "$current_body" \
    --argjson labels "$labels" \
    '{
      action: "edited",
      changes: {body: {from: $previous_body}},
      issue: {number: 123, body: $current_body, labels: $labels}
    }' > "$TMP/event.json"
}

write_closed_event() {
  local has_ready=$1
  local labels='[]'

  if [ "$has_ready" = "true" ]; then
    labels='[{"name":"ready-for-agent"}]'
  fi

  jq -n \
    --argjson labels "$labels" \
    '{action: "closed", issue: {number: 123, labels: $labels}}' \
    > "$TMP/event.json"
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

reset_case
write_edited_event \
  $'## Implementation Contract\n\nOld outcome' \
  $'## Implementation Contract\n\nNew outcome' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "material issue edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "material issue edit invalidates readiness"
echo "PASS material issue-body edits invalidate readiness from the edited event"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n- [ ] First criterion\n  - [X] Nested criterion\n1. [x] Numbered criterion\n\n### Scope boundaries\nIn scope.' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n- [x] First criterion\n  - [ ] Nested criterion\n1. [x] Numbered criterion\n\n### Scope boundaries\nIn scope.' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "checkbox-only issue edit preserves readiness: command failed"
fi
if [ -e "$LOG_FILE" ]; then
  fail "checkbox-only issue edit preserves readiness: readiness was removed"
fi
echo "PASS checkbox-only issue-body edits preserve readiness"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n1. [ ] Numbered example\n- [ ] Real criterion' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n1. [x] Numbered example\n- [ ] Real criterion' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "noncanonical checkbox edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "noncanonical checkbox edit invalidates readiness"
echo "PASS noncanonical task-marker edits invalidate readiness"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n- [ ] Real criterion\n\n### Scope boundaries\n- [ ] Scope note' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n- [ ] Real criterion\n\n### Scope boundaries\n- [x] Scope note' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "out-of-section checkbox edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "out-of-section checkbox edit invalidates readiness"
echo "PASS checkbox edits outside acceptance criteria invalidate readiness"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n```markdown\n- [ ] Example only\n```\n- [ ] Real criterion' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n```markdown\n- [x] Example only\n```\n- [ ] Real criterion' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "fenced checkbox edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "fenced checkbox edit invalidates readiness"
echo "PASS checkbox edits in fenced examples invalidate readiness"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n````markdown\n```markdown\n- [ ] Nested example only\n```\n````\n- [ ] Real criterion' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n````markdown\n```markdown\n- [x] Nested example only\n```\n````\n- [ ] Real criterion' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "nested fenced checkbox edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "nested fenced checkbox edit invalidates readiness"
echo "PASS shorter nested fences do not expose example checkbox edits"

reset_case
write_edited_event \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n> ```markdown\n> - [ ] Quoted example only\n> ```\n- [ ] Real criterion' \
  $'## Implementation Contract\n\n### Acceptance criteria\n\n> ```markdown\n> - [x] Quoted example only\n> ```\n- [ ] Real criterion' \
  true
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "blockquoted fenced checkbox edit invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "blockquoted fenced checkbox edit invalidates readiness"
echo "PASS blockquoted example checkbox edits invalidate readiness"

reset_case
jq -n '{
  action: "edited",
  changes: {body: {from: null}},
  issue: {
    number: 123,
    body: "## Implementation Contract\n\nNew contract",
    labels: [{name: "ready-for-agent"}]
  }
}' > "$TMP/event.json"
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" edited > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "null prior body invalidates readiness: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\n' \
  "null prior body invalidates readiness"
echo "PASS adding a body to an issue with no prior body invalidates readiness"

reset_case
write_closed_event true
export FAKE_READY_NUMBERS='201 203'
export FAKE_BLOCKING_PAGES='[
  {
    "data": {
      "repository": {
        "issue": {
          "blocking": {
            "nodes": [
              {"number": 201, "state": "OPEN"},
              {"number": 202, "state": "CLOSED"}
            ],
            "pageInfo": {"hasNextPage": true, "endCursor": "page-2"}
          }
        }
      }
    }
  },
  {
    "data": {
      "repository": {
        "issue": {
          "blocking": {
            "nodes": [
              {"number": 203, "state": "OPEN"},
              {"number": 204, "state": "OPEN"}
            ],
            "pageInfo": {"hasNextPage": false, "endCursor": null}
          }
        }
      }
    }
  }
]'
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  GITHUB_EVENT_PATH="$TMP/event.json" \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" closed > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "closed blocker invalidates paginated dependents: command failed"
fi
assert_log_exact $'remove-label:123:ready-for-agent\nremove-label:201:ready-for-agent\nremove-label:203:ready-for-agent\n' \
  "closed blocker invalidates paginated dependents"
echo "PASS closing a blocker invalidates every ready open dependent across all retained blocking pages"

reset_case
export FAKE_READY_ISSUES=$'301 OPEN\n302 CLOSED\n'
export FAKE_BLOCKED_BY_PAGES='[
  {
    "data": {
      "repository": {
        "issue": {
          "blockedBy": {
            "nodes": [{"number": 11, "state": "CLOSED"}],
            "pageInfo": {"hasNextPage": true, "endCursor": "page-2"}
          }
        }
      }
    }
  },
  {
    "data": {
      "repository": {
        "issue": {
          "blockedBy": {
            "nodes": [{"number": 12, "state": "OPEN"}],
            "pageInfo": {"hasNextPage": false, "endCursor": null}
          }
        }
      }
    }
  }
]'
if ! PATH="$TMP:$PATH" \
  REPO=example-owner/example-repo \
  "$SCRIPT_ROOT/audit-issue-readiness.sh" audit > "$OUT_FILE" 2> "$ERR_FILE"; then
  fail "scheduled audit paginates blocker checks: command failed"
fi
assert_log_exact $'remove-label:301:ready-for-agent\nremove-label:302:ready-for-agent\n' \
  "scheduled audit paginates blocker checks"
echo "PASS scheduled audit removes invalid readiness through paginated blockedBy checks without promotion or edge mutation"
