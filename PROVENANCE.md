# Provenance

This repository is a history-preserving extraction of the .NET MAUI Tizen backend from
[dotnet/maui](https://github.com/dotnet/maui). It is **not** a fresh reimplementation and
it is **not** a squashed copy: the original commits, authors, dates, and messages were
carried across so that every contributor keeps attribution here.

---

## Pinned baselines

All baselines are pinned to **commit SHAs, never branch names**. This is not pedantry:
`origin/net11.0` advanced from `ee4d06cde6` to `bedd1b18b7` during the few hours this
import was being prepared. A branch name would have made the import unreproducible.

| Role | Ref | Why |
|---|---|---|
| `sourceBaseline` | `ee4d06cde6b49e297631b08426a33fb34f3152ef` | dotnet/maui `net11.0` @ 2026-08-18. The forward source baseline. |
| `requiredAncestor` | `0b3bb76d2dd68d76b7c1302f43a76270d5949564` | PR [#36657](https://github.com/dotnet/maui/pull/36657), the Essentials/MainThread extensibility work. Minimum API floor. |
| `behaviorBaseline` | `c1f4f7d879f6126029009902289efd6a4bb1bda9` | Tag `9.0.120`, the last published Tizen behaviour/API baseline. Retained here as tag `upstream/9.0.120`. |
| `developmentPackageBaseline` | `11.0.0-preview.7.26426.4` | Coherent public-feed MAUI package set, from the [dnceng `dotnet11` feed](https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json). All key nuspec repository commits resolve to `bedd1b18b7`. |

Two details are easy to get wrong and are worth stating explicitly:

1. **`0b3bb76d2d` is not on dotnet/maui `main`.** It lives on `net11.0` (and
   `release/11.0.1xx-rc1`). Baselining against `main` would silently omit it. The import
   script hard-fails if this commit is not an ancestor of the source baseline.

2. **`src/Compatibility` does not exist on `net11.0`** — it was deleted upstream. Its 70
   Tizen files survive only at tag `9.0.120`, which is why that tag is imported and
   retained rather than just referenced. An import that took `net11.0` alone would lose
   them without any error.

### What the pin excludes

`sourceBaseline` is 4 commits after `requiredAncestor`, and none of those 4 touch Tizen
paths. But `net11.0` has continued past the pin, and three later commits **do**:

| Commit | PR | Title |
|---|---|---|
| `62418a4ec4` | [#37420](https://github.com/dotnet/maui/pull/37420) | Expose gesture recognizer dispatch APIs |
| `78502a5325` | [#37671](https://github.com/dotnet/maui/pull/37671) | Harden gesture recognizer dispatch API contracts |
| `4695c95801` | [#37755](https://github.com/dotnet/maui/pull/37755) | Add badge support to TabbedPage |

All three touch **only** `src/Controls/src/Core/PublicAPI/net-tizen/PublicAPI.Unshipped.txt`
— API surface declarations, not implementation. So the concrete gap is that the Controls
`net-tizen` Unshipped baseline is three API additions behind current `net11.0`. That
affects API baseline diffing rather than source migration.

This is recorded because a pin is only genuinely reproducible if what it *excludes* is
written down too.

---

## What was imported

| Measure | Value |
|---|---|
| Commits retained | 1,236 |
| Distinct authors | 121 |
| Files at tip | 316 |
| Earliest commit | 2016-04-29 |

History reaches back into the Xamarin.Forms era, well before the MAUI rename — the Tizen
backend predates this repository by nearly a decade.

### Major provenance pull requests

Both are present in the imported history and must remain so; the import script checks for
them and warns if the filter prunes either.

| Upstream PR | Commit in this repo | Title |
|---|---|---|
| [#2360](https://github.com/dotnet/maui/pull/2360) | `78aeb55` | Adds Tizen backend |
| [#9619](https://github.com/dotnet/maui/pull/9619) | `fd7dcae` | [main][Tizen] Replace Tizen Backend engine |

### Principal contributors to the Tizen backend

Attribution is preserved in git; this list is a convenience, not a substitute for
`git shortlog`. Contributors to the Tizen backend include Kangho Hur, Seungkeun Lee,
shmin, Jay Cho, and sung-su.kim from the Samsung side, alongside .NET MAUI team members
including Matthew Leibowitz, Rui Marinho, Shane Neuville, Gerald Versluis, Jonathan Dick,
and Samantha Houts.

To see the real picture:

```bash
git shortlog -sne --no-merges
```

---

## How the import was performed

The import is reproducible from the scripts in [`eng/import/`](eng/import/), and lands as
**two deliberately separate commits**.

### 1. Raw import — `eng/import/filter-maui-tizen.sh`

Mirrors dotnet/maui, reduces it to the two pinned baseline commits, and rewrites history
with [`git-filter-repo`](https://github.com/newren/git-filter-repo) (vendored as a single
file so no system install is required), retaining only paths matched by
[`eng/import/tizen-paths.txt`](eng/import/tizen-paths.txt).

The path spec matches `(?i)tizen` rather than enumerating current directories. That is
intentional: the backend has lived under many layouts over the years —
`Xamarin.Forms.Platform.Tizen/**`, `Stubs/**`, `Samples/Samples.Tizen/**`,
`PagesGallery/**`, `EmbeddingTestBeds/**`, `src/Platform.Renderers/**`, and today's
`src/Core/src/Platform/Tizen/**` — and a path list pinned to the present would have
truncated the history at each rename.

Upstream `LICENSE.txt` and `THIRD-PARTY-NOTICES.TXT` are also retained, so the licensing
lineage is verifiable from the git log itself rather than only from our own notice files.
They now live in [`docs/upstream/`](docs/upstream/).

**No file content is modified by this step.**

### 2. Normalization — `eng/import/normalize-layout.sh`

Reshapes the imported tree into this repository's layout. Every operation is a pure
`git mv`; the resulting diff is 316 renames with **zero content changes**, which keeps
`git log --follow` working across the restructure.

| From (dotnet/maui) | To (here) |
|---|---|
| `src/Core/src/**` | `src/Maui.Tizen.Core/**` |
| `src/Core/maps/src/**` | `src/Maui.Tizen.Maps/Core/**` |
| `src/Controls/Maps/src/**` | `src/Maui.Tizen.Maps/Controls/**` |
| `src/Controls/src/Core/**` | `src/Maui.Tizen.Controls/Core/**` |
| `src/Controls/src/Xaml/**` | `src/Maui.Tizen.Controls/Xaml/**` |
| `src/Essentials/src/**` | `src/Maui.Tizen.Essentials/**` |
| `src/BlazorWebView/src/Maui/**` | `src/Maui.Tizen.BlazorWebView/**` |
| `src/Graphics/src/**` | `src/Maui.Tizen.Graphics/**` |
| `src/SingleProject/Resizetizer/src/**` | `src/Maui.Tizen.Build.Tasks/**` |
| `src/*/samples/**` | `samples/**` |
| `src/Controls/tests/**` | `tests/Controls/**` |
| `eng/common/cross/**` | `eng/cross/**` |

Inner `Tizen/` directories and `.Tizen.cs` suffixes are **kept**, even though every file
here is Tizen-specific and they are therefore redundant. The MSBuild compile-item
conventions inherited from `src/MultiTargeting.targets` still key off those names, and
flattening them would convert a clean rename-only diff into large content churn. Removing
that redundancy belongs with the handler implementation workstream.

The two steps are kept separate so a reviewer can verify that nothing was smuggled in
during the filter by diffing the import commit on its own.

---

## Licensing

dotnet/maui is MIT licensed, and so is this repository. See [`LICENSE`](LICENSE).

Samsung's TizenFX and Tizen.UIExtensions are Apache-2.0 and are consumed **only** as
published NuGet packages and SDK workload packs — no Samsung source is copied into this
repository. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

---

## Reproducing the import

```bash
# A local dotnet/maui clone is dramatically faster than cloning from GitHub.
eng/import/filter-maui-tizen.sh --source /path/to/local/maui
eng/import/normalize-layout.sh
```

The filter is deterministic: the same pinned SHAs always yield the same rewritten history.
