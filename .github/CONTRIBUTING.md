# Contributing to Maui.Tizen

Thanks for your interest in contributing! Maui.Tizen provides the Tizen
platform backend for .NET MAUI, extracted from `dotnet/maui`. This repository
is in an active bring-up phase ahead of a planned transfer to Samsung
ownership (see `docs/governance/samsung-transfer-checklist.md`), so process
here is intentionally conservative.

## Before you start

- **Check the support matrix.** `docs/governance/tizen-support-matrix.md`
  lists the Tizen profiles/API levels and .NET versions this repo targets.
  Changes outside that matrix need a discussion issue first.
- **Search existing issues/PRs.** Avoid duplicate work; this project pulls
  from and stays coordinated with `dotnet/maui` and Samsung's
  `Tizen.NET`/workload repos.
- **Small, reviewable PRs.** Prefer focused changes over large drops. Large
  or generated changes (e.g., bulk formatting) should be called out in the
  PR description and, ideally, split into their own commit.

## Development workflow

1. Fork and branch from `main` (or the currently active integration branch,
   if the maintainers have called one out in the repo README/pins).
2. Make your change, including tests where applicable.
3. Run the build and test suite locally before opening a PR (see the repo
   README for current build instructions; CI enforces the same checks).
4. Open a PR using the provided template. Link any related issues.
5. Address review feedback. At least one CODEOWNERS approval is required to
   merge (see `.github/CODEOWNERS`).

## Commit and PR conventions

- Use clear, descriptive commit messages; squash-merge is the default merge
  strategy unless a maintainer requests otherwise.
- PRs that change public API surface must follow
  `docs/governance/api-compatibility-policy.md`, including updating the
  public API baseline files where applicable.
- PRs that touch package versioning, TFMs, or workload dependencies must
  follow `docs/governance/versioning-policy.md`.
- Do not include secrets, tokens, personal account references, or
  organization-internal URLs in code, workflows, or docs.

## Reporting bugs and requesting features

Use the issue templates under `.github/ISSUE_TEMPLATE/`. For security
vulnerabilities, do **not** open a public issue — follow `SECURITY.md`.

## Code of Conduct

Interactions in this repository are expected to be respectful and
professional. Until a project-specific Code of Conduct is published, the
[.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct)
applies as the interim standard.

## License

By contributing, you agree that your contributions will be licensed under
the same license as this repository (see `LICENSE` once published; MIT is
the intended license consistent with `dotnet/maui`).
