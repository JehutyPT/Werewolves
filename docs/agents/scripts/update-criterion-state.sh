#!/usr/bin/env bash
set -euo pipefail

# Updates one Markdown task-list criterion in a GitHub issue body.
# Usage: update-criterion-state.sh <mark|unmark> <issue_number> <criterion_text>

if [ $# -ne 3 ]; then
  echo "Usage: update-criterion-state.sh <mark|unmark> <issue_number> <criterion_text>" >&2
  exit 1
fi

ACTION=$1
ISSUE_NUMBER=$2
TEXT=$3

case "$ACTION" in
  mark)
    REQUIRED_STATE=" "
    STATE_LABEL="unchecked"
    SUCCESS_MESSAGE="Criterion marked complete in issue #$ISSUE_NUMBER"
    ;;
  unmark)
    REQUIRED_STATE="xX"
    STATE_LABEL="checked"
    SUCCESS_MESSAGE="Criterion unmarked in issue #$ISSUE_NUMBER"
    ;;
  *)
    echo "Usage: update-criterion-state.sh <mark|unmark> <issue_number> <criterion_text>" >&2
    exit 1
    ;;
esac

normalize_text() {
  printf '%s' "$1" | awk '
    BEGIN { text = "" }
    {
      text = text (text == "" ? "" : " ") $0
    }
    END {
      gsub(/\r/, " ", text)
      gsub(/\t/, " ", text)
      gsub(/[[:space:]]+/, " ", text)
      sub(/^ /, "", text)
      sub(/ $/, "", text)
      printf "%s", text
    }'
}

toggle_line() {
  local line=$1

  if [[ "$line" =~ $TASK_RE ]]; then
    case "$ACTION" in
      mark)
        printf '%s- [x] %s' "${BASH_REMATCH[1]}" "${BASH_REMATCH[3]}"
        ;;
      unmark)
        printf '%s- [ ] %s' "${BASH_REMATCH[1]}" "${BASH_REMATCH[3]}"
        ;;
    esac
  else
    printf '%s' "$line"
  fi
}

BODY=$(gh issue view "$ISSUE_NUMBER" --json body -q '.body')
TARGET_TEXT=$(normalize_text "$TEXT")

TASK_RE='^([[:blank:]]*)- \[([ xX])\] (.*)$'
CONTINUATION_RE='^[[:blank:]]+(.+)$'
FENCE_OPEN_RE='^[[:blank:]]*(`{3,}|~{3,})'
CONTRACT_HEADING_RE='^##[[:blank:]]+Implementation Contract[[:blank:]]*$'
ACCEPTANCE_HEADING_RE='^###[[:blank:]]+Acceptance criteria[[:blank:]]*$'
UP_TO_TWO_HEADING_RE='^#{1,2}[[:blank:]]+'
UP_TO_THREE_HEADING_RE='^#{1,3}[[:blank:]]+'

LINES=()
if [ -n "$BODY" ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    LINES+=("$line")
  done <<< "$BODY"
fi

MATCH_COUNT=0
MATCH_INDEX=-1
LINE_COUNT=${#LINES[@]}
IN_CONTRACT=false
IN_ACCEPTANCE=false
FENCE_CHAR=""
FENCE_LENGTH=0

i=0
while [ "$i" -lt "$LINE_COUNT" ]; do
  line=${LINES[$i]}
  heading_line=${line%$'\r'}

  if [ -n "$FENCE_CHAR" ]; then
    FENCE_CLOSE_RE="^[[:blank:]]*${FENCE_CHAR}{${FENCE_LENGTH},}[[:blank:]]*$"
    if [[ "$heading_line" =~ $FENCE_CLOSE_RE ]]; then
      FENCE_CHAR=""
      FENCE_LENGTH=0
    fi
    i=$((i + 1))
    continue
  fi

  if [[ "$heading_line" =~ $FENCE_OPEN_RE ]]; then
    marker=${BASH_REMATCH[1]}
    FENCE_CHAR=${marker:0:1}
    FENCE_LENGTH=${#marker}
    i=$((i + 1))
    continue
  fi

  if [[ "$heading_line" =~ $CONTRACT_HEADING_RE ]]; then
    IN_CONTRACT=true
    IN_ACCEPTANCE=false
    i=$((i + 1))
    continue
  fi

  if [ "$IN_CONTRACT" = true ] \
    && [[ "$heading_line" =~ $UP_TO_TWO_HEADING_RE ]]; then
    IN_CONTRACT=false
    IN_ACCEPTANCE=false
    i=$((i + 1))
    continue
  fi

  if [ "$IN_CONTRACT" = true ] \
    && [[ "$heading_line" =~ $ACCEPTANCE_HEADING_RE ]]; then
    IN_ACCEPTANCE=true
    i=$((i + 1))
    continue
  fi

  if [ "$IN_ACCEPTANCE" = true ] \
    && [[ "$heading_line" =~ $UP_TO_THREE_HEADING_RE ]]; then
    IN_ACCEPTANCE=false
    i=$((i + 1))
    continue
  fi

  if [ "$IN_ACCEPTANCE" != true ]; then
    i=$((i + 1))
    continue
  fi

  if [[ "$line" =~ $TASK_RE ]]; then
    state=${BASH_REMATCH[2]}
    criterion_text=${BASH_REMATCH[3]}

    j=$((i + 1))
    while [ "$j" -lt "$LINE_COUNT" ]; do
      next_line=${LINES[$j]}

      if [[ "$next_line" =~ $TASK_RE ]]; then
        break
      fi

      if [[ "$next_line" =~ $CONTINUATION_RE ]]; then
        criterion_text+=$'\n'"${BASH_REMATCH[1]}"
        j=$((j + 1))
        continue
      fi

      break
    done

    if [ "$(normalize_text "$criterion_text")" = "$TARGET_TEXT" ]; then
      if { [ "$ACTION" = "mark" ] && [ "$state" = "$REQUIRED_STATE" ]; } ||
         { [ "$ACTION" = "unmark" ] && [[ "$state" == [$REQUIRED_STATE] ]]; }; then
        MATCH_COUNT=$((MATCH_COUNT + 1))
        MATCH_INDEX=$i
      fi
    fi
  fi

  i=$((i + 1))
done

if [ "$MATCH_COUNT" -eq 0 ]; then
  echo "Error: no $STATE_LABEL criterion matching \"$TEXT\" found in issue #$ISSUE_NUMBER" >&2
  exit 1
fi

if [ "$MATCH_COUNT" -gt 1 ]; then
  echo "Error: multiple $STATE_LABEL criteria matching \"$TEXT\" found in issue #$ISSUE_NUMBER" >&2
  exit 1
fi

LINES[MATCH_INDEX]=$(toggle_line "${LINES[MATCH_INDEX]}")

UPDATED=""
if [ "${#LINES[@]}" -gt 0 ]; then
  printf -v UPDATED '%s\n' "${LINES[@]}"
  UPDATED=${UPDATED%$'\n'}
fi

gh issue edit "$ISSUE_NUMBER" --body "$UPDATED"

echo "$SUCCESS_MESSAGE"
