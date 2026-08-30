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

Please report suspected security vulnerabilities using GitHub's private
vulnerability reporting:

- Use the **"Report a vulnerability"** button under this repository's
  Security tab (Security → Advisories → "Report a vulnerability"). This is
  the interim reporting channel for `Redth/Maui.Tizen` today.

**Post-transfer requirement**: once ownership moves to Samsung (see
`docs/governance/samsung-transfer-checklist.md`), this section **must** be
updated with Samsung's designated security contact/process (e.g. a
Samsung PSIRT alias or equivalent) as a required transfer item — do not
leave this pointing at the interim maintainer's private-reporting channel
after transfer. No other reporting channel (e.g. MSRC) is authorized for
this repository unless a future repository owner explicitly documents that
authorization here; this repository is not currently affiliated with, nor
does it route reports to, Microsoft.

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
