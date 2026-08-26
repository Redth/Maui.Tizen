# Governance & Release Documentation

This directory holds the policy and process documents that prepare
Maui.Tizen for durable, multi-party ownership (currently interim, with a
planned transfer to Samsung — see the transfer checklist below). None of
these documents change build or runtime behavior by themselves; they define
policy that CI, release automation, and reviewers are expected to enforce.

| Document | Purpose |
| --- | --- |
| [`versioning-policy.md`](./versioning-policy.md) | .NET 11-aligned package versioning scheme and MAUI/Tizen workload dependency rules |
| [`package-metadata-conventions.md`](./package-metadata-conventions.md) | NuGet metadata conventions, package ownership, and trusted-publishing model |
| [`release-and-servicing-policy.md`](./release-and-servicing-policy.md) | Release cadence, supported version lines, and the release process gate |
| [`api-compatibility-policy.md`](./api-compatibility-policy.md) | What counts as a breaking change and how it must be reviewed |
| [`tizen-support-matrix.md`](./tizen-support-matrix.md) | Template for the supported Tizen profile/API level matrix per release line |
| [`deprecation-policy.md`](./deprecation-policy.md) | Minimum notice periods and process for deprecating/removing APIs, profiles, or version lines |
| [`samsung-transfer-checklist.md`](./samsung-transfer-checklist.md) | Everything required before/at transfer of ownership to Samsung |
| [`upstream-cutover-checklist.md`](./upstream-cutover-checklist.md) | Sequencing for deprecating in-tree Tizen support in `dotnet/maui` and updating Samsung `Tizen.NET` templates/docs |

## Related files outside this directory

- [`.github/CODEOWNERS`](../../.github/CODEOWNERS) — review routing (currently placeholder handles)
- [`.github/CONTRIBUTING.md`](../../.github/CONTRIBUTING.md)
- [`.github/SECURITY.md`](../../.github/SECURITY.md)
- [`.github/SUPPORT.md`](../../.github/SUPPORT.md)
- [`.github/workflows/release.yml`](../../.github/workflows/release.yml) — release automation skeleton, safely gated/disabled until publishing prerequisites are met

## Status

This is a **preparation** PR, conceptually stacked on the foundation import
branch. No package is published, no secret is provisioned, and no
runtime/source code is changed by these documents. See each file's own
status notes for what remains before it becomes an enforced, live policy.
