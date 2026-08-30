# Deprecation Policy

This policy governs how public APIs, TFMs, minimum Tizen API levels, and
whole version lines are deprecated and eventually removed for
`Maui.Tizen.*` packages.

## 1. Principles

- **No silent breaking removal.** Anything covered by
  `docs/governance/api-compatibility-policy.md` §1 must go through
  deprecation before removal, with the notice periods below.
- **Deprecate in N, remove in N+1 major at the earliest.** A deprecation
  must ship (with a warning) in at least one release before the earliest
  release that could remove it, and that removal can only happen in a
  major version bump per `docs/governance/versioning-policy.md`.
- **Always give an upgrade path.** A deprecation notice must state the
  replacement API/pattern, or explicitly state "no replacement, capability
  removed" with rationale.

## 2. Minimum notice periods

| Deprecated item | Minimum notice before removal |
| --- | --- |
| Public API member/type | 1 full minor release cycle, or 6 months, whichever is longer |
| Minimum supported Tizen API level increase | 1 full minor release cycle **and** published in `docs/governance/tizen-support-matrix.md` at least one release ahead |
| Supported Tizen profile (e.g. dropping Wearable) | 2 full minor release cycles, since this affects app store submissions/certifications |
| Whole major version line (end of servicing) | Aligned with the underlying .NET version's published EOL date; announced at GA of the version that supersedes it, at minimum |
| MSBuild property/item/target | Same as public API member |

These are floors, not targets — approvers may choose longer notice for
high-usage APIs.

## 3. Marking something deprecated

- **Code**: apply `[Obsolete("message", error: false)]` (or the analyzer
  equivalent used elsewhere in `dotnet/maui`) with a message that includes
  the replacement and the planned removal version.
- **Docs**: add an entry to a `CHANGELOG`/release notes "Deprecations"
  section, and update `docs/governance/tizen-support-matrix.md` if a
  profile/API level is affected.
- **Issue tracking**: open a tracking issue labeled `deprecation`, linked
  from the `[Obsolete]` message where practical (e.g. via a doc link), so
  consumers and maintainers can follow removal status.

## 4. Removal checklist

Before actually removing a deprecated item in a major release:

- [ ] Minimum notice period (§2) has elapsed since the item was first
      marked deprecated in a **shipped** (not just `main`) release.
- [ ] Release notes for the removal version explicitly list every removed
      item with its original deprecation version and replacement guidance.
- [ ] `docs/governance/tizen-support-matrix.md` updated if the removal
      affects profile/API level support.
- [ ] API compatibility baseline updated per
      `docs/governance/api-compatibility-policy.md`.

## 5. Emergency exceptions

A shorter notice period may be used only for:

- Security vulnerabilities where the deprecated surface is itself the
  vulnerable code path (coordinate with `SECURITY.md`'s disclosure
  process), or
- Legal/licensing requirements to remove specific code.

Any emergency exception must be explicitly called out in release notes
with the rationale, and approved through the same release environment
gates as a normal release (no bypassing `.github/workflows/release.yml`
approvals even for emergencies).
