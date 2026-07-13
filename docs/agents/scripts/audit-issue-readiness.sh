#!/usr/bin/env bash
set -euo pipefail

MODE=${1:-}
REPO=${REPO:-${GITHUB_REPOSITORY:-}}
EVENT_PATH=${GITHUB_EVENT_PATH:-}

usage() {
  echo "Usage: audit-issue-readiness.sh <edited|closed|audit>" >&2
}

require_repo() {
  if [ -z "$REPO" ]; then
    echo "Error: REPO or GITHUB_REPOSITORY is required." >&2
    exit 2
  fi
}

normalize_task_checkbox_states() {
  perl -pe '
    BEGIN {
      $in_contract = 0;
      $in_acceptance = 0;
      $fence_char = "";
      $fence_length = 0;
    }

    if ($fence_char eq "") {
      if (/^[ \t]*(`{3,}|~{3,})/) {
        $fence_char = substr($1, 0, 1);
        $fence_length = length($1);
      } else {
        if (/^##[ \t]+Implementation Contract[ \t]*\r?$/) {
          $in_contract = 1;
          $in_acceptance = 0;
        } elsif ($in_contract && /^#{1,2}[ \t]+/) {
          $in_contract = 0;
          $in_acceptance = 0;
        } elsif ($in_contract && /^###[ \t]+Acceptance criteria[ \t]*\r?$/) {
          $in_acceptance = 1;
        } elsif ($in_acceptance && /^#{1,3}[ \t]+/) {
          $in_acceptance = 0;
        }

        if ($in_acceptance) {
          s/^([ \t]*- )\[[ xX]\] /${1}[ ] /;
        }
      }
    } elsif (/^[ \t]*(`+|~+)[ \t]*\r?$/) {
      my $marker = $1;
      if (substr($marker, 0, 1) eq $fence_char
          && length($marker) >= $fence_length) {
        $fence_char = "";
        $fence_length = 0;
      }
    }
  '
}

audit_edited_issue() {
  require_repo
  if [ -z "$EVENT_PATH" ] || [ ! -f "$EVENT_PATH" ]; then
    echo "Error: GITHUB_EVENT_PATH must name the edited issue event payload." >&2
    exit 2
  fi

  local has_ready
  has_ready=$(jq -r '.issue.labels | any(.name == "ready-for-agent")' "$EVENT_PATH")
  if [ "$has_ready" != "true" ] \
    || ! jq -e '.changes | has("body")' "$EVENT_PATH" >/dev/null; then
    return
  fi

  local temp_dir
  temp_dir=$(mktemp -d)
  jq -j '.changes.body.from // ""' "$EVENT_PATH" \
    | normalize_task_checkbox_states > "$temp_dir/previous"
  jq -j '.issue.body // ""' "$EVENT_PATH" \
    | normalize_task_checkbox_states > "$temp_dir/current"
  if cmp -s "$temp_dir/previous" "$temp_dir/current"; then
    rm -rf "$temp_dir"
    return
  fi
  rm -rf "$temp_dir"

  local issue_number
  issue_number=$(jq -r '.issue.number' "$EVENT_PATH")
  echo "#$issue_number had a material body edit; removing ready-for-agent"
  gh issue edit "$issue_number" --repo "$REPO" --remove-label ready-for-agent
}

blocking_pages() {
  local issue_number=$1
  local owner=${REPO%%/*}
  local name=${REPO#*/}

  # GraphQL variables intentionally remain literal inside the quoted query.
  # shellcheck disable=SC2016
  gh api graphql \
    --paginate \
    --slurp \
    -f owner="$owner" \
    -f name="$name" \
    -F number="$issue_number" \
    -f query='
      query($owner: String!, $name: String!, $number: Int!, $endCursor: String) {
        repository(owner: $owner, name: $name) {
          issue(number: $number) {
            blocking(first: 100, after: $endCursor) {
              nodes { number state }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
      }'
}

blocked_by_pages() {
  local issue_number=$1
  local owner=${REPO%%/*}
  local name=${REPO#*/}

  # GraphQL variables intentionally remain literal inside the quoted query.
  # shellcheck disable=SC2016
  gh api graphql \
    --paginate \
    --slurp \
    -f owner="$owner" \
    -f name="$name" \
    -F number="$issue_number" \
    -f query='
      query($owner: String!, $name: String!, $number: Int!, $endCursor: String) {
        repository(owner: $owner, name: $name) {
          issue(number: $number) {
            blockedBy(first: 100, after: $endCursor) {
              nodes { number state }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
      }'
}

audit_closed_issue() {
  require_repo
  if [ -z "$EVENT_PATH" ] || [ ! -f "$EVENT_PATH" ]; then
    echo "Error: GITHUB_EVENT_PATH must name the closed issue event payload." >&2
    exit 2
  fi

  local issue_number
  issue_number=$(jq -r '.issue.number' "$EVENT_PATH")

  local has_ready
  has_ready=$(jq -r '.issue.labels | any(.name == "ready-for-agent")' "$EVENT_PATH")
  if [ "$has_ready" = "true" ]; then
    echo "#$issue_number is closed; removing ready-for-agent"
    gh issue edit "$issue_number" --repo "$REPO" --remove-label ready-for-agent
  fi

  local pages
  pages=$(blocking_pages "$issue_number")
  local dependent_numbers
  dependent_numbers=$(jq -r '[
      .[].data.repository.issue.blocking.nodes[]
      | select(.state == "OPEN")
      | .number
    ] | unique | .[]' <<< "$pages")

  local dependent_number
  while read -r dependent_number; do
    [ -n "$dependent_number" ] || continue
    local dependent_has_ready
    dependent_has_ready=$(gh issue view "$dependent_number" --repo "$REPO" \
      --json labels --jq '.labels | any(.name == "ready-for-agent")')
    if [ "$dependent_has_ready" != "true" ]; then
      continue
    fi
    echo "#$dependent_number depended on closed blocker #$issue_number; removing ready-for-agent"
    gh issue edit "$dependent_number" --repo "$REPO" --remove-label ready-for-agent
  done <<< "$dependent_numbers"
}

audit_ready_issues() {
  require_repo

  local ready_issues
  ready_issues=$(gh issue list --repo "$REPO" --state all --label ready-for-agent \
    --limit 1000 --json number,state \
    --jq '.[] | "\(.number) \(.state)"')

  local number
  local state
  while read -r number state; do
    [ -n "$number" ] || continue
    if [ "$state" = "CLOSED" ]; then
      echo "#$number is closed; removing ready-for-agent"
      gh issue edit "$number" --repo "$REPO" --remove-label ready-for-agent
      continue
    fi

    local pages
    pages=$(blocked_by_pages "$number")
    local blocker_numbers
    blocker_numbers=$(jq -r '[
        .[].data.repository.issue.blockedBy.nodes[]
        | select(.state == "OPEN")
        | ("#" + (.number | tostring))
      ] | unique | join(", ")' <<< "$pages")
    if [ -z "$blocker_numbers" ]; then
      continue
    fi

    echo "#$number has open blocker(s) $blocker_numbers; removing ready-for-agent"
    gh issue edit "$number" --repo "$REPO" --remove-label ready-for-agent
  done <<< "$ready_issues"

  echo "Audit complete. Native relationships were retained; no issue was promoted."
}

case "$MODE" in
  edited)
    audit_edited_issue
    ;;
  closed)
    audit_closed_issue
    ;;
  audit)
    audit_ready_issues
    ;;
  *)
    usage
    exit 2
    ;;
esac
