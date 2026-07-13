#!/usr/bin/env bash
set -euo pipefail

SCRIPT_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

BODY_FILE=$TMP/body.md
EDIT_FILE=$TMP/edited.md
ERR_FILE=$TMP/err.txt
export GH_BODY_FILE=$BODY_FILE
export GH_EDIT_FILE=$EDIT_FILE

cat > "$TMP/gh" <<'GH'
#!/usr/bin/env bash
set -euo pipefail

if [ "$1" = "issue" ] && [ "$2" = "view" ]; then
  cat "$GH_BODY_FILE"
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "edit" ]; then
  shift 3
  body_set=false
  body=""

  while [ $# -gt 0 ]; do
    case "$1" in
      --body)
        body=$2
        body_set=true
        shift 2
        ;;
      *)
        shift
        ;;
    esac
  done

  if [ "$body_set" = false ]; then
    echo "missing --body" >&2
    exit 2
  fi

  printf '%s' "$body" > "$GH_EDIT_FILE"
  exit 0
fi

echo "unexpected gh args: $*" >&2
exit 2
GH
chmod +x "$TMP/gh"

run_success() {
  local name=$1
  local script=$2
  local body=$3
  local text=$4
  local expected=$5

  printf -v body '%s\n%s\n%s' \
    $'## Implementation Contract\n\n### Acceptance criteria' \
    "$body" \
    $'\n### Scope boundaries\nIn scope.'
  printf -v expected '%s\n%s\n%s' \
    $'## Implementation Contract\n\n### Acceptance criteria' \
    "$expected" \
    $'\n### Scope boundaries\nIn scope.'

  printf '%s' "$body" > "$BODY_FILE"
  rm -f "$EDIT_FILE" "$ERR_FILE"

  PATH="$TMP:$PATH" "$SCRIPT_ROOT/$script" 123 "$text" > /dev/null 2> "$ERR_FILE"

  if ! cmp -s "$EDIT_FILE" <(printf '%s' "$expected"); then
    echo "FAIL $name"
    echo "stderr:"
    cat "$ERR_FILE"
    echo "expected bytes:"
    printf '%s' "$expected" | od -An -tx1
    echo "actual bytes:"
    od -An -tx1 "$EDIT_FILE"
    exit 1
  fi

  echo "PASS $name"
}

run_failure() {
  local name=$1
  local script=$2
  local body=$3
  local text=$4

  printf -v body '%s\n%s\n%s' \
    $'## Implementation Contract\n\n### Acceptance criteria' \
    "$body" \
    $'\n### Scope boundaries\nIn scope.'

  printf '%s' "$body" > "$BODY_FILE"
  rm -f "$EDIT_FILE" "$ERR_FILE"

  if PATH="$TMP:$PATH" "$SCRIPT_ROOT/$script" 123 "$text" > /dev/null 2> "$ERR_FILE"; then
    echo "FAIL $name: command succeeded unexpectedly"
    exit 1
  fi

  if [ -e "$EDIT_FILE" ]; then
    echo "FAIL $name: edited issue body despite failure"
    exit 1
  fi

  echo "PASS $name"
}

run_success \
  "mark exact not prefix" \
  mark-criterion-complete.sh \
  $'- [ ] Foo\n- [ ] Foobar' \
  "Foo" \
  $'- [x] Foo\n- [ ] Foobar'

run_success \
  "unmark exact not prefix" \
  unmark-criterion-complete.sh \
  $'- [x] Foo\n- [x] Foobar' \
  "Foo" \
  $'- [ ] Foo\n- [x] Foobar'

run_success \
  "regex and replacement metacharacters are literal" \
  mark-criterion-complete.sh \
  '- [ ] a&b/c\d .[*^$ ]' \
  'a&b/c\d .[*^$ ]' \
  '- [x] a&b/c\d .[*^$ ]'

run_failure \
  "inline non-task suffix is not toggled" \
  mark-criterion-complete.sh \
  "Note: - [ ] Foo" \
  "Foo"

run_success \
  "nested indentation is preserved" \
  mark-criterion-complete.sh \
  "  - [ ] Foo" \
  "Foo" \
  "  - [x] Foo"

run_failure \
  "duplicate unchecked criteria are ambiguous" \
  mark-criterion-complete.sh \
  $'- [ ] Foo\n- [ ] Foo' \
  "Foo"

run_success \
  "wrapped criterion matches normalized logical text" \
  mark-criterion-complete.sh \
  $'- [ ] After rehydration, the Moderator continues from the latest stable\n      Main Phase recovery boundary.' \
  "After rehydration, the Moderator continues from the latest stable Main Phase recovery boundary." \
  $'- [x] After rehydration, the Moderator continues from the latest stable\n      Main Phase recovery boundary.'

run_success \
  "CRLF task line matches and preserves CRLF" \
  mark-criterion-complete.sh \
  $'- [ ] Foo\r\n- [ ] Bar\r' \
  "Foo" \
  $'- [x] Foo\r\n- [ ] Bar\r'

run_success \
  "uppercase checked marker can be unmarked" \
  unmark-criterion-complete.sh \
  "- [X] Foo" \
  "Foo" \
  "- [ ] Foo"

run_failure \
  "wrong state fails without edit" \
  mark-criterion-complete.sh \
  "- [x] Foo" \
  "Foo"

run_failure \
  "task outside acceptance criteria is not a criterion" \
  mark-criterion-complete.sh \
  $'- [ ] Real criterion\n\n### Scope boundaries\n- [ ] Scope note' \
  "Scope note"

run_failure \
  "task in fenced acceptance example is not a criterion" \
  mark-criterion-complete.sh \
  $'```markdown\n- [ ] Example only\n```\n- [ ] Real criterion' \
  "Example only"

run_failure \
  "shorter nested fence does not close an outer fence" \
  mark-criterion-complete.sh \
  $'````markdown\n```markdown\n- [ ] Nested example only\n```\n````\n- [ ] Real criterion' \
  "Nested example only"

run_failure \
  "blockquoted task is not a canonical criterion" \
  mark-criterion-complete.sh \
  $'> - [ ] Quoted example\n- [ ] Real criterion' \
  "Quoted example"

run_failure \
  "numbered task is not a canonical criterion" \
  mark-criterion-complete.sh \
  $'1. [ ] Numbered example\n- [ ] Real criterion' \
  "Numbered example"
