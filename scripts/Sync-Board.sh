#!/usr/bin/env bash
# Reconcile GitHub Projects (project 2, @daycharles's PropFlow) with issue state.
#
#   Done        merged PR                (issue closed)
#   In review   open PR into develop
#   In progress branch / assigned, no PR (issue open)
#
# Needs `gh` authed with the `project` scope. Usage:
#   scripts/Sync-Board.sh done 32 33 35
#   scripts/Sync-Board.sh "in progress" 43
#   scripts/Sync-Board.sh "in review" 39 40
set -euo pipefail

OWNER=daycharles
PROJECT=2
COLUMN=${1:?"first arg is the column: done | in review | in progress | ready | backlog"}
shift
[ "$#" -gt 0 ] || { echo "give at least one issue number" >&2; exit 1; }

pid=$(gh project view "$PROJECT" --owner "$OWNER" --format json --jq '.id')
field=$(gh project field-list "$PROJECT" --owner "$OWNER" --format json --jq '.fields[]|select(.name=="Status")|.id')
option=$(gh project field-list "$PROJECT" --owner "$OWNER" --format json \
  --jq ".fields[]|select(.name==\"Status\")|.options[]|select((.name|ascii_downcase)==\"$(echo "$COLUMN" | tr '[:upper:]' '[:lower:]')\")|.id")
[ -n "$option" ] || { echo "no Status option matching '$COLUMN'" >&2; exit 1; }

for n in "$@"; do
  id=$(gh project item-list "$PROJECT" --owner "$OWNER" --limit 400 --format json --jq ".items[]|select(.content.number==$n)|.id")
  if [ -z "$id" ]; then
    gh project item-add "$PROJECT" --owner "$OWNER" --url "https://github.com/$OWNER/PropFlow/issues/$n" >/dev/null
    id=$(gh project item-list "$PROJECT" --owner "$OWNER" --limit 400 --format json --jq ".items[]|select(.content.number==$n)|.id")
  fi
  gh project item-edit --project-id "$pid" --id "$id" --field-id "$field" --single-select-option-id "$option" >/dev/null
  echo "#$n -> $COLUMN"
done
