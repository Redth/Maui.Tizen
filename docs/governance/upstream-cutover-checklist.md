# Upstream Cutover Checklist (dotnet/maui and Samsung/Tizen.NET)

Maui.Tizen was extracted from `dotnet/maui`. This checklist tracks what must
happen upstream (in `dotnet/maui` and in Samsung's `Tizen.NET` templates/
docs repos) for the cutover to be complete and non-disruptive. **This
checklist does not authorize or perform any deletion or publication** — it
exists so those actions can be sequenced correctly by their respective code
owners once Maui.Tizen is independently released and stable.

## Guiding rule

Nothing in `dotnet/maui` (old in-tree Tizen code, docs, templates) and
nothing in Samsung's `Tizen.NET` repos should be deleted or repointed until
**Maui.Tizen has at least one stable, publicly consumable release** that
covers equivalent functionality — verified against
`docs/governance/tizen-support-matrix.md` — and a deprecation notice
period (see `docs/governance/deprecation-policy.md`) has been observed in
the upstream repo(s) being cut over.

## 1. `dotnet/maui` repository

- [ ] Identify all in-tree Tizen-specific code paths (handlers, platform
      implementations, build props/targets, workload manifest entries)
      that Maui.Tizen supersedes.
- [ ] Open a tracking issue in `dotnet/maui` proposing removal, with the
      minimum notice period from `docs/governance/deprecation-policy.md`
      §2 and a link to Maui.Tizen's first stable release.
- [ ] Add an `[Obsolete]`/deprecation warning (not immediate removal) to
      the in-tree Tizen code pointing at `Maui.Tizen.*` as the replacement,
      once Maui.Tizen ships a stable release with matching capability
      coverage.
- [ ] Coordinate the actual removal PR in `dotnet/maui` with `dotnet/maui`
      maintainers directly — this repo's maintainers do not have authority
      to merge changes there; this checklist only tracks readiness.
- [ ] Update `dotnet/maui` docs/samples that reference in-tree Tizen
      support to point at Maui.Tizen instead, timed with the deprecation
      notice, not the eventual removal.
- [ ] Confirm `dotnet/maui`'s own release notes call out the migration
      path for existing Tizen app consumers (project file changes, package
      reference changes, workload changes).

## 2. .NET SDK workload manifests

- [ ] Confirm whether the Tizen workload manifest currently ships as part
      of the in-box `maui` workload or a separate `maui-tizen` workload.
      If in-box today, plan the split to a standalone `maui-tizen`
      workload manifest owned alongside this repo (aligns with
      `docs/governance/versioning-policy.md` §3). Coordinate this
      confirmation with whichever workstream owns workload/CI
      scaffolding, since the manifest split is a build-time concern, not
      a governance one.
- [ ] Ensure the .NET SDK's workload resolver can bundle the standalone
      `maui-tizen` workload without requiring a full SDK release
      out-of-band (i.e., it can ship via a workload manifest update,
      following the same pattern as other out-of-band MAUI platform
      workloads).
- [ ] Update any `dotnet new` templates that assume in-tree Tizen support
      to instead reference the `Maui.Tizen.*` package set once stable.

## 3. Samsung `Tizen.NET` templates/docs repos

- [ ] Inventory existing Samsung-owned templates/docs that reference the
      old in-tree `dotnet/maui` Tizen support path, so they can be
      repointed rather than left stale.
- [ ] Coordinate with Samsung `Tizen.NET` maintainers on a joint timeline:
      Samsung template/docs updates should land no earlier than
      Maui.Tizen's first stable release, and ideally in the same
      announcement window as the `dotnet/maui` deprecation notice (§1) to
      avoid presenting developers with three different "correct" answers
      at once.
- [ ] Confirm Samsung's `Tizen.NET.Sdk` / `Tizen.NET.API*` package version
      compatibility promises are reflected in
      `docs/governance/tizen-support-matrix.md` before docs are updated to
      recommend Maui.Tizen as the primary path.
- [ ] Confirm whose repo hosts the canonical "getting started with .NET
      MAUI on Tizen" doc going forward, and set up redirects/cross-links
      rather than duplicating content indefinitely.

## 4. Sequencing summary

1. Maui.Tizen ships a stable release (post rehearsal in
   `docs/governance/samsung-transfer-checklist.md` §8, if transfer has
   already happened; otherwise a stable release from the interim owner is
   sufficient to start this sequence).
2. `dotnet/maui` and Samsung docs/templates add deprecation notices
   pointing at Maui.Tizen (no deletions yet).
3. Minimum notice period elapses (per
   `docs/governance/deprecation-policy.md`).
4. `dotnet/maui` in-tree Tizen code and any superseded Samsung
   templates/docs are removed/archived, coordinated jointly — never
   unilaterally by this repo's maintainers.
