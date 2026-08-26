#!/usr/bin/env bash
#
# normalize-layout.sh — reshape the raw dotnet/maui import into the Maui.Tizen layout.
#
# This runs as a SEPARATE commit from the raw import on purpose. The import commit is
# mechanical and machine-reproducible; this one encodes editorial decisions about where
# things live. Keeping them apart means a reviewer can verify "nothing was smuggled in
# during the filter" by diffing the import commit alone.
#
# Every operation here is a pure `git mv`. No file content is edited, so the diff is
# rename-only and git's rename detection keeps `git log --follow` working through it.
#
# Design decisions worth knowing before you read the mapping:
#
#  * The MAUI repo's scaffolding prefixes (`src/<Area>/src/...`) are stripped, because
#    they encode dotnet/maui's multi-area monorepo shape, which this repo does not have.
#
#  * Inner `Tizen/` directories and `.Tizen.cs` suffixes are deliberately KEPT, even
#    though every file in this repository is Tizen-specific and they are therefore
#    redundant. Two reasons: the MSBuild compile-item conventions inherited from
#    src/MultiTargeting.targets still key off those names, and flattening them would
#    turn a clean rename-only diff into a large content-churn diff. Removing the
#    redundancy belongs with the handler implementation workstream, not the foundation.
#
#  * PublicAPI/net-tizen/*.txt files travel with their project. They are consumed as
#    API baselines by the inventory tooling.
#
# Usage: eng/import/normalize-layout.sh
# Safe to re-run: each move is skipped if the source no longer exists.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

# move <from> <to> — git mv that creates parents and tolerates an already-moved source.
move() {
  local from="$1" to="$2"
  if [[ ! -e "$from" ]]; then
    return 0
  fi
  mkdir -p "$(dirname "$to")"
  git mv -k "$from" "$to"
  log "  $from -> $to"
}

# move_children <from-dir> <to-dir> — move a directory's contents, preserving structure.
move_children() {
  local from="$1" to="$2"
  [[ -d "$from" ]] || return 0
  mkdir -p "$to"
  local entry
  for entry in "$from"/*; do
    [[ -e "$entry" ]] || continue
    move "$entry" "$to/$(basename "$entry")"
  done
  rmdir -p "$from" 2>/dev/null || true
}

log "Normalizing imported layout"

# --- Core -------------------------------------------------------------------
# src/Core/src is the Tizen platform layer: handlers, platform views, lifecycle.
move_children "src/Core/src"       "src/Maui.Tizen.Core"

# --- Maps -------------------------------------------------------------------
# Maps is split across two areas upstream (Core-level handlers and Controls-level
# types). They ship as one package here, so they are collapsed into one project
# with the split preserved one level down.
move_children "src/Core/maps/src"      "src/Maui.Tizen.Maps/Core"
move_children "src/Controls/Maps/src"  "src/Maui.Tizen.Maps/Controls"

# --- Controls ---------------------------------------------------------------
move_children "src/Controls/src/Core" "src/Maui.Tizen.Controls/Core"
move_children "src/Controls/src/Xaml" "src/Maui.Tizen.Controls/Xaml"

# --- Essentials -------------------------------------------------------------
move_children "src/Essentials/src" "src/Maui.Tizen.Essentials"

# --- BlazorWebView ----------------------------------------------------------
move_children "src/BlazorWebView/src/Maui" "src/Maui.Tizen.BlazorWebView"

# --- Graphics ---------------------------------------------------------------
# Graphics is upstreamed from dotnet/Microsoft.Maui.Graphics and only carries a
# single Tizen view plus API baselines. Its final disposition (keep-upstream vs.
# ship here) is recorded in the source-disposition manifest; it is parked here so
# the file is not lost while that decision is made.
move_children "src/Graphics/src/Graphics"      "src/Maui.Tizen.Graphics/Graphics"
move_children "src/Graphics/src/Graphics.Skia" "src/Maui.Tizen.Graphics/Graphics.Skia"

# --- Build tasks ------------------------------------------------------------
# The Tizen manifest/splash/resource generators live in Resizetizer upstream.
move_children "src/SingleProject/Resizetizer/src" "src/Maui.Tizen.Build.Tasks"

# --- Samples ----------------------------------------------------------------
move_children "src/Controls/samples"   "samples/Controls"
move_children "src/Essentials/samples" "samples/Essentials"
move_children "src/Graphics/samples"   "samples/Graphics"

# --- Tests ------------------------------------------------------------------
move_children "src/Controls/tests" "tests/Controls"

# --- Engineering ------------------------------------------------------------
# Tizen cross-compilation rootfs scripts, from eng/common/cross upstream.
move_children "eng/common/cross" "eng/cross"

# --- Upstream licence artifacts --------------------------------------------
# Retained for licensing lineage, but moved out of the root so they cannot be
# confused with this repository's own LICENSE and THIRD-PARTY-NOTICES.md.
move "LICENSE.txt"              "docs/upstream/dotnet-maui-LICENSE.txt"
move "THIRD-PARTY-NOTICES.TXT"  "docs/upstream/dotnet-maui-THIRD-PARTY-NOTICES.txt"

# Prune directories emptied by the moves above.
find src samples tests eng -type d -empty -delete 2>/dev/null || true

log "Done. Review with: git status && git diff --cached -M --stat"
