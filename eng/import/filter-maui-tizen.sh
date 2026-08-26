#!/usr/bin/env bash
#
# filter-maui-tizen.sh — produce a Tizen-only, history-preserving filtered clone of dotnet/maui.
#
# This script is deterministic and re-runnable: given the same source refs it always
# produces the same rewritten history. It performs the *raw* import only — it does not
# move, rename, or normalize any path. Normalization is a deliberately separate commit
# so reviewers can audit the mechanical rewrite independently of our editorial choices.
#
# Output: a bare repository at $WORK_DIR/maui-tizen-filtered.git containing only commits
# that touch Tizen paths, with original authors, dates, messages, and licence history intact.
#
# Usage:
#   eng/import/filter-maui-tizen.sh [--source <path-or-url>] [--work <dir>] [--force]
#
# Environment overrides:
#   MAUI_SOURCE   default: https://github.com/dotnet/maui.git (a local clone is much faster)
#   WORK_DIR      default: <repo>/artifacts/import
#
# Requirements: git >= 2.24, python3 >= 3.6. git-filter-repo is vendored alongside this
# script, so no system-wide installation is needed.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

MAUI_SOURCE="${MAUI_SOURCE:-https://github.com/dotnet/maui.git}"
WORK_DIR="${WORK_DIR:-$REPO_ROOT/artifacts/import}"
FORCE=0

# Baseline commits to retain. These are pinned SHAs, NOT branch names, and that is
# deliberate: `origin/net11.0` moves daily (it advanced from ee4d06cde6 to bedd1b18b7
# during the course of writing this import), so resolving a branch name would make the
# import non-reproducible. Both pins are recorded in eng/baselines.json.
#
#   SOURCE_COMMIT      — forward source baseline (dotnet/maui net11.0 @ 2026-08-18).
#                        Approved by the migration coordinator. Contains PR #36657.
#   SOURCE_TAG_COMMIT  — the last published behaviour baseline (tag 9.0.120), and the
#                        ONLY ref that still carries src/Compatibility Tizen sources,
#                        which were deleted upstream on net11.0.
SOURCE_COMMIT="${SOURCE_COMMIT:-ee4d06cde6b49e297631b08426a33fb34f3152ef}"
SOURCE_TAG_COMMIT="${SOURCE_TAG_COMMIT:-c1f4f7d879f6126029009902289efd6a4bb1bda9}"
SOURCE_TAG="${SOURCE_TAG:-9.0.120}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source) MAUI_SOURCE="$2"; shift 2 ;;
    --work)   WORK_DIR="$2";   shift 2 ;;
    --force)  FORCE=1;         shift   ;;
    -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

MIRROR="$WORK_DIR/maui-mirror.git"
FILTERED="$WORK_DIR/maui-tizen-filtered.git"
PATHS_FILE="$SCRIPT_DIR/tizen-paths.txt"
FILTER_REPO="$SCRIPT_DIR/git-filter-repo"

# Branch name the filtered history is published under. Deliberately not "net11.0":
# this is our import lineage, not a mirror of an upstream branch.
IMPORT_BRANCH="${IMPORT_BRANCH:-maui-tizen-import}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

[[ -f "$PATHS_FILE"  ]] || { echo "missing $PATHS_FILE" >&2; exit 1; }
[[ -f "$FILTER_REPO" ]] || { echo "missing vendored git-filter-repo at $FILTER_REPO" >&2; exit 1; }

command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 1; }

mkdir -p "$WORK_DIR"

if [[ -d "$FILTERED" && $FORCE -eq 0 ]]; then
  log "Filtered repo already exists at $FILTERED (pass --force to rebuild). Nothing to do."
  exit 0
fi

# ---------------------------------------------------------------------------
# 1. Mirror the source.
#
# A mirror is required rather than a plain clone: we need refs/remotes/* and tags,
# and git-filter-repo refuses to operate on a repository that has unpushed local state.
# ---------------------------------------------------------------------------
if [[ ! -d "$MIRROR" ]]; then
  log "Mirroring $MAUI_SOURCE -> $MIRROR"
  git clone --no-hardlinks --mirror "$MAUI_SOURCE" "$MIRROR"
else
  log "Reusing existing mirror at $MIRROR"
fi

# ---------------------------------------------------------------------------
# 2. Reduce the mirror to just the two pinned baseline commits.
#
# dotnet/maui carries thousands of refs. Filtering all of them would take far longer
# and would drag in unrelated release branches, so we pin exactly the commits the
# migration is baselined against and drop the rest.
# ---------------------------------------------------------------------------
log "Pinning baseline commits"
if ! BRANCH_SHA="$(git --git-dir="$MIRROR" rev-parse --verify --quiet "$SOURCE_COMMIT^{commit}")"; then
  echo "FATAL: pinned source commit $SOURCE_COMMIT is not present in $MIRROR." >&2
  echo "       Fetch dotnet/maui net11.0 history and retry." >&2
  exit 1
fi
if ! TAG_SHA="$(git --git-dir="$MIRROR" rev-parse --verify --quiet "$SOURCE_TAG_COMMIT^{commit}")"; then
  echo "FATAL: pinned tag commit $SOURCE_TAG_COMMIT ($SOURCE_TAG) is not present in $MIRROR." >&2
  exit 1
fi

log "  source baseline  = $BRANCH_SHA (net11.0 @ 2026-08-18)"
log "  $SOURCE_TAG baseline = $TAG_SHA"

# Sanity gate: the mandated Essentials/MainThread extensibility work (PR #36657) must be
# in the source baseline. If it is not, the whole import is built on the wrong branch.
REQUIRED_COMMIT="${REQUIRED_COMMIT:-0b3bb76d2dd68d76b7c1302f43a76270d5949564}"
if ! git --git-dir="$MIRROR" merge-base --is-ancestor "$REQUIRED_COMMIT" "$BRANCH_SHA" 2>/dev/null; then
  echo "FATAL: required commit $REQUIRED_COMMIT (PR #36657) is not an ancestor of the source baseline." >&2
  echo "       Note that this commit is NOT on dotnet/maui 'main' — it lives on 'net11.0'." >&2
  exit 1
fi
log "  verified PR #36657 ($REQUIRED_COMMIT) is in the baseline"

rm -rf "$WORK_DIR/pinned.git"
git clone --no-hardlinks --bare "$MIRROR" "$WORK_DIR/pinned.git" >/dev/null 2>&1
git --git-dir="$WORK_DIR/pinned.git" for-each-ref --format='delete %(refname)' | \
  git --git-dir="$WORK_DIR/pinned.git" update-ref --stdin
git --git-dir="$WORK_DIR/pinned.git" update-ref "refs/heads/$IMPORT_BRANCH" "$BRANCH_SHA"
git --git-dir="$WORK_DIR/pinned.git" update-ref "refs/tags/$SOURCE_TAG" "$TAG_SHA"

rm -rf "$FILTERED"
mv "$WORK_DIR/pinned.git" "$FILTERED"

# ---------------------------------------------------------------------------
# 3. Filter.
#
# --paths-from-file  declarative, reviewable path spec (see tizen-paths.txt)
# --preserve-commit-hashes is deliberately NOT used: paths change, so hashes must too.
# Author/committer identity, dates, and messages are preserved by default, which is the
# entire point of the exercise — contributors keep attribution in the new repository.
#
# GIT_DIR is exported because git-filter-repo shells out to plain `git` commands from
# the working directory, and a user with `safe.bareRepository=explicit` configured
# (as is the case on this machine) would otherwise have those calls refuse to run.
# ---------------------------------------------------------------------------
log "Filtering history to Tizen paths"
( cd "$FILTERED" && GIT_DIR=. python3 "$FILTER_REPO" \
    --force \
    --paths-from-file "$PATHS_FILE" \
    --prune-empty always \
    --prune-degenerate always )

# ---------------------------------------------------------------------------
# 4. Report.
# ---------------------------------------------------------------------------
COMMITS="$(git --git-dir="$FILTERED" rev-list --count "refs/heads/$IMPORT_BRANCH")"
AUTHORS="$(git --git-dir="$FILTERED" log --format='%aN' "refs/heads/$IMPORT_BRANCH" | sort -u | wc -l | tr -d ' ')"
FILES="$(git --git-dir="$FILTERED" ls-tree -r --name-only "refs/heads/$IMPORT_BRANCH" | wc -l | tr -d ' ')"

log "Filtered history ready at $FILTERED"
log "  commits retained : $COMMITS"
log "  distinct authors : $AUTHORS"
log "  files at tip     : $FILES"

# Provenance anchors that MUST survive the filter.
for pr in 2360 9619; do
  if git --git-dir="$FILTERED" log --oneline --all --grep="#${pr})" | grep -q .; then
    log "  provenance PR #$pr : present"
  else
    echo "WARNING: provenance PR #$pr was pruned by the filter" >&2
  fi
done
