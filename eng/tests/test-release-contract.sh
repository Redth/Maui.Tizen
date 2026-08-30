#!/usr/bin/env bash
#
# Offline release-workflow regressions. All packages, API responses, attestations and feed
# contents are synthetic; this test never signs, publishes, or contacts a device.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

CONTRACT="$REPO_ROOT/eng/release/release-contract.py"
TEMP_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEMP_ROOT"' EXIT

FAILURES=0
VERSION="11.0.0-preview.1"
SOURCE_SHA="eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
SOURCE_REF="refs/heads/main"
RUN_ID="123"
RUN_ATTEMPT="2"
REPOSITORY="Redth/Maui.Tizen"
UNSIGNED_NAME="maui-tizen-unsigned-${VERSION}-run-${RUN_ID}-attempt-${RUN_ATTEMPT}"
SIGNED_NAME="maui-tizen-signed-${VERSION}-run-${RUN_ID}-attempt-${RUN_ATTEMPT}"
FINGERPRINT="$(printf 'f%.0s' {1..64})"

pass() { printf '\033[1;32m  PASS\033[0m %s\n' "$*"; }
fail() {
  printf '\033[1;31m  FAIL\033[0m %s\n' "$*"
  FAILURES=$((FAILURES + 1))
}

expect_success() {
  local label="$1"
  shift
  if "$@" >"$TEMP_ROOT/last.out" 2>&1; then
    pass "$label"
  else
    fail "$label"
    sed 's/^/        /' "$TEMP_ROOT/last.out"
  fi
}

expect_failure() {
  local label="$1"
  shift
  if "$@" >"$TEMP_ROOT/last.out" 2>&1; then
    fail "$label -- expected a non-zero exit"
  else
    pass "$label"
  fi
}

make_package() {
  local directory="$1" id="$2" version="$3" kind="$4" signed="${5:-false}"
  python3 - "$directory" "$id" "$version" "$kind" "$signed" <<'PY'
import pathlib
import sys
import zipfile

directory = pathlib.Path(sys.argv[1])
package_id, version, kind, signed = sys.argv[2:]
directory.mkdir(parents=True, exist_ok=True)
suffix = "snupkg" if kind == "symbols" else "nupkg"
path = directory / f"{package_id}.{version}.{suffix}"
nuspec = f"""<?xml version="1.0"?>
<package>
  <metadata>
    <id>{package_id}</id>
    <version>{version}</version>
    <authors>Release Test Org</authors>
    <company>Release Test Org</company>
    <description>Release contract test package.</description>
    <projectUrl>https://github.com/Redth/Maui.Tizen</projectUrl>
    <license type="expression">MIT</license>
    <tags>maui;tizen;dotnet</tags>
    <readme>README.md</readme>
    <icon>icon.png</icon>
    <repository type="git" url="https://github.com/Redth/Maui.Tizen" commit="eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" />
  </metadata>
</package>
"""
with zipfile.ZipFile(path, "w") as archive:
    archive.writestr(f"{package_id}.nuspec", nuspec)
    archive.writestr("README.md", "test\n")
    if kind == "package":
        if package_id == "Maui.Tizen.Build.Tasks":
            archive.writestr("buildTransitive/Maui.Tizen.Build.Tasks.dll", b"MZ synthetic task dll")
        elif package_id == "Maui.Tizen.Templates":
            archive.writestr("content/templates/maui-tizen/.template.config/template.json", b"{}")
        else:
            archive.writestr(f"lib/net11.0-tizen11.0/{package_id}.dll", b"MZ synthetic dll")
    if signed == "true":
        archive.writestr(".signature.p7s", b"synthetic signature")
PY
}

make_unsigned_set() {
  local directory="$1" version="${2:-$VERSION}"
  mkdir -p "$directory"
  for id in Maui.Tizen.Core Maui.Tizen.Build.Tasks; do
    make_package "$directory" "$id" "$version" package false
    make_package "$directory" "$id" "$version" symbols false
  done
  make_package "$directory" Maui.Tizen.Templates "$version" package false
}

create_unsigned() {
  local directory="$1"
  python3 "$CONTRACT" create-unsigned-manifest \
    --packages-dir "$directory" \
    --output "$directory/release-manifest.json" \
    --expected-package-ids-file "$TEMP_ROOT/package-ids.txt" \
    --repository "$REPOSITORY" \
    --version "$VERSION" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --artifact-name "$UNSIGNED_NAME"
}

verify_unsigned() {
  local directory="$1"
  python3 "$CONTRACT" verify-unsigned-manifest \
    --packages-dir "$directory" \
    --manifest "$directory/release-manifest.json" \
    --expected-package-ids-file "$TEMP_ROOT/package-ids.txt" \
    --repository "$REPOSITORY" \
    --version "$VERSION" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --artifact-name "$UNSIGNED_NAME"
}

printf '%s\n' Maui.Tizen.Build.Tasks Maui.Tizen.Core Maui.Tizen.Templates > "$TEMP_ROOT/package-ids.txt"

UNSIGNED="$TEMP_ROOT/unsigned"
make_unsigned_set "$UNSIGNED"
expect_success "exact unsigned release manifest is generated" create_unsigned "$UNSIGNED"
expect_success "exact unsigned release manifest verifies" verify_unsigned "$UNSIGNED"

TASKS_WITHOUT_SYMBOLS="$TEMP_ROOT/tasks-without-symbols"
make_unsigned_set "$TASKS_WITHOUT_SYMBOLS"
rm "$TASKS_WITHOUT_SYMBOLS/Maui.Tizen.Build.Tasks.${VERSION}.snupkg"
expect_success "Build.Tasks ships without a runtime symbol package" \
  create_unsigned "$TASKS_WITHOUT_SYMBOLS"

TEMPLATES_WITHOUT_SYMBOLS="$TEMP_ROOT/templates-without-symbols"
make_unsigned_set "$TEMPLATES_WITHOUT_SYMBOLS"
rm "$TEMPLATES_WITHOUT_SYMBOLS/Maui.Tizen.Build.Tasks.${VERSION}.snupkg"
expect_success "Templates ships without a runtime symbol package" \
  create_unsigned "$TEMPLATES_WITHOUT_SYMBOLS"

WRONG_VERSION="$TEMP_ROOT/wrong-version"
make_unsigned_set "$WRONG_VERSION" "11.0.0-alpha"
expect_failure "wrong package version is rejected" create_unsigned "$WRONG_VERSION"

MISSING="$TEMP_ROOT/missing"
make_unsigned_set "$MISSING"
rm "$MISSING/Maui.Tizen.Build.Tasks.${VERSION}.nupkg"
expect_failure "missing shipping package is rejected" create_unsigned "$MISSING"

EXTRA="$TEMP_ROOT/extra"
make_unsigned_set "$EXTRA"
make_package "$EXTRA" Maui.Tizen.Extra "$VERSION" package false
expect_failure "extra shipping package is rejected" create_unsigned "$EXTRA"

ARTIFACT_JSON="$TEMP_ROOT/artifact.json"
python3 - "$ARTIFACT_JSON" "$UNSIGNED_NAME" "$SOURCE_SHA" <<'PY'
import json, sys
json.dump({
    "id": 456,
    "name": sys.argv[2],
    "digest": "sha256:" + "a" * 64,
    "expired": False,
    "workflow_run": {
        "id": 123,
        "head_sha": sys.argv[3],
        "head_branch": "main"
    }
}, open(sys.argv[1], "w"))
PY

verify_artifact() {
  python3 "$CONTRACT" verify-artifact-metadata \
    --metadata "$ARTIFACT_JSON" \
    --artifact-id "${1:-456}" \
    --name "${5:-$UNSIGNED_NAME}" \
    --digest "${2:-sha256:$(printf 'a%.0s' {1..64})}" \
    --run-id "$RUN_ID" \
    --run-attempt "${3:-$RUN_ATTEMPT}" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "${4:-$SOURCE_REF}"
}

expect_success "exact artifact ID, digest, run and attempt verify" verify_artifact
expect_failure "wrong artifact ID is rejected" verify_artifact 999
expect_failure "wrong artifact name is rejected" \
  verify_artifact 456 "sha256:$(printf 'a%.0s' {1..64})" 2 "$SOURCE_REF" wrong-artifact
expect_failure "wrong artifact digest is rejected" verify_artifact 456 "sha256:$(printf 'b%.0s' {1..64})"
expect_failure "wrong artifact attempt is rejected" verify_artifact 456 "sha256:$(printf 'a%.0s' {1..64})" 3
expect_failure "wrong artifact ref is rejected" verify_artifact 456 "sha256:$(printf 'a%.0s' {1..64})" 2 refs/heads/other

REPOSITORY_JSON="$TEMP_ROOT/repository.json"
BRANCH_JSON="$TEMP_ROOT/branch.json"
printf '{"default_branch":"main"}\n' > "$REPOSITORY_JSON"
printf '{"commit":{"sha":"%s"}}\n' "$SOURCE_SHA" > "$BRANCH_JSON"
expect_success "current protected default-branch SHA is accepted" \
  python3 "$CONTRACT" verify-source \
    --repository-json "$REPOSITORY_JSON" \
    --branch-json "$BRANCH_JSON" \
    --source-ref "$SOURCE_REF" \
    --source-commit "$SOURCE_SHA" \
    --ref-protected true
expect_failure "arbitrary release ref is rejected" \
  python3 "$CONTRACT" verify-source \
    --repository-json "$REPOSITORY_JSON" \
    --branch-json "$BRANCH_JSON" \
    --source-ref refs/heads/other \
    --source-commit "$SOURCE_SHA" \
    --ref-protected true

SERVICING_POLICY="$TEMP_ROOT/servicing-policy.json"
python3 - "$REPO_ROOT/eng/release/release-policy.json" "$SERVICING_POLICY" <<'PY'
import json
import sys

policy = json.load(open(sys.argv[1]))
policy["servicingBranches"] = ["release/10.x"]
json.dump(policy, open(sys.argv[2], "w"))
PY
expect_success "configured protected servicing branch head is accepted" \
  python3 "$CONTRACT" verify-source \
    --policy "$SERVICING_POLICY" \
    --repository-json "$REPOSITORY_JSON" \
    --branch-json "$BRANCH_JSON" \
    --source-ref refs/heads/release/10.x \
    --source-commit "$SOURCE_SHA" \
    --ref-protected true

CHECK_RUNS_JSON="$TEMP_ROOT/check-runs.json"
python3 - "$REPO_ROOT/eng/release/release-policy.json" "$CHECK_RUNS_JSON" "$SOURCE_SHA" <<'PY'
import json
import sys

policy = json.load(open(sys.argv[1]))
checks = [
    {
        "id": index,
        "name": name,
        "head_sha": sys.argv[3],
        "status": "completed",
        "conclusion": "success",
        "app": {"slug": "github-actions"},
    }
    for index, name in enumerate(policy["requiredStatusChecks"], start=1)
]
json.dump([{"total_count": len(checks), "check_runs": checks}], open(sys.argv[2], "w"))
PY
expect_success "all required GitHub Actions checks passed for the exact SHA" \
  python3 "$CONTRACT" verify-required-checks \
    --check-runs-json "$CHECK_RUNS_JSON" \
    --source-commit "$SOURCE_SHA"

python3 - "$CHECK_RUNS_JSON" <<'PY'
import json
import sys

pages = json.load(open(sys.argv[1]))
latest = dict(pages[0]["check_runs"][0])
latest["id"] = 999
latest["conclusion"] = "failure"
pages[0]["check_runs"].append(latest)
json.dump(pages, open(sys.argv[1], "w"))
PY
expect_failure "a newer failed required check cannot be hidden by an older success" \
  python3 "$CONTRACT" verify-required-checks \
    --check-runs-json "$CHECK_RUNS_JSON" \
    --source-commit "$SOURCE_SHA"

WORKLOAD_ID="Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7"
WORKLOAD_VERSION="11.0.0-preview.7"
WORKLOAD_FEED="$TEMP_ROOT/workload-feed"
WORKLOAD_ROOT="$TEMP_ROOT/dotnet-root"
WORKLOAD_BASELINES="$TEMP_ROOT/workload-baselines.json"
WORKLOAD_PACKAGE="$WORKLOAD_FEED/$(printf '%s' "$WORKLOAD_ID" | tr '[:upper:]' '[:lower:]')/$WORKLOAD_VERSION/$(printf '%s' "$WORKLOAD_ID" | tr '[:upper:]' '[:lower:]').$WORKLOAD_VERSION.nupkg"
mkdir -p "$(dirname "$WORKLOAD_PACKAGE")" \
  "$WORKLOAD_ROOT/sdk-manifests/11.0.100-preview.7/samsung.net.sdk.tizen"
python3 - "$WORKLOAD_PACKAGE" "$WORKLOAD_ROOT" <<'PY'
import pathlib
import sys
import zipfile

package = pathlib.Path(sys.argv[1])
root = pathlib.Path(sys.argv[2])
manifest = b'{"version":"11.0.0-preview.7","workloads":{}}\n'
with zipfile.ZipFile(package, "w") as archive:
    archive.writestr("data/WorkloadManifest.json", manifest)
(root / "sdk-manifests/11.0.100-preview.7/samsung.net.sdk.tizen/WorkloadManifest.json").write_bytes(manifest)
PY
WORKLOAD_SHA="$(shasum -a 256 "$WORKLOAD_PACKAGE" | cut -d' ' -f1)"
cat > "$WORKLOAD_BASELINES" <<JSON
{"target":{"workloadManifest":{"activation":{"packageId":"$WORKLOAD_ID","version":"$WORKLOAD_VERSION","packageSha256":"$WORKLOAD_SHA","signerFingerprint":"0123456789ABCDEF"}}}}
JSON
FAKE_VERIFY="$TEMP_ROOT/fake-verify-dotnet"
printf '%s\n' '#!/usr/bin/env bash' 'exit 0' > "$FAKE_VERIFY"
chmod +x "$FAKE_VERIFY"
verify_workload() {
  python3 "$CONTRACT" verify-installed-workload \
    --baselines "$WORKLOAD_BASELINES" \
    --workload-id "$WORKLOAD_ID" \
    --workload-version "$WORKLOAD_VERSION" \
    --package-sha256 "$WORKLOAD_SHA" \
    --signer-fingerprint 0123456789ABCDEF \
    --dotnet "$FAKE_VERIFY" \
    --dotnet-root "$WORKLOAD_ROOT" \
    --feed-base "file://$WORKLOAD_FEED"
}
expect_success "installed workload bytes match the reviewed package" verify_workload
mkdir -p "$WORKLOAD_ROOT/sdk-manifests/11.0.100-preview.6/samsung.net.sdk.tizen"
cp \
  "$WORKLOAD_ROOT/sdk-manifests/11.0.100-preview.7/samsung.net.sdk.tizen/WorkloadManifest.json" \
  "$WORKLOAD_ROOT/sdk-manifests/11.0.100-preview.6/samsung.net.sdk.tizen/WorkloadManifest.json"
printf '%s\n' '{"substituted":true}' \
  > "$WORKLOAD_ROOT/sdk-manifests/11.0.100-preview.7/samsung.net.sdk.tizen/WorkloadManifest.json"
expect_failure "stale matching workload cannot mask an active substituted copy" verify_workload

SIGNED="$TEMP_ROOT/signed"
mkdir -p "$SIGNED"
for id in Maui.Tizen.Core Maui.Tizen.Build.Tasks; do
  make_package "$SIGNED" "$id" "$VERSION" package true
  make_package "$SIGNED" "$id" "$VERSION" symbols true
done
make_package "$SIGNED" Maui.Tizen.Templates "$VERSION" package true

AUTH_REPORT="$SIGNED/authenticode-report.json"
python3 - "$AUTH_REPORT" "$SIGNED" "$FINGERPRINT" <<'PY'
import hashlib
import json
import pathlib
import sys
import zipfile

output, directory, fingerprint = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
packages = []
for package in sorted(directory.glob("*.nupkg")):
    with zipfile.ZipFile(package) as archive:
        binaries = []
        for name in archive.namelist():
            if name.endswith(".dll"):
                binaries.append({
                    "path": name,
                    "sha256": hashlib.sha256(archive.read(name)).hexdigest(),
                    "status": "Valid",
                    "certificateSha256": fingerprint,
                })
    packages.append({
        "filename": package.name,
        "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
        "binaries": binaries,
    })
json.dump({"schemaVersion": 1, "certificateSha256": fingerprint, "packages": packages}, open(output, "w"))
PY

create_signed() {
  python3 "$CONTRACT" create-signed-manifest \
    --unsigned-dir "$UNSIGNED" \
    --unsigned-manifest "$UNSIGNED/release-manifest.json" \
    --signed-dir "$SIGNED" \
    --authenticode-report "$AUTH_REPORT" \
    --certificate-sha256 "$FINGERPRINT" \
    --artifact-name "$SIGNED_NAME" \
    --run-attempt "$RUN_ATTEMPT" \
    --output "$SIGNED/release-manifest.json" \
    --attestation-checksums "$SIGNED/attestation-subjects.sha256"
}

expect_success "signed outputs bind one-to-one to unsigned inputs" create_signed
expect_success "signed manifest verifies approved signer and exact bytes" \
  python3 "$CONTRACT" verify-signed-manifest \
    --signed-dir "$SIGNED" \
    --manifest "$SIGNED/release-manifest.json" \
    --certificate-sha256 "$FINGERPRINT" \
    --repository "$REPOSITORY" \
    --version "$VERSION" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --artifact-name "$SIGNED_NAME"
expect_failure "wrong approved signer is rejected" \
  python3 "$CONTRACT" verify-signed-manifest \
    --signed-dir "$SIGNED" \
    --manifest "$SIGNED/release-manifest.json" \
    --certificate-sha256 "$(printf '1%.0s' {1..64})" \
    --repository "$REPOSITORY" \
    --version "$VERSION" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --artifact-name "$SIGNED_NAME"

SUBSTITUTED="$TEMP_ROOT/substituted"
cp -R "$SIGNED" "$SUBSTITUTED"
python3 - "$SUBSTITUTED/Maui.Tizen.Core.${VERSION}.nupkg" "$SUBSTITUTED/authenticode-report.json" <<'PY'
import hashlib
import json
import pathlib
import sys
import zipfile

package = pathlib.Path(sys.argv[1])
report_path = pathlib.Path(sys.argv[2])
replacement = package.with_suffix(".replacement")
with zipfile.ZipFile(package) as source, zipfile.ZipFile(replacement, "w") as target:
    for item in source.infolist():
        data = source.read(item.filename)
        if item.filename == "README.md":
            data = b"substituted payload\n"
        target.writestr(item, data)
replacement.replace(package)
report = json.load(open(report_path))
for item in report["packages"]:
    if item["filename"] == package.name:
        item["sha256"] = hashlib.sha256(package.read_bytes()).hexdigest()
json.dump(report, open(report_path, "w"))
PY
expect_failure "signed package payload substitution is rejected" \
  python3 "$CONTRACT" create-signed-manifest \
    --unsigned-dir "$UNSIGNED" \
    --unsigned-manifest "$UNSIGNED/release-manifest.json" \
    --signed-dir "$SUBSTITUTED" \
    --authenticode-report "$SUBSTITUTED/authenticode-report.json" \
    --certificate-sha256 "$FINGERPRINT" \
    --artifact-name "$SIGNED_NAME" \
    --run-attempt "$RUN_ATTEMPT" \
    --output "$SUBSTITUTED/release-manifest.json" \
    --attestation-checksums "$SUBSTITUTED/attestation-subjects.sha256"

ATTESTATION="$TEMP_ROOT/attestation.json"
SUBJECT_NAME="Maui.Tizen.Core.${VERSION}.nupkg"
SUBJECT_DIGEST="$(python3 - "$SIGNED/release-manifest.json" "$SUBJECT_NAME" <<'PY'
import json, sys
m=json.load(open(sys.argv[1]))
print(next(f["sha256"] for p in m["packages"] for f in p["files"] if f["filename"] == sys.argv[2]))
PY
)"
python3 - "$ATTESTATION" "$REPOSITORY" "$SOURCE_SHA" "$SOURCE_REF" "$RUN_ID" "$RUN_ATTEMPT" "$SUBJECT_NAME" "$SUBJECT_DIGEST" <<'PY'
import json, sys
out, repo, sha, ref, run_id, attempt, name, digest = sys.argv[1:]
json.dump([{
  "verificationResult": {
    "signature": {"certificate": {"extensions": {
      "buildSignerURI": f"https://github.com/{repo}/.github/workflows/release.yml@{ref}",
      "buildSignerDigest": sha,
      "sourceRepositoryURI": f"https://github.com/{repo}",
      "sourceRepositoryDigest": sha,
      "sourceRepositoryRef": ref,
      "runInvocationURI": f"https://github.com/{repo}/actions/runs/{run_id}/attempts/{attempt}"
    }}},
    "statement": {
      "predicateType": "https://slsa.dev/provenance/v1",
      "subject": [{"name": name, "digest": {"sha256": digest}}]
    }
  }
}], open(out, "w"))
PY

verify_attestation() {
  python3 "$CONTRACT" verify-attestation-report \
    --report "$1" \
    --repository "$REPOSITORY" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --subject-name "$SUBJECT_NAME" \
    --subject-digest "$SUBJECT_DIGEST"
}

expect_success "attestation binds signer, source, subject and run attempt" verify_attestation "$ATTESTATION"

mutate_attestation() {
  local output="$1" path="$2" value="$3"
  python3 - "$ATTESTATION" "$output" "$path" "$value" <<'PY'
import json, sys
source, output, path, value = sys.argv[1:]
data=json.load(open(source))
node=data[0]
parts=path.split(".")
for part in parts[:-1]:
    node=node[int(part)] if part.isdigit() else node[part]
node[parts[-1]]=value
json.dump(data, open(output, "w"))
PY
}

mutate_attestation "$TEMP_ROOT/wrong-signer.json" \
  verificationResult.signature.certificate.extensions.buildSignerURI \
  "https://github.com/evil/repo/.github/workflows/release.yml@refs/heads/main"
expect_failure "wrong attestation signer is rejected" verify_attestation "$TEMP_ROOT/wrong-signer.json"

mutate_attestation "$TEMP_ROOT/wrong-source-digest.json" \
  verificationResult.signature.certificate.extensions.sourceRepositoryDigest \
  "$(printf '1%.0s' {1..40})"
expect_failure "wrong attestation source digest is rejected" verify_attestation "$TEMP_ROOT/wrong-source-digest.json"

mutate_attestation "$TEMP_ROOT/wrong-source-ref.json" \
  verificationResult.signature.certificate.extensions.sourceRepositoryRef \
  refs/heads/other
expect_failure "wrong attestation source ref is rejected" verify_attestation "$TEMP_ROOT/wrong-source-ref.json"

mutate_attestation "$TEMP_ROOT/wrong-attempt.json" \
  verificationResult.signature.certificate.extensions.runInvocationURI \
  "https://github.com/${REPOSITORY}/actions/runs/${RUN_ID}/attempts/1"
expect_failure "prior-attempt attestation is rejected" verify_attestation "$TEMP_ROOT/wrong-attempt.json"

mutate_attestation "$TEMP_ROOT/wrong-subject.json" \
  verificationResult.statement.subject.0.digest.sha256 \
  "$(printf '2%.0s' {1..64})"
expect_failure "wrong attestation subject digest is rejected" verify_attestation "$TEMP_ROOT/wrong-subject.json"

FAKE_GH="$TEMP_ROOT/gh"
cat > "$FAKE_GH" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
endpoint="${2:-}"
case "$endpoint" in
  repos/*/environments/*)
    printf '%s\n' '{"protection_rules":[],"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":false}}'
    ;;
  repos/*/rules/branches/*)
    printf '%s\n' '[]'
    ;;
  *)
    exit 64
    ;;
esac
SH
chmod +x "$FAKE_GH"
expect_failure "missing environment and ruleset protections block publishing" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --gh "$FAKE_GH"

FAKE_GH_READY="$TEMP_ROOT/gh-ready"
cat > "$FAKE_GH_READY" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
endpoint="${2:-}"
case "$endpoint" in
  repos/*/environments/*)
    printf '%s\n' '{"protection_rules":[{"type":"required_reviewers","reviewers":[{"type":"User","reviewer":{"login":"reviewer"}}]}],"deployment_branch_policy":{"protected_branches":true,"custom_branch_policies":false}}'
    ;;
  repos/*/rules/branches/*)
    cat <<'JSON'
[{"type":"pull_request","parameters":{"required_approving_review_count":1,"require_code_owner_review":true,"require_last_push_approval":true}},{"type":"deletion"},{"type":"non_fast_forward"},{"type":"required_linear_history"},{"type":"required_status_checks","parameters":{"strict_required_status_checks_policy":true,"required_status_checks":[{"context":"Build and test (no Tizen workload)"},{"context":"Build tasks under full-framework MSBuild (final lane)"},{"context":"Hosted validation (no Tizen workload)"},{"context":"Tizen device lane (informational)"},{"context":"Verify imported history"},{"context":"Tizen workload availability (external gate)"}]}}]
JSON
    ;;
  repos/*/rulesets/1)
    cat <<'JSON'
{"id":1,"target":"branch","enforcement":"active","bypass_actors":[],"conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"rules":[{"type":"pull_request","parameters":{"required_approving_review_count":1,"require_code_owner_review":true,"require_last_push_approval":true}},{"type":"deletion"},{"type":"non_fast_forward"},{"type":"required_linear_history"},{"type":"required_status_checks","parameters":{"strict_required_status_checks_policy":true,"required_status_checks":[{"context":"Build and test (no Tizen workload)"},{"context":"Build tasks under full-framework MSBuild (final lane)"},{"context":"Hosted validation (no Tizen workload)"},{"context":"Tizen device lane (informational)"},{"context":"Verify imported history"},{"context":"Tizen workload availability (external gate)"}]}}]}
JSON
    ;;
  repos/*/rulesets?*)
    printf '%s\n' '[{"id":1,"target":"branch","enforcement":"active"}]'
    ;;
  orgs/*/actions/runner-groups/7/repositories?*)
    printf '%s\n' '{"total_count":1,"repositories":[{"full_name":"Redth/Maui.Tizen"}]}'
    ;;
  orgs/*/actions/runner-groups/7/runners?*)
    printf '%s\n' '{"total_count":0,"runners":[]}'
    ;;
  orgs/*/actions/runner-groups/7)
    printf '%s\n' '{"id":7,"name":"maui-tizen-release","inherited":false,"visibility":"selected","allows_public_repositories":true,"restricted_to_workflows":true,"selected_workflows":["Redth/Maui.Tizen/.github/workflows/tizen-device-validation.yml@refs/heads/main"]}'
    ;;
  orgs/*/actions/runner-groups?*)
    [[ "$endpoint" == *"visible_to_repository=Maui.Tizen"* ]] || exit 65
    printf '%s\n' '{"total_count":1,"runner_groups":[{"id":7,"name":"maui-tizen-release"}]}'
    ;;
  repos/Redth/Maui.Tizen)
    printf '%s\n' '{"owner":{"login":"Redth","type":"Organization"}}'
    ;;
  *)
    exit 64
    ;;
esac
SH
chmod +x "$FAKE_GH_READY"
expect_success "complete environment and ruleset protections are accepted" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --gh "$FAKE_GH_READY"

FAKE_GH_SERVICING="$TEMP_ROOT/gh-servicing"
python3 - "$FAKE_GH_READY" "$FAKE_GH_SERVICING" <<'PY'
import pathlib
import sys

source = pathlib.Path(sys.argv[1]).read_text()
source = source.replace(
    '"include":["~DEFAULT_BRANCH"]',
    '"include":["~DEFAULT_BRANCH","refs/heads/release/10.x"]',
)
source = source.replace(
    '"selected_workflows":["Redth/Maui.Tizen/.github/workflows/tizen-device-validation.yml@refs/heads/main"]',
    '"selected_workflows":["Redth/Maui.Tizen/.github/workflows/tizen-device-validation.yml@refs/heads/main","Redth/Maui.Tizen/.github/workflows/tizen-device-validation.yml@refs/heads/release/10.x"]',
)
pathlib.Path(sys.argv[2]).write_text(source)
PY
chmod +x "$FAKE_GH_SERVICING"
expect_success "servicing releases require servicing-branch rules and runner workflow restriction" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --source-branch release/10.x \
    --policy "$SERVICING_POLICY" \
    --gh "$FAKE_GH_SERVICING"

FAKE_GH_INHERITED_RUNNERS="$TEMP_ROOT/gh-inherited-runners"
python3 - "$FAKE_GH_READY" "$FAKE_GH_INHERITED_RUNNERS" <<'PY'
import pathlib
import sys

source = pathlib.Path(sys.argv[1]).read_text()
pathlib.Path(sys.argv[2]).write_text(source.replace('"inherited":false', '"inherited":true'))
PY
chmod +x "$FAKE_GH_INHERITED_RUNNERS"
expect_failure "enterprise-inherited runner groups are rejected" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --gh "$FAKE_GH_INHERITED_RUNNERS"

FAKE_GH_SPLIT="$TEMP_ROOT/gh-split-rules"
cat > "$FAKE_GH_SPLIT" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
endpoint="${2:-}"
case "$endpoint" in
  repos/*/environments/*)
    printf '%s\n' '{"protection_rules":[{"type":"required_reviewers","reviewers":[{"type":"User","reviewer":{"login":"reviewer"}}]}],"deployment_branch_policy":{"protected_branches":true,"custom_branch_policies":false}}'
    ;;
  repos/*/rulesets/1)
    printf '%s\n' '{"id":1,"target":"branch","enforcement":"active","bypass_actors":[{"actor_type":"RepositoryRole","actor_id":5,"bypass_mode":"always"}],"conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"rules":[{"type":"pull_request","parameters":{"required_approving_review_count":1,"require_code_owner_review":true,"require_last_push_approval":true}},{"type":"deletion"},{"type":"non_fast_forward"},{"type":"required_linear_history"},{"type":"required_status_checks","parameters":{"strict_required_status_checks_policy":true,"required_status_checks":[{"context":"Build and test (no Tizen workload)"},{"context":"Build tasks under full-framework MSBuild (final lane)"},{"context":"Hosted validation (no Tizen workload)"},{"context":"Tizen device lane (informational)"},{"context":"Verify imported history"},{"context":"Tizen workload availability (external gate)"}]}}]}'
    ;;
  repos/*/rulesets/2)
    printf '%s\n' '{"id":2,"target":"branch","enforcement":"active","bypass_actors":[],"conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"rules":[]}'
    ;;
  repos/*/rulesets?*)
    printf '%s\n' '[{"id":1,"target":"branch","enforcement":"active"},{"id":2,"target":"branch","enforcement":"active"}]'
    ;;
  *)
    exit 64
    ;;
esac
SH
chmod +x "$FAKE_GH_SPLIT"
expect_failure "bypassable protections cannot be laundered through an empty safe ruleset" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --gh "$FAKE_GH_SPLIT"

FAKE_GH_HIDDEN_BYPASS="$TEMP_ROOT/gh-hidden-bypass"
python3 - "$FAKE_GH_READY" "$FAKE_GH_HIDDEN_BYPASS" <<'PY'
import pathlib
import sys

source = pathlib.Path(sys.argv[1]).read_text()
pathlib.Path(sys.argv[2]).write_text(source.replace('"bypass_actors":[],', ''))
PY
chmod +x "$FAKE_GH_HIDDEN_BYPASS"
expect_failure "ruleset bypass visibility is mandatory" \
  python3 "$CONTRACT" verify-protections \
    --repository "$REPOSITORY" \
    --default-branch main \
    --gh "$FAKE_GH_HIDDEN_BYPASS"

expect_success "ruleset wildcard does not cross ref path separators" \
  env PYTHONDONTWRITEBYTECODE=1 python3 - "$CONTRACT" <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("release_contract", sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
raise SystemExit(
    0
    if not module.github_ref_matches("refs/*main", "refs/heads/main", "main")
    else 1
)
PY

expect_failure "placeholder governance and package policies block publishing" \
  python3 "$CONTRACT" verify-policies \
    --support-matrix docs/governance/tizen-support-matrix.md \
    --packages-dir "$UNSIGNED" \
    --manifest "$UNSIGNED/release-manifest.json" \
    --expected-package-ids-file "$TEMP_ROOT/package-ids.txt" \
    --repository "$REPOSITORY" \
    --version "$VERSION" \
    --source-commit "$SOURCE_SHA" \
    --source-ref "$SOURCE_REF" \
    --run-id "$RUN_ID" \
    --run-attempt "$RUN_ATTEMPT" \
    --artifact-name "$UNSIGNED_NAME"

BASELINE_API="$TEMP_ROOT/api-baseline"
CURRENT_API="$TEMP_ROOT/api-current"
mkdir -p "$BASELINE_API" "$CURRENT_API"
expect_failure "historical upstream API reference cannot be selected as a release baseline" \
  python3 "$CONTRACT" verify-api-baseline \
    --baseline-dir "$REPO_ROOT/eng/api-baselines/net9.0-tizen7.0" \
    --version 9.0.120

BASELINE_GENERATOR_PACKAGES="$TEMP_ROOT/baseline-generator-packages"
mkdir -p "$BASELINE_GENERATOR_PACKAGES"
make_package "$BASELINE_GENERATOR_PACKAGES" Maui.Tizen.Build.Tasks "$VERSION" package false
make_package "$BASELINE_GENERATOR_PACKAGES" Maui.Tizen.Templates "$VERSION" package false
python3 - "$BASELINE_GENERATOR_PACKAGES/Maui.Tizen.Build.Tasks.${VERSION}.nupkg" <<'PY'
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1], "a") as archive:
    archive.writestr("buildTransitive/libSkiaSharp.dll", b"native dependency")
PY
expect_success "release package inspection includes only the package-owned buildTransitive task assembly" \
  python3 - "$CONTRACT" "$BASELINE_GENERATOR_PACKAGES/Maui.Tizen.Build.Tasks.${VERSION}.nupkg" <<'PY'
import importlib.util
import pathlib
import sys

spec = importlib.util.spec_from_file_location("release_contract", sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
binaries = module.read_package(pathlib.Path(sys.argv[2]))["binaries"]
assert "buildTransitive/Maui.Tizen.Build.Tasks.dll" in binaries
assert "buildTransitive/libSkiaSharp.dll" not in binaries
PY
BASELINE_GENERATOR_MANIFEST="$BASELINE_GENERATOR_PACKAGES/release-manifest.json"
BASELINE_GENERATOR_IDS="$TEMP_ROOT/baseline-generator-package-ids.txt"
printf '%s\n' Maui.Tizen.Build.Tasks Maui.Tizen.Templates > "$BASELINE_GENERATOR_IDS"
python3 "$CONTRACT" create-unsigned-manifest \
  --packages-dir "$BASELINE_GENERATOR_PACKAGES" \
  --output "$BASELINE_GENERATOR_MANIFEST" \
  --expected-package-ids-file "$BASELINE_GENERATOR_IDS" \
  --repository "$REPOSITORY" \
  --version "$VERSION" \
  --source-commit "$SOURCE_SHA" \
  --source-ref "$SOURCE_REF" \
  --run-id "$RUN_ID" \
  --run-attempt "$RUN_ATTEMPT" \
  --artifact-name "$UNSIGNED_NAME"
BASELINE_GENERATOR_OUTPUT="$REPO_ROOT/eng/api-baselines/.release-generator-probe-$$"
mkdir -p "$BASELINE_GENERATOR_OUTPUT"
printf 'preserve\n' > "$BASELINE_GENERATOR_OUTPUT/sentinel.txt"
expect_failure "standalone baseline generation rejects an incomplete package set" \
  pwsh -NoProfile -File "$REPO_ROOT/eng/scripts/generate-release-api-baseline.ps1" \
    -PackagesDirectory "$BASELINE_GENERATOR_PACKAGES" \
    -PackageVersion "$VERSION" \
    -ReleaseManifest "$BASELINE_GENERATOR_MANIFEST" \
    -OutputDirectory "$BASELINE_GENERATOR_OUTPUT"
if [[ "$(cat "$BASELINE_GENERATOR_OUTPUT/sentinel.txt" 2>/dev/null || true)" == "preserve" ]]; then
  pass "failed baseline generation preserves the previous baseline atomically"
else
  fail "failed baseline generation preserves the previous baseline atomically"
fi
rm -rf "$BASELINE_GENERATOR_OUTPUT"

cat > "$BASELINE_API/Test.json" <<'JSON'
{"schemaVersion":2,"types":[{"namespace":"Test","name":"Base","kind":"class","arity":0,"isAbstract":true,"members":[]},{"namespace":"Test","name":"Callback","kind":"delegate","arity":0,"delegateSignature":"Invoke(System.String) -> System.Void","delegateParameters":["1:value:None::"],"members":[]},{"namespace":"Test","name":"Contract","kind":"interface","arity":0,"interfaces":[],"members":[]},{"namespace":"Test","name":"Generic","kind":"class","arity":1,"genericConstraints":["T0:None:"],"members":[]},{"namespace":"Test","name":"Mode","kind":"enum","arity":0,"underlyingType":"System.Int32","members":[]},{"namespace":"Test","name":"Widget","kind":"class","arity":0,"members":[{"kind":"field","signature":"Default : System.String","isLiteral":true,"isInitOnly":false,"constantValue":"String:640065006600610075006C007400"},{"kind":"method","signature":"void Run(System.Int32)","isFinal":false,"isExtensionMethod":false,"parameters":["1:value:None::"]},{"kind":"property","signature":"Item[System.Int32] { get; set; } : System.Int32","getterAccessibility":"public","setterAccessibility":"public","parameters":["1:index:None::"]}]}]}
JSON
printf '%s\n' '{"schemaVersion":1,"packages":[]}' > "$BASELINE_API/manifest.json"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
expect_success "API compatibility ignores baseline metadata and accepts an unchanged surface" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
widget["members"]=[
    member for member in widget["members"]
    if member["signature"] != "void Run(System.Int32)"
]
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a removed member" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
APPROVED_BREAKS="$TEMP_ROOT/approved-api-breaks.json"
cat > "$APPROVED_BREAKS" <<'JSON'
{"approvedBreakingChanges":[{"difference":"Test.json: removed public member Test.Widget ('method', 'void Run(System.Int32)')","baselineVersion":"10.0.0","releaseMajor":11,"deprecationEvidence":"https://github.com/Redth/Maui.Tizen/issues/123"}]}
JSON
expect_failure "approved API breaks remain forbidden within the same major" \
  python3 "$CONTRACT" compare-api \
    --baseline-dir "$BASELINE_API" \
    --current-dir "$CURRENT_API" \
    --baseline-version 10.0.0 \
    --release-version 10.1.0 \
    --approved-breaks-json "$APPROVED_BREAKS"
expect_success "an exact reviewed API break is accepted only in a newer major" \
  python3 "$CONTRACT" compare-api \
    --baseline-dir "$BASELINE_API" \
    --current-dir "$CURRENT_API" \
    --baseline-version 10.0.0 \
    --release-version 11.0.0 \
    --approved-breaks-json "$APPROVED_BREAKS"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
expect_failure "stale major-version API approvals are rejected" \
  python3 "$CONTRACT" compare-api \
    --baseline-dir "$BASELINE_API" \
    --current-dir "$CURRENT_API" \
    --baseline-version 10.0.0 \
    --release-version 11.0.0 \
    --approved-breaks-json "$APPROVED_BREAKS"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Callback")["delegateSignature"]="Invoke(System.Int32) -> System.Void"
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a changed delegate signature" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Callback")["delegateParameters"]=["1:renamed:None::"]
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects changed delegate parameter metadata" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Mode")["underlyingType"]="System.Int64"
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a changed enum backing type" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Generic")["genericConstraints"]=["T0:ReferenceTypeConstraint:"]
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a tightened generic constraint" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
next(item for item in widget["members"] if item["kind"] == "property")["setterAccessibility"]="private"
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects reduced property accessor visibility" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
next(item for item in widget["members"] if item["kind"] == "method")["parameters"]=["1:renamed:None:"]
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects changed parameter metadata" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
next(item for item in widget["members"] if item["kind"] == "method")["isExtensionMethod"]=True
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects changed extension-method status" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
next(item for item in widget["members"] if item["kind"] == "field")["constantValue"]="String:6300680061006E00670065006400"
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects changed public field constants" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
widget=next(item for item in data["types"] if item["name"] == "Widget")
next(item for item in widget["members"] if item["kind"] == "method")["isFinal"]=True
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a newly sealed override" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Base")["members"].append(
    {"kind":"method","signature":"void NewRequirement()","isAbstract":True}
)
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a new abstract requirement" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
cp "$BASELINE_API/Test.json" "$CURRENT_API/Test.json"
python3 - "$CURRENT_API/Test.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
next(item for item in data["types"] if item["name"] == "Contract")["interfaces"]=["Test.NewBase"]
json.dump(data, open(sys.argv[1], "w"))
PY
expect_failure "API compatibility rejects a new base interface requirement" \
  python3 "$CONTRACT" compare-api --baseline-dir "$BASELINE_API" --current-dir "$CURRENT_API"
BASELINE_OUTPUT_SHA="$(shasum -a 256 "$BASELINE_API/Test.json" | cut -d' ' -f1)"
cat > "$BASELINE_API/manifest.json" <<JSON
{"schemaVersion":1,"baselineKind":"standalone-release","dumpSchemaVersion":2,"packageVersion":"10.0.0","targetFramework":"net11.0-tizen11.0","sourceCommit":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","sourceManifestSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","packages":[{"assembly":"Test.dll","outputSha256":"$BASELINE_OUTPUT_SHA"}],"msbuildFiles":[]}
JSON
expect_success "API baseline version and output hashes are bound" \
  python3 "$CONTRACT" verify-api-baseline \
    --baseline-dir "$BASELINE_API" \
    --version 10.0.0
expect_failure "API baseline policy version mismatch is rejected" \
  python3 "$CONTRACT" verify-api-baseline \
    --baseline-dir "$BASELINE_API" \
    --version 9.0.0

MSBUILD_PACKAGES="$TEMP_ROOT/msbuild-packages"
mkdir -p "$MSBUILD_PACKAGES" "$BASELINE_API/msbuild"
cp "$UNSIGNED/Maui.Tizen.Core.${VERSION}.nupkg" "$MSBUILD_PACKAGES/"
python3 - "$MSBUILD_PACKAGES/Maui.Tizen.Core.${VERSION}.nupkg" <<'PY'
import pathlib
import sys
import zipfile

package = pathlib.Path(sys.argv[1])
replacement = package.with_suffix(".replacement")
with zipfile.ZipFile(package) as source, zipfile.ZipFile(replacement, "w") as target:
    for item in source.infolist():
        target.writestr(item, source.read(item.filename))
    target.writestr(
        "buildTransitive/Maui.Tizen.Core.targets",
        "<Project><PropertyGroup><MauiTizenEnabled>true</MauiTizenEnabled></PropertyGroup></Project>\n",
    )
replacement.replace(package)
PY
printf '%s\n' '<Project><PropertyGroup><MauiTizenEnabled>true</MauiTizenEnabled></PropertyGroup></Project>' \
  > "$BASELINE_API/msbuild/Maui.Tizen.Core.targets"
MSBUILD_SHA="$(shasum -a 256 "$BASELINE_API/msbuild/Maui.Tizen.Core.targets" | cut -d' ' -f1)"
python3 - "$BASELINE_API/manifest.json" "$MSBUILD_SHA" <<'PY'
import json, sys
path, digest = sys.argv[1:]
data=json.load(open(path))
data["msbuildFiles"]=[{
    "packageId":"Maui.Tizen.Core",
    "packagePath":"buildTransitive/Maui.Tizen.Core.targets",
    "baselineFile":"msbuild/Maui.Tizen.Core.targets",
    "sha256":digest,
}]
json.dump(data, open(path, "w"))
PY
expect_success "MSBuild public API matches its hash-pinned baseline" \
  python3 "$CONTRACT" verify-msbuild-contracts \
    --packages-dir "$MSBUILD_PACKAGES" \
    --baseline-dir "$BASELINE_API" \
    --version 10.0.0
python3 - "$MSBUILD_PACKAGES/Maui.Tizen.Core.${VERSION}.nupkg" <<'PY'
import pathlib
import sys
import zipfile

package = pathlib.Path(sys.argv[1])
replacement = package.with_suffix(".replacement")
with zipfile.ZipFile(package) as source, zipfile.ZipFile(replacement, "w") as target:
    for item in source.infolist():
        data = source.read(item.filename)
        if item.filename == "buildTransitive/Maui.Tizen.Core.targets":
            data = b"<Project><PropertyGroup><MauiTizenEnabled>false</MauiTizenEnabled></PropertyGroup></Project>\n"
        target.writestr(item, data)
replacement.replace(package)
PY
expect_failure "MSBuild public API changes are rejected" \
  python3 "$CONTRACT" verify-msbuild-contracts \
    --packages-dir "$MSBUILD_PACKAGES" \
    --baseline-dir "$BASELINE_API" \
    --version 10.0.0

EXISTING="$TEMP_ROOT/existing"
mkdir -p "$EXISTING"
cp "$SIGNED/Maui.Tizen.Core.${VERSION}.nupkg" "$EXISTING/"
python3 - "$EXISTING/Maui.Tizen.Core.${VERSION}.nupkg" <<'PY'
import pathlib
import sys
import zipfile

package = pathlib.Path(sys.argv[1])
replacement = package.with_suffix(".repository-signed")
with zipfile.ZipFile(package) as source, zipfile.ZipFile(replacement, "w") as target:
    for item in source.infolist():
        data = source.read(item.filename)
        if item.filename == ".signature.p7s":
            data = b"synthetic repository countersignature"
        target.writestr(item, data)
replacement.replace(package)
PY
PLAN="$TEMP_ROOT/publication-plan.json"
FAKE_PUSH="$TEMP_ROOT/fake-dotnet"
PUSH_LOG="$TEMP_ROOT/push.log"
cat > "$FAKE_PUSH" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${2:-}" == "verify" ]]; then
  exit 0
fi
printf '%s\n' "$*" >> "$PUSH_LOG"
SH
chmod +x "$FAKE_PUSH"
expect_success "partial retry accepts repository-signature transformation only" \
  env PUSH_LOG="$PUSH_LOG" python3 "$CONTRACT" plan-publication \
    --manifest "$SIGNED/release-manifest.json" \
    --existing-packages-dir "$EXISTING" \
    --certificate-sha256 "$FINGERPRINT" \
    --dotnet "$FAKE_PUSH" \
    --output "$PLAN"
python3 - "$PLAN" <<'PY' || fail "partial retry plans primary and symbol packages independently"
import json, sys
actions=sorted(item["action"] for item in json.load(open(sys.argv[1]))["packages"])
raise SystemExit(0 if actions == ["publish", "publish", "publish", "publish", "skip"] else 1)
PY
pass "partial retry plans primary and symbol packages independently"

: > "$PUSH_LOG"
expect_success "partial retry executes only missing publication" \
  env MAUI_TIZEN_NUGET_TOKEN=synthetic PUSH_LOG="$PUSH_LOG" \
    python3 "$CONTRACT" plan-publication \
      --manifest "$SIGNED/release-manifest.json" \
      --existing-packages-dir "$EXISTING" \
      --output "$PLAN" \
      --execute \
      --source https://api.nuget.org/v3/index.json \
      --certificate-sha256 "$FINGERPRINT" \
      --dotnet "$FAKE_PUSH"
if [[ "$(wc -l < "$PUSH_LOG" | tr -d ' ')" == "4" ]] \
    && grep -Fq "Maui.Tizen.Build.Tasks.${VERSION}.nupkg" "$PUSH_LOG" \
    && grep -Fq "Maui.Tizen.Build.Tasks.${VERSION}.snupkg" "$PUSH_LOG" \
    && grep -Fq "Maui.Tizen.Core.${VERSION}.snupkg" "$PUSH_LOG" \
    && grep -Fq "Maui.Tizen.Templates.${VERSION}.nupkg" "$PUSH_LOG" \
    && ! grep -Fq "Maui.Tizen.Core.${VERSION}.nupkg" "$PUSH_LOG"; then
  pass "partial retry pushes only missing primary and symbol packages"
else
  fail "partial retry pushes only missing primary and symbol packages"
fi

printf 'substituted bytes\n' > "$EXISTING/Maui.Tizen.Core.${VERSION}.nupkg"
: > "$PUSH_LOG"
expect_failure "partial retry rejects a mismatched existing package" \
  env MAUI_TIZEN_NUGET_TOKEN=synthetic PUSH_LOG="$PUSH_LOG" \
    python3 "$CONTRACT" plan-publication \
      --manifest "$SIGNED/release-manifest.json" \
      --existing-packages-dir "$EXISTING" \
      --output "$PLAN" \
      --execute \
      --source https://api.nuget.org/v3/index.json \
      --certificate-sha256 "$FINGERPRINT" \
      --dotnet "$FAKE_PUSH"
if [[ ! -s "$PUSH_LOG" ]]; then
  pass "mismatched existing package blocks every push"
else
  fail "mismatched existing package blocks every push"
fi

echo
if [[ $FAILURES -ne 0 ]]; then
  fail "$FAILURES release contract regression(s) failed"
  exit 1
fi
pass "All release contract regressions passed"
