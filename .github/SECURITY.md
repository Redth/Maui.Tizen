# Security Policy

## Supported Versions

Maui.Tizen follows the servicing policy in
`docs/governance/release-and-servicing-policy.md`. Security fixes are
provided for:

| Version line          | Supported          |
| ---------------------- | ------------------ |
| Latest stable (current .NET 11-aligned release) | :white_check_mark: |
| Previous stable line (N-1), within its servicing window | :white_check_mark: |
| Preview / RC builds     | Best-effort only |
| Anything older than N-1 | :x: |

The exact supported version ranges are published alongside each release and
tracked in `docs/governance/tizen-support-matrix.md`.

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report suspected security vulnerabilities using one of the following
channels, in order of preference:

1. **GitHub private vulnerability reporting**: use the "Report a
   vulnerability" button under this repository's Security tab
   (Security → Advisories → "Report a vulnerability"). This is the preferred
   channel once the transfer to the target GitHub org is complete and
   private reporting is enabled.
2. **Microsoft Security Response Center (MSRC)**: while this repository is
   maintained under a Microsoft-affiliated org, vulnerabilities may also be
   reported via <https://msrc.microsoft.com/create-report>, following the
   [Microsoft `SECURITY.md` guidance](https://github.com/microsoft/repo-templates/blob/main/shared/SECURITY.md).
3. **Post-transfer**: once ownership moves to Samsung (see
   `docs/governance/samsung-transfer-checklist.md`), this section must be
   updated with Samsung's designated security contact/process before the
   MSRC channel is removed. Do not remove the MSRC channel until a
   replacement contact is confirmed and tested.

Please include as much detail as possible:

- Affected package(s) and version(s) (e.g. `Maui.Tizen.Core 11.0.0-preview.1`)
- Tizen profile/device/emulator and API level
- Reproduction steps or a minimal sample
- Potential impact (e.g., code execution, privilege escalation, data
  exposure)

You should receive an acknowledgment within 5 business days. This is a
young, actively-developed project — response times outside business hours
or during transfer windows may be slower; we will communicate delays.

## Disclosure Policy

We follow coordinated disclosure. Please give us a reasonable window to
investigate and release a fix before any public disclosure. We will credit
reporters (unless anonymity is requested) in the release notes of the fix.

## Scope Notes

- This repository does not currently hold any secrets, signing keys, or
  credentials in source; publishing/signing is performed through
  organization-managed environments (see
  `docs/governance/package-metadata-conventions.md` and
  `.github/workflows/release.yml`). Reports about leaked credentials in this
  repo should still be reported immediately via the channels above.
