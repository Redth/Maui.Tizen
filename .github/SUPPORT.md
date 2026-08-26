# Support

This document describes how to get help with Maui.Tizen and what level of
support to expect at the current project stage.

## Project Status

Maui.Tizen is in active bring-up as an out-of-tree Tizen backend for .NET
MAUI, ahead of a planned transfer to Samsung ownership. Support commitments
below will be superseded by Samsung's own support policy post-transfer (see
`docs/governance/samsung-transfer-checklist.md`); until then, this document
is authoritative.

## How to Get Help

1. **Documentation first**: check the README, `docs/governance/` policies,
   and the Tizen support matrix
   (`docs/governance/tizen-support-matrix.md`) for known limitations before
   filing an issue.
2. **Search existing issues**: someone may have already reported (or
   answered) your question.
3. **Open an issue**: use the appropriate template under
   `.github/ISSUE_TEMPLATE/`. Include the Maui.Tizen package version, .NET
   SDK/workload version, Tizen profile/API level, and a minimal repro when
   reporting bugs.
4. **Security issues**: do **not** use public issues — see `SECURITY.md`.

## Support Boundaries

- **In scope**: build/pack/deploy issues with `Maui.Tizen.*` packages,
  workload installation problems specific to Tizen, API compatibility
  questions against the published support matrix, and regressions between
  released versions.
- **Out of scope (route elsewhere)**:
  - General .NET MAUI questions unrelated to Tizen → `dotnet/maui`.
  - Tizen platform/OS/device issues unrelated to .NET → Samsung's
    `Tizen.NET` / Tizen Developer support channels.
  - Questions about unreleased/roadmap features not tracked in an issue.

## Response Expectations

- Given the current project stage, there is **no guaranteed SLA**. Triage is
  best-effort by the interim maintainer (see `.github/CODEOWNERS`).
- Once transferred to Samsung, response-time expectations should be defined
  as part of the transfer (tracked in
  `docs/governance/samsung-transfer-checklist.md`) and published here.

## Long-Term Servicing

For which versions receive bug fixes and security patches, see
`docs/governance/release-and-servicing-policy.md`.
