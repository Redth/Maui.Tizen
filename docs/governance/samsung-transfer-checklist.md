# Samsung Transfer Checklist

This checklist covers what must be true before Maui.Tizen ownership and
publishing responsibility transfer to Samsung. **Nothing in this checklist
is executed by this PR** — it is the governance record of what "done"
looks like, so the transfer can be tracked and audited item-by-item.
Nothing here should be treated as complete until independently verified by
both the transferring and receiving org.

## 1. GitHub organization & teams

- [ ] Target GitHub org/repo location confirmed with Samsung (fork-and-
      transfer vs. repo-transfer vs. new repo + history import).
- [ ] Samsung-side GitHub teams created and mapped to the placeholder
      handles in `.github/CODEOWNERS` (e.g. `@samsung/tizen-dotnet-core`,
      `@samsung/tizen-dotnet-leads`).
- [ ] Interim maintainer team (`@Redth` / `.NET Foundation`/Microsoft-side
      contacts) retained with read/triage access for a defined transition
      window post-transfer (avoid an abrupt "day 1, zero access" cutover).
- [ ] `.github/CODEOWNERS` updated to reference real teams (no more
      placeholder comments) once teams exist and have repo access.

## 2. Environments & secrets

- [ ] GitHub Environments used by `.github/workflows/release.yml`
      (`build`, `sign`, `publish` or equivalent) recreated under Samsung's
      org with their own protection rules (required reviewers, wait
      timers, deployment branch restrictions).
- [ ] No secrets/credentials carried over from the interim org "as-is"
      without rotation — every credential (signing cert, publishing
      trust config) is reissued fresh in the Samsung-owned environment.
- [ ] Confirm nuget.org **Trusted Publishing** is (re)configured against
      the new org/repo (OIDC subject changes when the repo moves org —
      trusted publishing configs are typically repo-path-scoped and must
      be redone, not just copied).
- [ ] Signing certificate ownership moved to a Samsung-controlled
      certificate authority relationship / code-signing service; interim
      certificate (if any was ever provisioned) revoked.

## 3. Branch protection & repo settings

- [ ] Branch protection rules on `main` (and any servicing branches)
      recreated: required status checks, required reviews, required
      CODEOWNERS review, linear history/merge strategy, restriction on
      force-push/deletion.
- [ ] Required status checks list matches the workflow job names in
      `.github/workflows/release.yml` and any CI workflow owned by the
      foundation/CI workstream (avoid a gap where checks silently stop
      being required after a repo move, which can happen if job names
      differ post-transfer).
- [ ] Repository security settings reviewed: Dependabot, secret scanning,
      code scanning, private vulnerability reporting all enabled under the
      new org's policies.

## 4. Package ownership

- [ ] nuget.org **package ID prefix reservation** for `Maui.Tizen.*`
      confirmed under the Samsung-controlled (or jointly-administered)
      nuget.org organization account (see
      `docs/governance/package-metadata-conventions.md` §1).
- [ ] Package owners list on nuget.org updated per
      `docs/governance/package-metadata-conventions.md` §4 (org account +
      break-glass individuals, MFA enforced on all).
- [ ] Symbol server / source link configuration re-verified after the
      repo URL changes (stale `RepositoryUrl` metadata in already-published
      packages is expected and documented, not silently ignored).

## 5. Signing

- [ ] Code-signing and NuGet package-signing certificates provisioned
      under Samsung's PKI/organization, distinct from any interim/
      transitional signing used before transfer.
- [ ] Signing job in `.github/workflows/release.yml` updated to reference
      the new environment/secret names (no leftover references to interim
      infrastructure).

## 6. Device runners / validation infrastructure

- [ ] Physical Tizen device and/or emulator runners for CI validation
      provisioned and reachable from the new org's Actions runners
      (self-hosted runner registration, network/firewall rules).
- [ ] `docs/governance/tizen-support-matrix.md` re-validated against the
      new runner fleet before the first Samsung-owned release — do not
      assume prior validation results carry over if the runner
      infrastructure changed.
- [ ] Runner maintenance/ownership (who patches OS images, rotates
      emulator versions) assigned to a named Samsung team.

## 7. Recovery / break-glass contacts

- [ ] At least two named individuals (not a single point of failure) on
      the Samsung side with org-owner-level access documented in an
      internal (non-public) contact list.
- [ ] Equivalent break-glass contact retained on the originating side for
      a defined window, in case of an issue only discoverable from
      pre-transfer context.
- [ ] Incident/security contact path in `SECURITY.md` updated to Samsung's
      designated channel (see `SECURITY.md` §"Post-transfer requirement")
      with the interim GitHub private-vulnerability-reporting channel only
      removed once the new Samsung channel is tested and confirmed.

## 8. Mandatory rehearsal release

- [ ] **A full rehearsal release is performed under Samsung ownership
      before the first real public release from the new org.** This
      means: version bump → build → sign (with the new Samsung signing
      config) → pack → validate against the support matrix → publish to a
      **non-production feed** (e.g. a private/internal NuGet feed, or
      nuget.org in a throwaway/test package ID if a real dry run against
      the live feed is required) to prove the entire
      `.github/workflows/release.yml` pipeline works end-to-end with
      Samsung's environments, secrets, and runners.
- [ ] Rehearsal release sign-off recorded (who approved each environment
      gate, what was published where, and confirmation it was
      subsequently deleted/rolled back if pushed anywhere production-
      adjacent).
- [ ] Only after a successful rehearsal is the publish job in
      `.github/workflows/release.yml` allowed to target the real
      `Maui.Tizen.*` package IDs on the production nuget.org feed.

## 9. Communication

- [ ] README, package descriptions, and this checklist updated to remove
      "pending transfer" language once complete.
- [ ] Public announcement (blog/release notes) of the ownership change,
      including the new support/security contact channels.
