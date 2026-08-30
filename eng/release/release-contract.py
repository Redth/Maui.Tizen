#!/usr/bin/env python3
"""Fail-closed release artifact and policy verification for Maui.Tizen."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_POLICY = ROOT / "eng/release/release-policy.json"
DEFAULT_BASELINES = ROOT / "eng/baselines.json"
DEFAULT_CONTRACTS = ROOT / "eng/validation/package-contents"
SEMVER = re.compile(
    r"^(?P<major>0|[1-9]\d*)\.(?P<minor>0|[1-9]\d*)\.(?P<patch>0|[1-9]\d*)"
    r"(?P<label>-(?:preview|rc)\.[0-9]+)?$"
)
SHA256 = re.compile(r"^[0-9a-f]{64}$")
COMMIT_SHA = re.compile(r"^[0-9a-f]{40}$")
PLACEHOLDER = re.compile(r"\b(?:TBD|TODO|PLACEHOLDER|NOT[- ]READY)\b", re.IGNORECASE)
SYMBOLS_OPTIONAL_PACKAGE_IDS = frozenset(
    {"Maui.Tizen.Build.Tasks", "Maui.Tizen.Templates"}
)
AUTHENTICODE_OPTIONAL_PACKAGE_IDS = frozenset({"Maui.Tizen.Templates"})


class ContractError(RuntimeError):
    pass


def fail(message: str) -> None:
    raise ContractError(message)


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Could not read JSON from {path}: {exc}")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def normalized_fingerprint(value: str) -> str:
    return re.sub(r"[^0-9A-Fa-f]", "", value).lower()


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def metadata_child(metadata: ET.Element, name: str) -> ET.Element | None:
    return next((child for child in metadata if local_name(child.tag) == name), None)


def metadata_text(metadata: ET.Element, name: str) -> str:
    element = metadata_child(metadata, name)
    return (element.text or "").strip() if element is not None else ""


def read_package(path: Path) -> dict[str, Any]:
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            nuspecs = [name for name in names if name.lower().endswith(".nuspec")]
            if len(nuspecs) != 1:
                fail(f"{path.name} must contain exactly one .nuspec; found {len(nuspecs)}")
            root = ET.fromstring(archive.read(nuspecs[0]))
            metadata = next(
                (element for element in root.iter() if local_name(element.tag) == "metadata"),
                None,
            )
            if metadata is None:
                fail(f"{path.name} has no nuspec metadata element")
            repository = metadata_child(metadata, "repository")
            license_element = metadata_child(metadata, "license")
            package_id = metadata_text(metadata, "id")
            return {
                "id": package_id,
                "version": metadata_text(metadata, "version"),
                "authors": metadata_text(metadata, "authors"),
                "company": metadata_text(metadata, "company"),
                "description": metadata_text(metadata, "description"),
                "projectUrl": metadata_text(metadata, "projectUrl"),
                "license": (license_element.text or "").strip()
                if license_element is not None
                else "",
                "licenseType": license_element.attrib.get("type", "")
                if license_element is not None
                else "",
                "tags": metadata_text(metadata, "tags"),
                "readme": metadata_text(metadata, "readme"),
                "icon": metadata_text(metadata, "icon"),
                "repositoryUrl": repository.attrib.get("url", "")
                if repository is not None
                else "",
                "repositoryType": repository.attrib.get("type", "")
                if repository is not None
                else "",
                "repositoryCommit": repository.attrib.get("commit", "")
                if repository is not None
                else "",
                "signed": any(name.lower() == ".signature.p7s" for name in names),
                "entries": sorted(name.replace("\\", "/") for name in names),
                "binaries": sorted(
                    name.replace("\\", "/")
                    for name in names
                    if name.lower().endswith(".dll")
                    and (
                        name.split("/", 1)[0].lower()
                        in {"lib", "ref", "runtimes", "tasks", "tools"}
                        or name.replace("\\", "/").lower()
                        == f"buildtransitive/{package_id}.dll".lower()
                    )
                ),
            }
    except (OSError, zipfile.BadZipFile, ET.ParseError) as exc:
        fail(f"Could not inspect package {path}: {exc}")


def expected_ids(path: Path | None, contracts_dir: Path) -> list[str]:
    if path is not None:
        ids = [
            line.strip()
            for line in path.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]
    else:
        suffix = ".contract.txt"
        ids = sorted(
            item.name[: -len(suffix)]
            for item in contracts_dir.glob(f"*{suffix}")
            if item.is_file()
        )
    if not ids or len(ids) != len(set(ids)):
        fail("Expected package IDs must be a non-empty unique list")
    return sorted(ids)


def validate_version(version: str, baselines_path: Path) -> None:
    match = SEMVER.fullmatch(version)
    if match is None:
        fail(
            f"Package version '{version}' is not an allowed release SemVer. "
            "Use major.minor.patch, -preview.N, or -rc.N with no build metadata."
        )
    baselines = load_json(baselines_path)
    expected_major = str(baselines["target"]["dotNetVersion"]).split(".", 1)[0]
    if match.group("major") != expected_major:
        fail(
            f"Package version '{version}' is not aligned with the .NET "
            f"{expected_major} release train"
        )


def package_archives(directory: Path) -> tuple[list[Path], list[Path]]:
    return sorted(directory.glob("*.nupkg")), sorted(directory.glob("*.snupkg"))


def collect_unsigned_packages(
    directory: Path, version: str, package_ids: list[str]
) -> list[dict[str, Any]]:
    nupkgs, snupkgs = package_archives(directory)
    all_paths = nupkgs + snupkgs
    if not all_paths:
        fail(f"No package archives were found in {directory}")

    by_identity: dict[tuple[str, str], Path] = {}
    inspected: dict[Path, dict[str, Any]] = {}
    expected_set = set(package_ids)

    for path in all_paths:
        info = read_package(path)
        inspected[path] = info
        if info["id"] not in expected_set:
            fail(f"Unexpected shipping package '{info['id']}' in {path.name}")
        if info["version"] != version:
            fail(
                f"{path.name} contains version '{info['version']}', expected requested "
                f"version '{version}'"
            )
        if info["signed"]:
            fail(f"Unsigned release input {path.name} is already NuGet-signed")
        kind = "symbols" if path.suffix == ".snupkg" else "package"
        key = (info["id"], kind)
        if key in by_identity:
            fail(f"Duplicate {kind} archive for {info['id']}")
        expected_filename = f"{info['id']}.{version}.{path.suffix.lstrip('.')}"
        if path.name != expected_filename:
            fail(
                f"{path.name} is not the exact expected filename "
                f"'{expected_filename}'"
            )
        by_identity[key] = path

    entries: list[dict[str, Any]] = []
    for package_id in package_ids:
        package_path = by_identity.get((package_id, "package"))
        symbols_path = by_identity.get((package_id, "symbols"))
        symbols_required = package_id not in SYMBOLS_OPTIONAL_PACKAGE_IDS
        if package_path is None or (symbols_required and symbols_path is None):
            missing = []
            if package_path is None:
                missing.append(".nupkg")
            if symbols_required and symbols_path is None:
                missing.append(".snupkg")
            fail(f"{package_id} is missing required {' and '.join(missing)} output")
        files = [
            {
                "kind": "package",
                "filename": package_path.name,
                "sha256": sha256_file(package_path),
            }
        ]
        if symbols_path is not None:
            files.append(
                {
                    "kind": "symbols",
                    "filename": symbols_path.name,
                    "sha256": sha256_file(symbols_path),
                }
            )
        entries.append({"id": package_id, "version": version, "files": files})
    return entries


def workload_contract(baselines_path: Path) -> dict[str, Any]:
    workload = load_json(baselines_path)["target"]["workloadManifest"]
    activation = workload.get("activation") or {}
    return {
        "id": activation.get("packageId"),
        "version": activation.get("version"),
        "packageSha256": activation.get("packageSha256"),
        "signerFingerprint": activation.get("signerFingerprint"),
    }


def create_unsigned_manifest(args: argparse.Namespace) -> None:
    validate_version(args.version, args.baselines)
    if not COMMIT_SHA.fullmatch(args.source_commit):
        fail("Source commit must be a full lowercase 40-character Git SHA")
    if not args.source_ref.startswith("refs/heads/"):
        fail("Release source ref must be a branch ref")
    if not args.run_id.isdigit() or not args.run_attempt.isdigit():
        fail("Workflow run ID and attempt must be decimal integers")

    ids = expected_ids(args.expected_package_ids_file, args.contracts_dir)
    packages = collect_unsigned_packages(args.packages_dir, args.version, ids)
    manifest = {
        "schemaVersion": 1,
        "kind": "unsigned",
        "repository": args.repository,
        "packageVersion": args.version,
        "sourceCommit": args.source_commit,
        "sourceRef": args.source_ref,
        "workflowRunId": args.run_id,
        "workflowRunAttempt": args.run_attempt,
        "artifactName": args.artifact_name,
        "workloadManifest": workload_contract(args.baselines),
        "packages": packages,
    }
    write_json(args.output, manifest)
    verify_unsigned_directory(
        args.packages_dir,
        manifest,
        ids,
        allowed_supporting={args.output.name},
    )


def manifest_files(manifest: dict[str, Any]) -> list[dict[str, Any]]:
    files: list[dict[str, Any]] = []
    for package in manifest.get("packages", []):
        for item in package.get("files", []):
            files.append({"package": package, **item})
    return files


def verify_manifest_header(
    manifest: dict[str, Any],
    kind: str,
    expected: argparse.Namespace | None = None,
) -> None:
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != kind:
        fail(f"Expected a schemaVersion 1 {kind} release manifest")
    if expected is None:
        return
    checks = {
        "repository": expected.repository,
        "packageVersion": expected.version,
        "sourceCommit": expected.source_commit,
        "sourceRef": expected.source_ref,
        "workflowRunId": expected.run_id,
        "workflowRunAttempt": expected.run_attempt,
        "artifactName": expected.artifact_name,
    }
    for key, value in checks.items():
        if value is not None and manifest.get(key) != value:
            fail(
                f"Manifest {key} is '{manifest.get(key)}', expected '{value}'"
            )


def verify_unsigned_directory(
    directory: Path,
    manifest: dict[str, Any],
    ids: list[str],
    allowed_supporting: set[str] | None = None,
) -> None:
    verify_manifest_header(manifest, "unsigned")
    packages = manifest.get("packages")
    if not isinstance(packages, list):
        fail("Manifest packages must be an array")
    actual_ids = [item.get("id") for item in packages]
    if actual_ids != ids:
        fail(f"Manifest package IDs {actual_ids} do not exactly match {ids}")
    for package in packages:
        kinds = [item.get("kind") for item in package.get("files", [])]
        expected_kinds = ["package"]
        if package.get("id") not in SYMBOLS_OPTIONAL_PACKAGE_IDS:
            expected_kinds.append("symbols")
        if kinds not in (expected_kinds, ["package", "symbols"]):
            fail(
                f"Manifest file kinds for {package.get('id')} are {kinds}, "
                f"expected {expected_kinds}"
            )

    expected_names = set(allowed_supporting or {"release-manifest.json"})
    for item in manifest_files(manifest):
        filename = item.get("filename")
        digest = item.get("sha256")
        if not isinstance(filename, str) or not SHA256.fullmatch(str(digest)):
            fail("Every manifest file needs an exact filename and lowercase SHA-256")
        path = directory / filename
        if not path.is_file():
            fail(f"Manifest file is missing: {filename}")
        if sha256_file(path) != digest:
            fail(f"Manifest SHA-256 mismatch for {filename}")
        info = read_package(path)
        package = item["package"]
        if info["id"] != package.get("id") or info["version"] != package.get(
            "version"
        ):
            fail(f"Manifest identity does not match {filename}")
        if info["signed"]:
            fail(f"Unsigned artifact contains a signed package: {filename}")
        expected_names.add(filename)

    actual_names = {path.name for path in directory.iterdir() if path.is_file()}
    extras = sorted(actual_names - expected_names)
    missing = sorted(expected_names - actual_names)
    if extras or missing:
        fail(
            f"Unsigned artifact file set mismatch; missing={missing}, extra={extras}"
        )


def verify_unsigned_manifest(args: argparse.Namespace) -> None:
    manifest = load_json(args.manifest)
    verify_manifest_header(manifest, "unsigned", args)
    ids = expected_ids(args.expected_package_ids_file, args.contracts_dir)
    verify_unsigned_directory(
        args.packages_dir, manifest, ids, allowed_supporting={args.manifest.name}
    )


def load_signature_report(
    report_path: Path, signed_dir: Path, fingerprint: str
) -> dict[str, Any]:
    report = load_json(report_path)
    if report.get("schemaVersion") != 1 or not isinstance(
        report.get("packages"), list
    ):
        fail("Authenticode report must use schemaVersion 1 and contain packages")
    approved = normalized_fingerprint(fingerprint)
    if not SHA256.fullmatch(approved):
        fail("Approved signing certificate must be a SHA-256 fingerprint")

    report_packages = {
        item.get("filename"): item for item in report["packages"]
    }
    for package_path in sorted(signed_dir.glob("*.nupkg")):
        package_report = report_packages.get(package_path.name)
        if package_report is None:
            fail(f"Authenticode report is missing {package_path.name}")
        if package_report.get("sha256") != sha256_file(package_path):
            fail(f"Authenticode report hash mismatch for {package_path.name}")
        package_info = read_package(package_path)
        expected_binaries = set(package_info["binaries"])
        actual_binaries = {
            item.get("path") for item in package_report.get("binaries", [])
        }
        if expected_binaries != actual_binaries:
            fail(
                f"Authenticode report binary set mismatch for {package_path.name}"
            )
        if (
            not expected_binaries
            and package_info["id"] not in AUTHENTICODE_OPTIONAL_PACKAGE_IDS
        ):
            fail(f"Shipping package {package_path.name} contains no managed binaries")
        for binary in package_report.get("binaries", []):
            if binary.get("status") != "Valid":
                fail(
                    f"Authenticode signature is not valid for "
                    f"{package_path.name}:{binary.get('path')}"
                )
            if (
                normalized_fingerprint(str(binary.get("certificateSha256", "")))
                != approved
            ):
                fail(
                    f"Wrong Authenticode signer for "
                    f"{package_path.name}:{binary.get('path')}"
                )
    if set(report_packages) != {path.name for path in signed_dir.glob("*.nupkg")}:
        fail("Authenticode report contains substituted or extra packages")
    return report


def canonical_authenticode_payload(data: bytes, label: str) -> bytes:
    if len(data) < 0x40 or data[:2] != b"MZ":
        fail(f"Managed binary is not a valid PE image: {label}")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    optional_header = pe_offset + 4 + 20
    if (
        pe_offset + 4 > len(data)
        or data[pe_offset : pe_offset + 4] != b"PE\0\0"
        or optional_header + 2 > len(data)
    ):
        fail(f"Managed binary has an invalid PE header: {label}")
    magic = struct.unpack_from("<H", data, optional_header)[0]
    if magic == 0x10B:
        data_directories = optional_header + 96
    elif magic == 0x20B:
        data_directories = optional_header + 112
    else:
        fail(f"Managed binary has an unknown PE optional-header format: {label}")

    checksum_offset = optional_header + 64
    certificate_directory = data_directories + (8 * 4)
    if certificate_directory + 8 > len(data) or checksum_offset + 4 > len(data):
        fail(f"Managed binary PE headers are truncated: {label}")

    canonical = bytearray(data)
    canonical[checksum_offset : checksum_offset + 4] = b"\0" * 4
    certificate_offset, certificate_size = struct.unpack_from(
        "<II", canonical, certificate_directory
    )
    canonical[certificate_directory : certificate_directory + 8] = b"\0" * 8
    if certificate_offset == 0 and certificate_size == 0:
        return bytes(canonical)
    if (
        certificate_offset == 0
        or certificate_size == 0
        or certificate_offset + certificate_size > len(canonical)
    ):
        fail(f"Managed binary has an invalid Authenticode certificate table: {label}")
    return bytes(
        canonical[:certificate_offset]
        + canonical[certificate_offset + certificate_size :]
    )


def verify_authenticode_only_change(
    unsigned: bytes, signed: bytes, label: str
) -> None:
    if unsigned == signed:
        return
    canonical_unsigned = canonical_authenticode_payload(unsigned, label)
    canonical_signed = canonical_authenticode_payload(signed, label)
    if canonical_signed == canonical_unsigned:
        return
    if canonical_signed.startswith(canonical_unsigned) and all(
        value == 0 for value in canonical_signed[len(canonical_unsigned) :]
    ):
        return
    fail(f"Signed binary payload changed beyond Authenticode metadata: {label}")


def verify_signed_package_payload(unsigned_path: Path, signed_path: Path) -> None:
    try:
        with zipfile.ZipFile(unsigned_path) as unsigned_archive, zipfile.ZipFile(
            signed_path
        ) as signed_archive:
            unsigned_names = [
                name for name in unsigned_archive.namelist() if not name.endswith("/")
            ]
            signed_names = [
                name for name in signed_archive.namelist() if not name.endswith("/")
            ]
            if len({name.lower() for name in unsigned_names}) != len(unsigned_names):
                fail(f"Unsigned package has duplicate/case-colliding entries: {unsigned_path.name}")
            if len({name.lower() for name in signed_names}) != len(signed_names):
                fail(f"Signed package has duplicate/case-colliding entries: {signed_path.name}")
            signature_names = [
                name for name in signed_names if name.lower() == ".signature.p7s"
            ]
            if signature_names != [".signature.p7s"]:
                fail(f"Signed package must add exactly one .signature.p7s: {signed_path.name}")
            payload_names = [name for name in signed_names if name != ".signature.p7s"]
            if set(payload_names) != set(unsigned_names):
                fail(f"Signed package entry set differs from unsigned input: {signed_path.name}")
            for name in unsigned_names:
                unsigned_bytes = unsigned_archive.read(name)
                signed_bytes = signed_archive.read(name)
                if name.lower().endswith(".dll"):
                    verify_authenticode_only_change(
                        unsigned_bytes,
                        signed_bytes,
                        f"{signed_path.name}:{name}",
                    )
                elif unsigned_bytes != signed_bytes:
                    fail(f"Signed package substituted payload: {signed_path.name}:{name}")
    except (OSError, zipfile.BadZipFile, KeyError) as exc:
        fail(f"Could not compare signed package payload {signed_path}: {exc}")


def create_signed_manifest(args: argparse.Namespace) -> None:
    if not args.run_attempt.isdigit():
        fail("Signed workflow run attempt must be a decimal integer")
    unsigned = load_json(args.unsigned_manifest)
    verify_manifest_header(unsigned, "unsigned")
    ids = [package["id"] for package in unsigned["packages"]]
    verify_unsigned_directory(
        args.unsigned_dir,
        unsigned,
        ids,
        allowed_supporting={args.unsigned_manifest.name},
    )

    unsigned_files = {
        item["filename"]: item for item in manifest_files(unsigned)
    }
    signed_paths = sorted(args.signed_dir.glob("*.nupkg")) + sorted(
        args.signed_dir.glob("*.snupkg")
    )
    if {path.name for path in signed_paths} != set(unsigned_files):
        fail("Signed package filenames do not match unsigned inputs one-to-one")

    report = load_signature_report(
        args.authenticode_report, args.signed_dir, args.certificate_sha256
    )
    signed_packages: list[dict[str, Any]] = []
    for unsigned_package in unsigned["packages"]:
        files: list[dict[str, Any]] = []
        for unsigned_file in unsigned_package["files"]:
            path = args.signed_dir / unsigned_file["filename"]
            verify_signed_package_payload(
                args.unsigned_dir / unsigned_file["filename"], path
            )
            info = read_package(path)
            if not info["signed"]:
                fail(f"Signed output is not NuGet-signed: {path.name}")
            if (
                info["id"] != unsigned_package["id"]
                or info["version"] != unsigned_package["version"]
            ):
                fail(f"Signed output substituted package identity: {path.name}")
            signed_digest = sha256_file(path)
            if signed_digest == unsigned_file["sha256"]:
                fail(f"Signing did not change package bytes: {path.name}")
            files.append(
                {
                    "kind": unsigned_file["kind"],
                    "filename": path.name,
                    "unsignedSha256": unsigned_file["sha256"],
                    "sha256": signed_digest,
                }
            )
        signed_packages.append(
            {
                "id": unsigned_package["id"],
                "version": unsigned_package["version"],
                "files": files,
            }
        )

    manifest = {
        "schemaVersion": 1,
        "kind": "signed",
        "repository": unsigned["repository"],
        "packageVersion": unsigned["packageVersion"],
        "sourceCommit": unsigned["sourceCommit"],
        "sourceRef": unsigned["sourceRef"],
        "workflowRunId": unsigned["workflowRunId"],
        "workflowRunAttempt": args.run_attempt,
        "artifactName": args.artifact_name,
        "unsignedArtifactName": unsigned["artifactName"],
        "unsignedWorkflowRunAttempt": unsigned["workflowRunAttempt"],
        "unsignedManifestSha256": sha256_file(args.unsigned_manifest),
        "signingCertificateSha256": normalized_fingerprint(
            args.certificate_sha256
        ),
        "authenticodeReport": {
            "filename": args.authenticode_report.name,
            "sha256": sha256_file(args.authenticode_report),
        },
        "workloadManifest": unsigned["workloadManifest"],
        "packages": signed_packages,
    }
    write_json(args.output, manifest)
    checksums = []
    for item in manifest_files(manifest):
        checksums.append(f"{item['sha256']}  {item['filename']}")
    args.attestation_checksums.write_text(
        "\n".join(checksums) + "\n", encoding="utf-8"
    )
    verify_signed_directory(
        args.signed_dir,
        manifest,
        allowed_supporting={
            args.output.name,
            args.authenticode_report.name,
            args.attestation_checksums.name,
        },
    )


def verify_signed_directory(
    directory: Path,
    manifest: dict[str, Any],
    allowed_supporting: set[str] | None = None,
) -> None:
    verify_manifest_header(manifest, "signed")
    expected_names = set(
        allowed_supporting
        or {
            "release-manifest.json",
            "authenticode-report.json",
            "attestation-subjects.sha256",
        }
    )
    seen: set[tuple[str, str]] = set()
    for item in manifest_files(manifest):
        filename = item.get("filename")
        digest = item.get("sha256")
        unsigned_digest = item.get("unsignedSha256")
        if not isinstance(filename, str) or not SHA256.fullmatch(str(digest)):
            fail("Signed manifest contains an invalid filename or SHA-256")
        if not SHA256.fullmatch(str(unsigned_digest)):
            fail("Signed manifest is missing the unsigned input SHA-256")
        path = directory / filename
        if not path.is_file() or sha256_file(path) != digest:
            fail(f"Signed manifest SHA-256 mismatch for {filename}")
        info = read_package(path)
        package = item["package"]
        if (
            info["id"] != package.get("id")
            or info["version"] != package.get("version")
            or not info["signed"]
        ):
            fail(f"Signed package identity/signature mismatch for {filename}")
        key = (info["id"], item.get("kind"))
        if key in seen:
            fail(f"Duplicate signed package entry for {key}")
        seen.add(key)
        expected_names.add(filename)

    report = manifest.get("authenticodeReport") or {}
    report_path = directory / str(report.get("filename", ""))
    if not report_path.is_file() or sha256_file(report_path) != report.get(
        "sha256"
    ):
        fail("Signed manifest does not bind the Authenticode report")

    actual_names = {path.name for path in directory.iterdir() if path.is_file()}
    extras = sorted(actual_names - expected_names)
    missing = sorted(expected_names - actual_names)
    if extras or missing:
        fail(f"Signed artifact file set mismatch; missing={missing}, extra={extras}")


def verify_signed_manifest(args: argparse.Namespace) -> None:
    manifest = load_json(args.manifest)
    verify_manifest_header(manifest, "signed", args)
    if args.unsigned_manifest is not None:
        if not args.unsigned_manifest.is_file():
            fail("Unsigned manifest needed for signed-output binding is missing")
        if (
            manifest.get("unsignedManifestSha256")
            != sha256_file(args.unsigned_manifest)
        ):
            fail("Signed manifest is not bound to the downloaded unsigned manifest")
        unsigned = load_json(args.unsigned_manifest)
        if (
            manifest.get("unsignedWorkflowRunAttempt")
            != unsigned.get("workflowRunAttempt")
        ):
            fail("Signed manifest is not bound to the unsigned producer attempt")
        unsigned_files = {
            item["filename"]: item for item in manifest_files(unsigned)
        }
        for item in manifest_files(manifest):
            filename = item["filename"]
            if filename not in unsigned_files:
                fail(f"Signed manifest has no unsigned input for {filename}")
            verify_signed_package_payload(
                args.unsigned_manifest.parent / filename,
                args.signed_dir / filename,
            )
    fingerprint = normalized_fingerprint(args.certificate_sha256)
    if manifest.get("signingCertificateSha256") != fingerprint:
        fail("Signed manifest does not use the approved signing certificate")
    verify_signed_directory(
        args.signed_dir,
        manifest,
        allowed_supporting={
            args.manifest.name,
            "authenticode-report.json",
            "attestation-subjects.sha256",
        },
    )
    load_signature_report(
        args.signed_dir / manifest["authenticodeReport"]["filename"],
        args.signed_dir,
        fingerprint,
    )


def verify_artifact_metadata(args: argparse.Namespace) -> None:
    metadata = load_json(args.metadata)
    expected_digest = args.digest.lower()
    if SHA256.fullmatch(expected_digest):
        expected_digest = f"sha256:{expected_digest}"
    elif not re.fullmatch(r"sha256:[0-9a-f]{64}", expected_digest):
        fail("Artifact digest must be a SHA-256 value")
    checks = {
        "id": int(args.artifact_id),
        "name": args.name,
        "digest": expected_digest,
        "expired": False,
    }
    for key, expected in checks.items():
        actual = metadata.get(key)
        if key == "digest" and isinstance(actual, str):
            actual = actual.lower()
            if SHA256.fullmatch(actual):
                actual = f"sha256:{actual}"
        if actual != expected:
            fail(f"Artifact metadata {key} is '{actual}', expected '{expected}'")
    workflow_run = metadata.get("workflow_run") or {}
    if workflow_run.get("id") != int(args.run_id):
        fail("Artifact belongs to a different workflow run")
    if workflow_run.get("head_sha") != args.source_commit:
        fail("Artifact belongs to a different source commit")
    if workflow_run.get("head_branch") != args.source_ref.removeprefix(
        "refs/heads/"
    ):
        fail("Artifact belongs to a different source ref")
    expected_attempt_marker = f"-attempt-{args.run_attempt}"
    if expected_attempt_marker not in args.name:
        fail("Artifact name is not bound to the requested workflow attempt")


def configured_release_branches(policy: dict[str, Any], default_branch: str) -> list[str]:
    servicing = policy.get("servicingBranches")
    if not isinstance(servicing, list):
        fail("Release policy servicingBranches must be an array")

    branches = [default_branch]
    for branch in servicing:
        if (
            not isinstance(branch, str)
            or not re.fullmatch(r"release/[1-9][0-9]*\.x", branch)
        ):
            fail(f"Release policy contains an invalid servicing branch: {branch!r}")
        if branch in branches:
            fail(f"Release policy repeats release branch '{branch}'")
        branches.append(branch)
    return branches


def verify_source(args: argparse.Namespace) -> None:
    repository = load_json(args.repository_json)
    branch = load_json(args.branch_json)
    policy = load_json(args.policy)
    default_branch = repository.get("default_branch")
    if not default_branch:
        fail("Repository API response has no default_branch")
    if policy.get("defaultBranch") != default_branch:
        fail("Release policy default branch does not match the repository default")
    release_branches = configured_release_branches(policy, default_branch)
    if not args.source_ref.startswith("refs/heads/"):
        fail(f"Release ref '{args.source_ref}' is not a branch ref")
    source_branch = args.source_ref.removeprefix("refs/heads/")
    if source_branch not in release_branches:
        fail(
            f"Release ref '{args.source_ref}' is neither the default branch nor a "
            "policy-approved servicing branch"
        )
    if (branch.get("commit") or {}).get("sha") != args.source_commit:
        fail("Release SHA is not the current release-branch head")
    if args.require_protected == "true" and args.ref_protected != "true":
        fail("Release source ref is not reported as protected")


def verify_required_checks(args: argparse.Namespace) -> None:
    policy = load_json(args.policy)
    required = policy.get("requiredStatusChecks")
    if (
        not isinstance(required, list)
        or not required
        or any(not isinstance(item, str) or not item for item in required)
    ):
        fail("Release policy requiredStatusChecks must contain check names")

    payload = load_json(args.check_runs_json)
    pages = payload if isinstance(payload, list) else [payload]
    check_runs: list[dict[str, Any]] = []
    for page in pages:
        if not isinstance(page, dict) or not isinstance(page.get("check_runs"), list):
            fail("GitHub check-runs response is malformed")
        check_runs.extend(page["check_runs"])

    failures: list[str] = []
    for name in required:
        candidates = [
            item
            for item in check_runs
            if item.get("name") == name
            and item.get("head_sha") == args.source_commit
            and ((item.get("app") or {}).get("slug") == "github-actions")
            and isinstance(item.get("id"), int)
        ]
        if not candidates:
            failures.append(f"{name}: no GitHub Actions check run for the release SHA")
            continue
        latest = max(candidates, key=lambda item: item["id"])
        if latest.get("status") != "completed" or latest.get("conclusion") != "success":
            failures.append(
                f"{name}: latest run is status={latest.get('status')!r}, "
                f"conclusion={latest.get('conclusion')!r}"
            )
    if failures:
        fail(
            "Required checks have not succeeded for the exact release SHA:\n  "
            + "\n  ".join(failures)
        )


def verify_installed_workload(args: argparse.Namespace) -> None:
    reviewed = workload_contract(args.baselines)
    actual = {
        "id": args.workload_id,
        "version": args.workload_version,
        "packageSha256": args.package_sha256,
        "signerFingerprint": args.signer_fingerprint,
    }
    for key, value in reviewed.items():
        if not isinstance(value, str) or not value:
            fail(
                f"eng/baselines.json has no reviewed workload manifest {key}"
            )
        if actual[key] != value:
            fail(f"Requested workload manifest {key} is not the reviewed value")
    if not SHA256.fullmatch(args.package_sha256):
        fail("Workload manifest package SHA-256 is invalid")

    package_id = args.workload_id.lower()
    marker = ".manifest-"
    if marker not in package_id:
        fail("Reviewed workload package ID has no manifest band suffix")
    manifest_id, band = package_id.split(marker, 1)

    dotnet_root = args.dotnet_root
    if dotnet_root is None:
        configured = os.environ.get("DOTNET_ROOT")
        if configured:
            dotnet_root = Path(configured)
        else:
            dotnet_path = shutil.which(args.dotnet)
            if dotnet_path is None:
                fail(f"Could not locate dotnet command '{args.dotnet}'")
            dotnet_root = Path(dotnet_path).resolve().parent

    lower_version = args.workload_version.lower()
    package_url = (
        f"{args.feed_base.rstrip('/')}/{package_id}/{lower_version}/"
        f"{package_id}.{lower_version}.nupkg"
    )
    with tempfile.TemporaryDirectory(prefix="maui-tizen-workload-") as temporary:
        package_path = Path(temporary) / f"{package_id}.{lower_version}.nupkg"
        try:
            with urllib.request.urlopen(package_url) as response:
                package_path.write_bytes(response.read())
        except (OSError, urllib.error.HTTPError) as exc:
            fail(f"Could not download the reviewed workload manifest: {exc}")
        if sha256_file(package_path) != args.package_sha256:
            fail("Reviewed workload manifest package SHA-256 does not match")
        try:
            with zipfile.ZipFile(package_path) as archive:
                manifest_entries = [
                    name
                    for name in archive.namelist()
                    if name.replace("\\", "/") == "data/WorkloadManifest.json"
                ]
                if manifest_entries != ["data/WorkloadManifest.json"]:
                    fail(
                        "Reviewed workload package must contain exactly one "
                        "data/WorkloadManifest.json"
                    )
                expected_manifest = archive.read(manifest_entries[0])
        except (zipfile.BadZipFile, KeyError) as exc:
            fail(f"Could not inspect reviewed workload package: {exc}")

        result = subprocess.run(
            [
                args.dotnet,
                "nuget",
                "verify",
                "--all",
                "--certificate-fingerprint",
                args.signer_fingerprint,
                str(package_path),
            ],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode != 0:
            fail(
                "Reviewed workload manifest signature verification failed:\n"
                f"{result.stdout}\n{result.stderr}"
            )

    manifest_root = dotnet_root / "sdk-manifests"
    feature_line = ".".join(band.split(".")[:2]) + "."
    candidates = sorted(
        {
            *manifest_root.glob(f"{feature_line}*/{manifest_id}/WorkloadManifest.json"),
            *manifest_root.glob(
                f"{feature_line}*/{manifest_id}/*/WorkloadManifest.json"
            ),
        }
    )
    if not candidates:
        fail(
            "The installed Samsung workload manifest was not found in the "
            f"{feature_line}* SDK feature line"
        )
    mismatched = [
        path
        for path in candidates
        if not path.is_file() or path.read_bytes() != expected_manifest
    ]
    if mismatched:
        fail(
            "An installed Samsung workload manifest differs from the reviewed "
            "package: " + ", ".join(str(path) for path in mismatched)
        )


def verify_workload_contract(args: argparse.Namespace) -> None:
    reviewed = workload_contract(args.baselines)
    actual = {
        "id": args.workload_id,
        "version": args.workload_version,
        "packageSha256": args.package_sha256,
        "signerFingerprint": args.signer_fingerprint,
    }
    for key, value in reviewed.items():
        if not isinstance(value, str) or not value:
            fail(
                f"eng/baselines.json has no reviewed workload manifest {key}"
            )
        if actual[key] != value:
            fail(f"Requested workload manifest {key} is not the reviewed value")
    if not SHA256.fullmatch(args.package_sha256):
        fail("Workload manifest package SHA-256 is invalid")


def run_gh_json(gh: str, endpoint: str) -> Any:
    result = subprocess.run(
        [gh, "api", endpoint],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        fail(f"GitHub API request failed for {endpoint}: {result.stderr.strip()}")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        fail(f"GitHub API returned invalid JSON for {endpoint}: {exc}")


def github_ref_matches(pattern: str, ref: str, default_branch: str) -> bool:
    if pattern == "~ALL":
        return True
    if pattern == "~DEFAULT_BRANCH":
        return ref == f"refs/heads/{default_branch}"
    if pattern == default_branch:
        return ref == f"refs/heads/{default_branch}"

    expression = ["^"]
    index = 0
    while index < len(pattern):
        character = pattern[index]
        if character == "*":
            if index + 1 < len(pattern) and pattern[index + 1] == "*":
                index += 1
                if index + 1 < len(pattern) and pattern[index + 1] == "/":
                    index += 1
                    expression.append("(?:.*/)?")
                else:
                    expression.append(".*")
            else:
                expression.append("[^/]*")
        elif character == "?":
            expression.append("[^/]")
        else:
            expression.append(re.escape(character))
        index += 1
    expression.append("$")
    return re.fullmatch("".join(expression), ref) is not None


def verify_protections(args: argparse.Namespace) -> None:
    policy = load_json(args.policy)
    if policy.get("defaultBranch") != args.default_branch:
        fail("Release policy default branch does not match the repository default")
    release_branches = configured_release_branches(policy, args.default_branch)
    source_branch = args.source_branch or args.default_branch
    if source_branch not in release_branches:
        fail("Protection audit source branch is not approved by release policy")
    environments = policy.get("protectedEnvironments")
    if not isinstance(environments, list) or not environments:
        fail("Release policy declares no protected environments")
    missing: list[str] = []
    for environment in environments:
        encoded = urllib.parse.quote(str(environment), safe="")
        data = run_gh_json(
            args.gh, f"repos/{args.repository}/environments/{encoded}"
        )
        reviewer_rules = [
            rule
            for rule in data.get("protection_rules", [])
            if rule.get("type") == "required_reviewers"
            and len(rule.get("reviewers") or []) > 0
        ]
        branch_policy = data.get("deployment_branch_policy") or {}
        if not reviewer_rules:
            missing.append(f"{environment}: no required reviewers")
        if branch_policy.get("protected_branches") is not True:
            missing.append(
                f"{environment}: deployments are not restricted to protected branches"
            )

    rulesets = run_gh_json(
        args.gh, f"repos/{args.repository}/rulesets?targets=branch&per_page=100"
    )
    safe_rules: list[dict[str, Any]] = []
    for summary in rulesets:
        if summary.get("enforcement") != "active" or summary.get("target") != "branch":
            continue
        detail = run_gh_json(
            args.gh, f"repos/{args.repository}/rulesets/{summary.get('id')}"
        )
        ref_names = (detail.get("conditions") or {}).get("ref_name") or {}
        includes = ref_names.get("include") or []
        excludes = ref_names.get("exclude") or []
        ref = f"refs/heads/{source_branch}"
        included = any(
            github_ref_matches(pattern, ref, args.default_branch)
            for pattern in includes
        )
        excluded = any(
            github_ref_matches(pattern, ref, args.default_branch)
            for pattern in excludes
        )
        bypass_actors = detail.get("bypass_actors")
        if (
            included
            and not excluded
            and isinstance(bypass_actors, list)
            and len(bypass_actors) == 0
        ):
            safe_rules.extend(detail.get("rules") or [])
    if not safe_rules:
        missing.append(
            f"{source_branch}: no active covering ruleset is free of bypass actors"
        )
    rule_types = {rule.get("type") for rule in safe_rules}
    pull_rules = [
        rule.get("parameters") or {}
        for rule in safe_rules
        if rule.get("type") == "pull_request"
    ]
    if max(
        (
            int(rule.get("required_approving_review_count", 0))
            for rule in pull_rules
        ),
        default=0,
    ) < 1:
        missing.append(f"{source_branch}: no approving review is required")
    if not any(
        rule.get("require_code_owner_review") is True for rule in pull_rules
    ):
        missing.append(f"{source_branch}: CODEOWNERS review is not required")
    if not any(
        rule.get("require_last_push_approval") is True for rule in pull_rules
    ):
        missing.append(f"{source_branch}: last-push approval is not required")
    for rule_type in ("deletion", "non_fast_forward", "required_linear_history"):
        if rule_type not in rule_types:
            missing.append(f"{source_branch}: missing {rule_type} rule")
    status_rules = [
        rule.get("parameters") or {}
        for rule in safe_rules
        if rule.get("type") == "required_status_checks"
    ]
    required_contexts = {
        item.get("context")
        for rule in status_rules
        for item in rule.get("required_status_checks", [])
        if item.get("context")
    }
    policy_contexts = set(policy.get("requiredStatusChecks") or [])
    if not policy_contexts or not policy_contexts.issubset(required_contexts):
        missing.append(
            f"{source_branch}: required status checks do not match release policy"
        )
    if not any(
        rule.get("strict_required_status_checks_policy") is True
        for rule in status_rules
    ):
        missing.append(
            f"{source_branch}: status checks are not required on the latest head"
        )

    runner_policy = policy.get("runnerGroup") or {}
    runner_group_name = runner_policy.get("name")
    repository = run_gh_json(args.gh, f"repos/{args.repository}")
    owner = repository.get("owner") or {}
    if owner.get("type") != "Organization":
        missing.append("release runners must be owned by a GitHub organization")
    elif not isinstance(runner_group_name, str) or not runner_group_name:
        missing.append("release policy declares no runner group")
    else:
        organization = owner.get("login")
        visible_repository = urllib.parse.quote(
            args.repository.split("/", 1)[-1], safe=""
        )
        groups = run_gh_json(
            args.gh,
            f"orgs/{organization}/actions/runner-groups"
            f"?visible_to_repository={visible_repository}&per_page=100",
        )
        matches = [
            group
            for group in groups.get("runner_groups", [])
            if group.get("name") == runner_group_name
        ]
        if len(matches) != 1:
            missing.append(
                f"runner group '{runner_group_name}' is not uniquely visible to this repository"
            )
        else:
            group_id = matches[0].get("id")
            group = run_gh_json(
                args.gh,
                f"orgs/{organization}/actions/runner-groups/{group_id}",
            )
            expected_workflows = {
                f"{args.repository}/.github/workflows/"
                f"tizen-device-validation.yml@refs/heads/{branch}"
                for branch in release_branches
            }
            selected_workflows = group.get("selected_workflows")
            if group.get("inherited") is not False:
                missing.append(
                    "release runner group must be owned by the repository organization, not inherited"
                )
            if group.get("visibility") != "selected":
                missing.append("release runner group is not repository-selected")
            if group.get("allows_public_repositories") is not True:
                missing.append("release runner group does not explicitly allow this public repository")
            if group.get("restricted_to_workflows") is not True:
                missing.append("release runner group is not restricted to selected workflows")
            if (
                not isinstance(selected_workflows, list)
                or set(selected_workflows) != expected_workflows
            ):
                missing.append(
                    "release runner group is not restricted exclusively to the reviewed device workflow"
                )
            repositories = run_gh_json(
                args.gh,
                f"orgs/{organization}/actions/runner-groups/{group_id}/repositories"
                "?per_page=100",
            )
            selected_repositories = {
                item.get("full_name")
                for item in repositories.get("repositories", [])
                if item.get("full_name")
            }
            if selected_repositories != {args.repository}:
                missing.append(
                    "release runner group repository access is not exclusive to this repository"
                )
            if runner_policy.get("requireJit") is not True:
                missing.append("release policy does not require one-job JIT runners")
            runners = run_gh_json(
                args.gh,
                f"orgs/{organization}/actions/runner-groups/{group_id}/runners"
                "?per_page=100",
            )
            if int(runners.get("total_count", -1)) != 0:
                missing.append(
                    "release runner group has persistent registered runners; JIT requires zero idle runners"
                )
    if missing:
        fail("Release protections are incomplete:\n  " + "\n  ".join(missing))


def policy_value(value: Any, label: str, errors: list[str]) -> str:
    if not isinstance(value, str) or not value.strip() or PLACEHOLDER.search(value):
        errors.append(f"{label} is unset or placeholder")
        return ""
    return value.strip()


def api_directory_differences(
    baseline_dir: Path, current_dir: Path
) -> list[str]:
    baseline_files = sorted(
        path
        for path in baseline_dir.glob("*.json")
        if path.name != "manifest.json"
    )
    current_files = {
        path.name: path
        for path in current_dir.glob("*.json")
        if path.name != "manifest.json"
    }
    if not baseline_files:
        fail(f"API compatibility baseline directory is empty: {baseline_dir}")
    errors: list[str] = []
    for baseline_path in baseline_files:
        current_path = current_files.get(baseline_path.name)
        if current_path is None:
            errors.append(f"missing current API dump {baseline_path.name}")
            continue
        baseline = load_json(baseline_path)
        current = load_json(current_path)
        current_types = {
            (
                item.get("namespace"),
                item.get("name"),
                item.get("arity"),
            ): item
            for item in current.get("types", [])
        }
        for old_type in baseline.get("types", []):
            key = (
                old_type.get("namespace"),
                old_type.get("name"),
                old_type.get("arity"),
            )
            new_type = current_types.get(key)
            if new_type is None:
                errors.append(f"{baseline_path.name}: removed public type {key}")
                continue
            for property_name in (
                "kind",
                "accessibility",
                "isStatic",
                "isAbstract",
                "baseType",
                "delegateSignature",
                "delegateParameters",
                "underlyingType",
                "genericConstraints",
            ):
                if property_name == "genericConstraints" and property_name not in old_type:
                    continue
                if old_type.get(property_name) != new_type.get(property_name):
                    errors.append(
                        f"{baseline_path.name}: changed {property_name} for "
                        f"{key[0]}.{key[1]}"
                    )
            if old_type.get("isSealed") is False and new_type.get("isSealed") is True:
                errors.append(
                    f"{baseline_path.name}: sealed public type {key[0]}.{key[1]}"
                )
            old_interfaces = set(old_type.get("interfaces") or [])
            new_interfaces = set(new_type.get("interfaces") or [])
            if old_type.get("kind") == "interface" and old_interfaces != new_interfaces:
                errors.append(
                    f"{baseline_path.name}: changed base interfaces for "
                    f"{key[0]}.{key[1]}"
                )
            elif not old_interfaces.issubset(new_interfaces):
                errors.append(
                    f"{baseline_path.name}: removed interface(s) from "
                    f"{key[0]}.{key[1]}: {sorted(old_interfaces - new_interfaces)}"
                )
            current_members = {
                (member.get("kind"), member.get("signature")): member
                for member in new_type.get("members", [])
            }
            baseline_member_keys = {
                (member.get("kind"), member.get("signature"))
                for member in old_type.get("members", [])
            }
            for member in old_type.get("members", []):
                member_key = (member.get("kind"), member.get("signature"))
                current_member = current_members.get(member_key)
                if current_member is None:
                    errors.append(
                        f"{baseline_path.name}: removed public member "
                        f"{key[0]}.{key[1]} {member_key}"
                    )
                    continue
                for property_name in (
                    "accessibility",
                    "isStatic",
                    "isAbstract",
                    "isVirtual",
                    "isFinal",
                    "isExtensionMethod",
                    "isLiteral",
                    "isInitOnly",
                    "constantValue",
                    "genericConstraints",
                    "parameters",
                    "getterAccessibility",
                    "setterAccessibility",
                ):
                    if (
                        property_name
                        in {
                            "genericConstraints",
                            "isFinal",
                            "isExtensionMethod",
                            "isLiteral",
                            "isInitOnly",
                            "constantValue",
                            "parameters",
                            "getterAccessibility",
                            "setterAccessibility",
                        }
                        and property_name not in member
                    ):
                        continue
                    if member.get(property_name) != current_member.get(property_name):
                        errors.append(
                            f"{baseline_path.name}: changed {property_name} for "
                            f"{key[0]}.{key[1]} {member_key}"
                        )
            if old_type.get("kind") == "interface" or old_type.get("isAbstract") is True:
                for member_key, current_member in current_members.items():
                    if (
                        member_key not in baseline_member_keys
                        and current_member.get("isAbstract") is True
                    ):
                        errors.append(
                            f"{baseline_path.name}: added abstract requirement "
                            f"{key[0]}.{key[1]} {member_key}"
                        )
    return errors


def version_major(version: str, label: str) -> int:
    match = SEMVER.fullmatch(version)
    if match is None:
        fail(f"{label} '{version}' is not a supported release version")
    return int(match.group("major"))


def enforce_api_differences(
    differences: list[str],
    release_version: str | None = None,
    baseline_version: str | None = None,
    approvals: Any = None,
) -> None:
    approval_entries = approvals if approvals is not None else []
    if not isinstance(approval_entries, list):
        fail("API approvedBreakingChanges must be an array")

    approved: dict[str, dict[str, Any]] = {}
    for entry in approval_entries:
        if not isinstance(entry, dict):
            fail("Each approved API break must be an object")
        difference = entry.get("difference")
        evidence = entry.get("deprecationEvidence")
        approved_baseline = entry.get("baselineVersion")
        approved_major = entry.get("releaseMajor")
        if not isinstance(difference, str) or not difference:
            fail("Each approved API break must name one exact difference")
        if difference in approved:
            fail(f"Approved API break is duplicated: {difference}")
        if (
            not isinstance(evidence, str)
            or not re.fullmatch(
                r"https://github\.com/[^/]+/[^/]+/issues/[1-9][0-9]*",
                evidence,
            )
        ):
            fail(
                f"Approved API break has no concrete GitHub deprecation issue: {difference}"
            )
        if not isinstance(approved_baseline, str) or SEMVER.fullmatch(
            approved_baseline
        ) is None:
            fail(f"Approved API break has no valid baselineVersion: {difference}")
        if not isinstance(approved_major, int) or approved_major < 1:
            fail(f"Approved API break has no valid releaseMajor: {difference}")
        approved[difference] = entry

    if not differences:
        if approved:
            fail(
                "API break approvals are stale; no matching differences were detected:\n  "
                + "\n  ".join(sorted(approved))
            )
        return

    if release_version is None or baseline_version is None:
        fail("API compatibility check failed:\n  " + "\n  ".join(differences))
    release_major = version_major(release_version, "Release version")
    baseline_major = version_major(baseline_version, "API baseline version")
    if release_major <= baseline_major:
        fail(
            "API compatibility check failed outside a newer major release:\n  "
            + "\n  ".join(differences)
        )
    for difference, entry in approved.items():
        if entry["baselineVersion"] != baseline_version:
            fail(
                f"Approved API break baselineVersion does not match "
                f"'{baseline_version}': {difference}"
            )
        if entry["releaseMajor"] != release_major:
            fail(
                f"Approved API break releaseMajor does not match "
                f"'{release_major}': {difference}"
            )

    detected = set(differences)
    approved_set = set(approved)
    missing = sorted(detected - approved_set)
    stale = sorted(approved_set - detected)
    if missing or stale:
        details: list[str] = []
        if missing:
            details.append("unapproved differences:\n    " + "\n    ".join(missing))
        if stale:
            details.append("stale approvals:\n    " + "\n    ".join(stale))
        fail("Major-release API break approval mismatch:\n  " + "\n  ".join(details))


def compare_api_directories(
    baseline_dir: Path,
    current_dir: Path,
    release_version: str | None = None,
    baseline_version: str | None = None,
    approvals: Any = None,
) -> None:
    enforce_api_differences(
        api_directory_differences(baseline_dir, current_dir),
        release_version,
        baseline_version,
        approvals,
    )


def compare_api_command(args: argparse.Namespace) -> None:
    approvals = None
    if args.approved_breaks_json is not None:
        approval_data = load_json(args.approved_breaks_json)
        approvals = approval_data.get("approvedBreakingChanges")
    compare_api_directories(
        args.baseline_dir,
        args.current_dir,
        args.release_version,
        args.baseline_version,
        approvals,
    )


def verify_api_baseline_manifest(
    baseline_dir: Path, baseline_version: str
) -> dict[str, Any]:
    manifest_path = baseline_dir / "manifest.json"
    manifest = load_json(manifest_path)
    if manifest.get("baselineKind") != "standalone-release":
        fail(
            "API baseline is not a standalone-release baseline generated from "
            "Maui.Tizen package identities"
        )
    if not COMMIT_SHA.fullmatch(str(manifest.get("sourceCommit") or "")):
        fail("Standalone release API baseline has no source commit")
    if not SHA256.fullmatch(str(manifest.get("sourceManifestSha256") or "")):
        fail("Standalone release API baseline has no reviewed manifest hash")
    expected_tfm = (load_json(DEFAULT_BASELINES).get("target") or {}).get(
        "targetFramework"
    )
    if manifest.get("targetFramework") != expected_tfm:
        fail(
            f"Standalone release API baseline target framework is "
            f"'{manifest.get('targetFramework')}', expected '{expected_tfm}'"
        )
    if manifest.get("packageVersion") != baseline_version:
        fail(
            f"API baseline manifest version '{manifest.get('packageVersion')}' "
            f"does not match release policy '{baseline_version}'"
        )
    if manifest.get("dumpSchemaVersion") != 2:
        fail("API baseline manifest does not require dump schema version 2")
    packages = manifest.get("packages")
    if not isinstance(packages, list) or not packages:
        fail("API baseline manifest contains no package outputs")
    expected_files: set[str] = set()
    for package in packages:
        assembly = package.get("assembly")
        output_sha256 = package.get("outputSha256")
        if (
            not isinstance(assembly, str)
            or not isinstance(output_sha256, str)
            or not SHA256.fullmatch(output_sha256)
        ):
            fail("API baseline manifest package entry is incomplete")
        filename = Path(assembly).stem + ".json"
        if filename in expected_files:
            fail(f"API baseline manifest repeats output {filename}")
        expected_files.add(filename)
        output = baseline_dir / filename
        if not output.is_file() or sha256_file(output) != output_sha256:
            fail(f"API baseline output hash mismatch for {filename}")
        if load_json(output).get("schemaVersion") != 2:
            fail(f"API baseline output uses an unsupported schema: {filename}")
    actual_files = {
        path.name
        for path in baseline_dir.glob("*.json")
        if path.name != "manifest.json"
    }
    if actual_files != expected_files:
        fail(
            "API baseline manifest file set mismatch; "
            f"expected={sorted(expected_files)}, actual={sorted(actual_files)}"
        )
    msbuild_files = manifest.get("msbuildFiles")
    if not isinstance(msbuild_files, list):
        fail("API baseline manifest does not declare MSBuild public API files")
    for entry in msbuild_files:
        baseline_file = entry.get("baselineFile")
        digest = entry.get("sha256")
        if not isinstance(baseline_file, str) or not SHA256.fullmatch(str(digest)):
            fail("MSBuild API baseline entry is incomplete")
        path = (baseline_dir / baseline_file).resolve()
        if baseline_dir.resolve() not in path.parents:
            fail("MSBuild API baseline path escapes the baseline directory")
        if not path.is_file() or sha256_file(path) != digest:
            fail(f"MSBuild API baseline hash mismatch for {baseline_file}")
    return manifest


def msbuild_contract_differences(
    packages_dir: Path,
    baseline_dir: Path,
    baseline_version: str,
) -> list[str]:
    manifest = verify_api_baseline_manifest(baseline_dir, baseline_version)
    expected = {
        (entry.get("packageId"), entry.get("packagePath")): entry
        for entry in manifest["msbuildFiles"]
    }
    if len(expected) != len(manifest["msbuildFiles"]):
        fail("MSBuild API baseline contains duplicate package/path entries")

    actual: dict[tuple[str, str], bytes] = {}
    for package_path in sorted(packages_dir.glob("*.nupkg")):
        info = read_package(package_path)
        with zipfile.ZipFile(package_path) as archive:
            for name in archive.namelist():
                normalized = name.replace("\\", "/")
                parts = normalized.split("/")
                if (
                    len(parts) >= 2
                    and parts[0].lower()
                    in {"build", "buildmultitargeting", "buildtransitive"}
                    and normalized.lower().endswith((".props", ".targets"))
                ):
                    actual[(info["id"], normalized)] = archive.read(name)

    differences: list[str] = []
    if set(actual) != set(expected):
        differences.append(
            "MSBuild public API file set mismatch; "
            f"expected={sorted(expected)}, actual={sorted(actual)}"
        )
    for key, current_bytes in actual.items():
        if key not in expected:
            continue
        baseline_file = (baseline_dir / expected[key]["baselineFile"]).resolve()
        if current_bytes != baseline_file.read_bytes():
            differences.append(
                f"MSBuild public API changed for {key[0]}:{key[1]}"
            )
    return differences


def verify_msbuild_contracts(
    packages_dir: Path,
    baseline_dir: Path,
    baseline_version: str,
) -> None:
    enforce_api_differences(
        msbuild_contract_differences(
            packages_dir, baseline_dir, baseline_version
        )
    )


def run_api_compatibility(
    packages_dir: Path,
    baseline_dir: Path,
    baseline_version: str,
    dotnet: str,
    release_version: str,
    approvals: Any,
) -> None:
    verify_api_baseline_manifest(baseline_dir, baseline_version)
    with tempfile.TemporaryDirectory(prefix="maui-tizen-api-") as temporary:
        temporary_path = Path(temporary)
        assemblies: list[Path] = []
        for package in sorted(packages_dir.glob("*.nupkg")):
            with zipfile.ZipFile(package) as archive:
                package_root = (temporary_path / package.stem).resolve()
                for name in read_package(package)["binaries"]:
                    target = (package_root / name).resolve()
                    if package_root not in target.parents:
                        fail(f"Package API path escapes extraction root: {name}")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(archive.read(name))
                    assemblies.append(target)
        if not assemblies:
            fail("No managed assemblies were found for API compatibility validation")
        output = temporary_path / "current"
        command = [
            dotnet,
            "run",
            "--project",
            str(ROOT / "eng/tools/ApiDump/ApiDump.csproj"),
            "-c",
            "Release",
            "--",
            *[str(path) for path in assemblies],
            "--out",
            str(output),
        ]
        result = subprocess.run(
            command, cwd=ROOT, text=True, capture_output=True, check=False
        )
        if result.returncode != 0:
            fail(f"ApiDump failed:\n{result.stdout}\n{result.stderr}")
        differences = api_directory_differences(baseline_dir, output)
    differences.extend(
        msbuild_contract_differences(
            packages_dir, baseline_dir, baseline_version
        )
    )
    enforce_api_differences(
        differences,
        release_version,
        baseline_version,
        approvals,
    )


def evaluate_shipping_projects(dotnet: str) -> dict[str, dict[str, str]]:
    projects: dict[str, dict[str, str]] = {}
    for project in sorted((ROOT / "src").rglob("*.csproj")):
        result = subprocess.run(
            [
                dotnet,
                "msbuild",
                str(project),
                "-getProperty:PackageId",
                "-getProperty:Company",
                "-getProperty:PackageIcon",
                "-getProperty:IsPackable",
                "-v:q",
            ],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode != 0:
            fail(f"Could not evaluate package metadata for {project}: {result.stderr}")
        try:
            properties = json.loads(result.stdout).get("Properties") or {}
        except json.JSONDecodeError as exc:
            fail(f"MSBuild returned invalid property JSON for {project}: {exc}")
        package_id = properties.get("PackageId")
        if package_id:
            if package_id in projects:
                fail(f"Multiple projects evaluate to PackageId '{package_id}'")
            projects[package_id] = {
                "path": str(project.relative_to(ROOT)),
                "company": properties.get("Company", ""),
                "icon": properties.get("PackageIcon", ""),
                "isPackable": properties.get("IsPackable", ""),
            }
    return projects


def verify_policies(args: argparse.Namespace) -> None:
    validate_version(args.version, args.baselines)
    policy = load_json(args.policy)
    manifest = load_json(args.manifest)
    verify_manifest_header(manifest, "unsigned", args)
    ids = expected_ids(args.expected_package_ids_file, args.contracts_dir)
    verify_unsigned_directory(
        args.packages_dir,
        manifest,
        ids,
        allowed_supporting={args.manifest.name},
    )

    errors: list[str] = []
    if policy.get("schemaVersion") != 1:
        errors.append("release policy schemaVersion must be 1")
    if policy.get("status") != "ready":
        errors.append("release policy status is not 'ready'")
    if policy.get("publishingEnabled") is not True:
        errors.append("release policy publishingEnabled is not true")

    api = policy.get("apiCompatibility") or {}
    baseline_version = policy_value(
        api.get("baselineVersion"), "API baseline version", errors
    )
    baseline_directory = policy_value(
        api.get("baselineDirectory"), "API baseline directory", errors
    )
    if baseline_version == args.version:
        errors.append("API compatibility baseline cannot be the release being built")

    metadata_policy = policy.get("packageMetadata") or {}
    authors = policy_value(
        metadata_policy.get("authors"), "package metadata authors", errors
    )
    company = policy_value(
        metadata_policy.get("company"), "package metadata company", errors
    )
    icon = policy_value(
        metadata_policy.get("icon"), "package metadata icon", errors
    )

    signing = policy.get("signing") or {}
    certificate = normalized_fingerprint(
        policy_value(
            signing.get("certificateSha256"),
            "signing certificate SHA-256",
            errors,
        )
    )
    if certificate and not SHA256.fullmatch(certificate):
        errors.append("signing certificate SHA-256 is not 64 hexadecimal characters")
    if signing.get("requireAuthenticode") is not True:
        errors.append("Authenticode policy is not mandatory")

    trusted = policy.get("trustedPublishing") or {}
    if trusted.get("enabled") is not True:
        errors.append("NuGet trusted publishing is not enabled")
    if trusted.get("repository") != args.repository:
        errors.append("trusted publishing repository does not match this repository")
    if trusted.get("workflow") != ".github/workflows/release.yml":
        errors.append("trusted publishing workflow is not release.yml")
    if trusted.get("environment") != "nuget-publish":
        errors.append("trusted publishing environment is not nuget-publish")

    policy_workload = policy.get("workloadManifest") or {}
    baseline_workload = workload_contract(args.baselines)
    for key in ("id", "version", "packageSha256", "signerFingerprint"):
        expected = policy_value(
            policy_workload.get(key), f"workload manifest {key}", errors
        )
        if expected and expected != baseline_workload.get(key):
            errors.append(
                f"release policy workload {key} does not match eng/baselines.json"
            )
        if expected and expected != (manifest.get("workloadManifest") or {}).get(key):
            errors.append(f"unsigned release manifest workload {key} is not reviewed")

    support_path = args.support_matrix
    try:
        support_text = support_path.read_text(encoding="utf-8")
        if PLACEHOLDER.search(support_text) or "Status: TEMPLATE" in support_text:
            errors.append("support matrix still contains template/placeholder values")
    except OSError as exc:
        errors.append(f"support matrix could not be read: {exc}")

    required_tags = {"maui", "tizen", "dotnet"}
    project_metadata = (
        evaluate_shipping_projects(args.dotnet) if company and icon else {}
    )
    for package_entry in manifest["packages"]:
        package_file = next(
            item
            for item in package_entry["files"]
            if item.get("kind") == "package"
        )
        info = read_package(args.packages_dir / package_file["filename"])
        package_label = info["id"]
        checks = {
            "authors": (info["authors"], authors),
            "icon": (info["icon"], icon),
            "project URL": (
                info["projectUrl"],
                f"https://github.com/{args.repository}",
            ),
            "repository URL": (
                info["repositoryUrl"],
                f"https://github.com/{args.repository}",
            ),
            "repository type": (info["repositoryType"].lower(), "git"),
            "repository commit": (info["repositoryCommit"], args.source_commit),
            "license": (info["license"], "MIT"),
            "license type": (info["licenseType"].lower(), "expression"),
            "readme": (info["readme"], "README.md"),
        }
        for label, (actual, expected) in checks.items():
            if expected and actual != expected:
                errors.append(
                    f"{package_label} {label} is '{actual}', expected '{expected}'"
                )
        if not info["description"] or PLACEHOLDER.search(info["description"]):
            errors.append(f"{package_label} description is missing/placeholder")
        for metadata_file in (info["readme"], info["icon"]):
            if metadata_file and metadata_file not in info["entries"]:
                errors.append(
                    f"{package_label} metadata file is missing from package: "
                    f"{metadata_file}"
                )
        tags = {
            tag.lower()
            for tag in re.split(r"[\s;,]+", info["tags"])
            if tag.strip()
        }
        if not required_tags.issubset(tags):
            errors.append(
                f"{package_label} tags are missing "
                f"{sorted(required_tags - tags)}"
            )
        if company and icon:
            project = project_metadata.get(package_label)
            if project is None:
                errors.append(
                    f"{package_label} has no uniquely evaluated shipping project"
                )
            else:
                if project["company"] != company:
                    errors.append(
                        f"{package_label} Company is '{project['company']}', "
                        f"expected '{company}'"
                    )
                if project["icon"] != icon:
                    errors.append(
                        f"{package_label} PackageIcon is '{project['icon']}', "
                        f"expected '{icon}'"
                    )
                if project["isPackable"].lower() != "true":
                    errors.append(
                        f"{package_label} project is not explicitly packable"
                    )

    if errors:
        fail("Release policy checks failed:\n  " + "\n  ".join(errors))

    baseline_path = (ROOT / baseline_directory).resolve()
    if ROOT not in baseline_path.parents:
        fail("API baseline directory must stay inside the repository")
    run_api_compatibility(
        args.packages_dir,
        baseline_path,
        baseline_version,
        args.dotnet,
        args.version,
        api.get("approvedBreakingChanges"),
    )


def verify_attestation_report(args: argparse.Namespace) -> None:
    report = load_json(args.report)
    if not isinstance(report, list) or not report:
        fail("Attestation verification report is empty")
    expected_run = (
        f"https://github.com/{args.repository}/actions/runs/"
        f"{args.run_id}/attempts/{args.run_attempt}"
    )
    expected_signer = (
        f"https://github.com/{args.repository}/.github/workflows/"
        f"release.yml@{args.source_ref}"
    )
    expected_repo = f"https://github.com/{args.repository}"
    digest = args.subject_digest.lower().removeprefix("sha256:")
    if not SHA256.fullmatch(digest):
        fail("Expected attestation subject digest is invalid")

    reasons: list[str] = []
    for entry in report:
        result = entry.get("verificationResult") or {}
        certificate = ((result.get("signature") or {}).get("certificate") or {})
        extensions = certificate.get("extensions") or {}
        statement = result.get("statement") or {}
        subjects = statement.get("subject") or []
        subject_matches = any(
            subject.get("name") == args.subject_name
            and (subject.get("digest") or {}).get("sha256") == digest
            for subject in subjects
        )
        checks = {
            "signer workflow": (
                extensions.get("buildSignerURI"),
                expected_signer,
            ),
            "signer digest": (
                extensions.get("buildSignerDigest"),
                args.source_commit,
            ),
            "source repository": (
                extensions.get("sourceRepositoryURI"),
                expected_repo,
            ),
            "source digest": (
                extensions.get("sourceRepositoryDigest"),
                args.source_commit,
            ),
            "source ref": (
                extensions.get("sourceRepositoryRef"),
                args.source_ref,
            ),
            "run attempt": (
                extensions.get("runInvocationURI"),
                expected_run,
            ),
        }
        mismatches = [
            f"{label}='{actual}'"
            for label, (actual, expected) in checks.items()
            if actual != expected
        ]
        if statement.get("predicateType") != "https://slsa.dev/provenance/v1":
            mismatches.append("predicate type mismatch")
        if not subject_matches:
            mismatches.append("subject name/digest mismatch")
        if not mismatches:
            return
        reasons.extend(mismatches)
    fail("No attestation matched the release policy: " + "; ".join(reasons))


def write_release_nuget_config(args: argparse.Namespace) -> None:
    try:
        tree = ET.parse(args.base)
    except (OSError, ET.ParseError) as exc:
        fail(f"Could not read base NuGet configuration: {exc}")
    root = tree.getroot()
    package_sources = root.find("packageSources")
    mappings = root.find("packageSourceMapping")
    if package_sources is None or mappings is None:
        fail("Base NuGet configuration needs packageSources and packageSourceMapping")
    key = "maui-tizen-release-artifact"
    for item in list(package_sources):
        if item.attrib.get("key") == key:
            package_sources.remove(item)
    ET.SubElement(
        package_sources,
        "add",
        {"key": key, "value": str(args.packages_dir.resolve())},
    )
    for item in list(mappings):
        if item.attrib.get("key") == key:
            mappings.remove(item)
    source = ET.SubElement(mappings, "packageSource", {"key": key})
    ET.SubElement(source, "package", {"pattern": "Maui.Tizen.*"})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    tree.write(args.output, encoding="utf-8", xml_declaration=True)


def fetch_existing_package(
    package_id: str,
    version: str,
    filename: str,
    kind: str,
    existing_dir: Path | None,
    feed_base: str | None,
    symbol_feed_base: str | None,
) -> bytes | None:
    if existing_dir is not None:
        path = existing_dir / filename
        return path.read_bytes() if path.is_file() else None

    if kind == "symbols":
        if not symbol_feed_base:
            fail(
                "Either --symbol-feed-base or --existing-packages-dir is required"
            )
        symbol_url = (
            f"{symbol_feed_base.rstrip('/')}/{package_id}/{version}"
        )
        try:
            with urllib.request.urlopen(symbol_url) as response:
                return response.read()
        except urllib.error.HTTPError as exc:
            if exc.code == 404:
                return None
            fail(
                f"NuGet symbol package query failed for {package_id}: HTTP {exc.code}"
            )
        except OSError as exc:
            fail(f"NuGet symbol package query failed for {package_id}: {exc}")

    if not feed_base:
        fail("Either --feed-base or --existing-packages-dir is required")
    lower_id = package_id.lower()
    lower_version = version.lower()
    index_url = f"{feed_base.rstrip('/')}/{lower_id}/index.json"
    try:
        with urllib.request.urlopen(index_url) as response:
            versions = json.load(response).get("versions") or []
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        fail(f"NuGet index query failed for {package_id}: HTTP {exc.code}")
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"NuGet index query failed for {package_id}: {exc}")
    if lower_version not in {str(item).lower() for item in versions}:
        return None
    package_url = (
        f"{feed_base.rstrip('/')}/{lower_id}/{lower_version}/"
        f"{lower_id}.{lower_version}.nupkg"
    )
    try:
        with urllib.request.urlopen(package_url) as response:
            return response.read()
    except urllib.error.HTTPError as exc:
        fail(f"Existing package download failed for {package_id}: HTTP {exc.code}")


def canonical_package_payload(package: bytes, label: str) -> dict[str, bytes]:
    try:
        with zipfile.ZipFile(io.BytesIO(package)) as archive:
            names = [
                name for name in archive.namelist() if not name.endswith("/")
            ]
            if len({name.lower() for name in names}) != len(names):
                fail(f"Package has duplicate/case-colliding entries: {label}")
            return {
                name: archive.read(name)
                for name in names
                if name.lower() != ".signature.p7s"
            }
    except (zipfile.BadZipFile, KeyError) as exc:
        fail(f"Could not inspect published package {label}: {exc}")


def verify_published_package(
    local_path: Path,
    published: bytes,
    fingerprint: str | None,
    dotnet: str,
    verify_signer: bool,
) -> None:
    if canonical_package_payload(
        local_path.read_bytes(), local_path.name
    ) != canonical_package_payload(published, f"published {local_path.name}"):
        fail(
            f"{local_path.name} already exists with payload that does not "
            "match the signed release manifest"
        )
    if fingerprint and verify_signer:
        with tempfile.TemporaryDirectory(
            prefix="maui-tizen-published-"
        ) as temporary:
            published_path = Path(temporary) / local_path.name
            published_path.write_bytes(published)
            result = subprocess.run(
                [
                    dotnet,
                    "nuget",
                    "verify",
                    "--all",
                    "--certificate-fingerprint",
                    fingerprint,
                    str(published_path),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            if result.returncode != 0:
                fail(
                    f"Published package signer verification failed for "
                    f"{local_path.name}:\n{result.stdout}\n{result.stderr}"
                )


def plan_publication(args: argparse.Namespace) -> None:
    manifest = load_json(args.manifest)
    verify_manifest_header(manifest, "signed")
    plan: list[dict[str, Any]] = []
    for package in manifest["packages"]:
        for package_file in package["files"]:
            existing = fetch_existing_package(
                package["id"],
                package["version"],
                package_file["filename"],
                package_file["kind"],
                args.existing_packages_dir,
                args.feed_base,
                args.symbol_feed_base,
            )
            action = "publish"
            if existing is not None:
                verify_published_package(
                    args.manifest.parent / package_file["filename"],
                    existing,
                    args.certificate_sha256,
                    args.dotnet,
                    package_file["kind"] == "package",
                )
                action = "skip"
            plan.append(
                {
                    "id": package["id"],
                    "version": package["version"],
                    "kind": package_file["kind"],
                    "filename": package_file["filename"],
                    "sha256": package_file["sha256"],
                    "action": action,
                }
            )
    write_json(args.output, {"schemaVersion": 1, "packages": plan})
    if not args.execute:
        return

    if not args.source:
        fail("--source is required when executing publication")
    api_key = os.environ.get(args.api_key_env)
    if not api_key:
        fail(
            f"Environment-scoped credential '{args.api_key_env}' is required "
            "when executing publication"
        )
    for item in plan:
        if item["action"] == "skip":
            continue
        package_path = args.manifest.parent / item["filename"]
        result = subprocess.run(
            [
                args.dotnet,
                "nuget",
                "push",
                str(package_path),
                "--source",
                args.source,
                *(["--no-symbols"] if item["kind"] == "package" else []),
            ],
            cwd=ROOT,
            env={
                **os.environ,
                "NUGET_API_KEY": api_key,
                "NUGET_SYMBOL_API_KEY": api_key,
            },
            text=True,
            capture_output=True,
            check=False,
        )
        if result.returncode != 0:
            fail(
                f"NuGet push failed for {item['filename']}:\n"
                f"{result.stdout}\n{result.stderr}"
            )


def add_manifest_expectations(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--repository", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--artifact-name", required=True)


def add_package_id_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--contracts-dir", type=Path, default=DEFAULT_CONTRACTS)
    parser.add_argument("--expected-package-ids-file", type=Path)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    version = subparsers.add_parser("validate-version")
    version.add_argument("--version", required=True)
    version.add_argument("--baselines", type=Path, default=DEFAULT_BASELINES)
    version.set_defaults(handler=lambda args: validate_version(args.version, args.baselines))

    create_unsigned = subparsers.add_parser("create-unsigned-manifest")
    create_unsigned.add_argument("--packages-dir", type=Path, required=True)
    create_unsigned.add_argument("--output", type=Path, required=True)
    create_unsigned.add_argument("--baselines", type=Path, default=DEFAULT_BASELINES)
    add_manifest_expectations(create_unsigned)
    add_package_id_options(create_unsigned)
    create_unsigned.set_defaults(handler=create_unsigned_manifest)

    verify_unsigned = subparsers.add_parser("verify-unsigned-manifest")
    verify_unsigned.add_argument("--packages-dir", type=Path, required=True)
    verify_unsigned.add_argument("--manifest", type=Path, required=True)
    add_manifest_expectations(verify_unsigned)
    add_package_id_options(verify_unsigned)
    verify_unsigned.set_defaults(handler=verify_unsigned_manifest)

    create_signed = subparsers.add_parser("create-signed-manifest")
    create_signed.add_argument("--unsigned-dir", type=Path, required=True)
    create_signed.add_argument("--unsigned-manifest", type=Path, required=True)
    create_signed.add_argument("--signed-dir", type=Path, required=True)
    create_signed.add_argument("--authenticode-report", type=Path, required=True)
    create_signed.add_argument("--certificate-sha256", required=True)
    create_signed.add_argument("--artifact-name", required=True)
    create_signed.add_argument("--run-attempt", required=True)
    create_signed.add_argument("--output", type=Path, required=True)
    create_signed.add_argument("--attestation-checksums", type=Path, required=True)
    create_signed.set_defaults(handler=create_signed_manifest)

    verify_signed = subparsers.add_parser("verify-signed-manifest")
    verify_signed.add_argument("--signed-dir", type=Path, required=True)
    verify_signed.add_argument("--manifest", type=Path, required=True)
    verify_signed.add_argument("--unsigned-manifest", type=Path)
    verify_signed.add_argument("--certificate-sha256", required=True)
    add_manifest_expectations(verify_signed)
    verify_signed.set_defaults(handler=verify_signed_manifest)

    artifact = subparsers.add_parser("verify-artifact-metadata")
    artifact.add_argument("--metadata", type=Path, required=True)
    artifact.add_argument("--artifact-id", required=True)
    artifact.add_argument("--name", required=True)
    artifact.add_argument("--digest", required=True)
    artifact.add_argument("--run-id", required=True)
    artifact.add_argument("--run-attempt", required=True)
    artifact.add_argument("--source-commit", required=True)
    artifact.add_argument("--source-ref", required=True)
    artifact.set_defaults(handler=verify_artifact_metadata)

    source = subparsers.add_parser("verify-source")
    source.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    source.add_argument("--repository-json", type=Path, required=True)
    source.add_argument("--branch-json", type=Path, required=True)
    source.add_argument("--source-ref", required=True)
    source.add_argument("--source-commit", required=True)
    source.add_argument("--ref-protected", required=True)
    source.add_argument(
        "--require-protected",
        choices=("true", "false"),
        default="true",
    )
    source.set_defaults(handler=verify_source)

    required_checks = subparsers.add_parser("verify-required-checks")
    required_checks.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    required_checks.add_argument("--check-runs-json", type=Path, required=True)
    required_checks.add_argument("--source-commit", required=True)
    required_checks.set_defaults(handler=verify_required_checks)

    workload = subparsers.add_parser("verify-installed-workload")
    workload.add_argument("--baselines", type=Path, default=DEFAULT_BASELINES)
    workload.add_argument("--workload-id", required=True)
    workload.add_argument("--workload-version", required=True)
    workload.add_argument("--package-sha256", required=True)
    workload.add_argument("--signer-fingerprint", required=True)
    workload.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    workload.add_argument("--dotnet-root", type=Path)
    workload.add_argument(
        "--feed-base",
        default="https://api.nuget.org/v3-flatcontainer",
    )
    workload.set_defaults(handler=verify_installed_workload)

    workload_contract_parser = subparsers.add_parser("verify-workload-contract")
    workload_contract_parser.add_argument(
        "--baselines", type=Path, default=DEFAULT_BASELINES
    )
    workload_contract_parser.add_argument("--workload-id", required=True)
    workload_contract_parser.add_argument("--workload-version", required=True)
    workload_contract_parser.add_argument("--package-sha256", required=True)
    workload_contract_parser.add_argument("--signer-fingerprint", required=True)
    workload_contract_parser.set_defaults(handler=verify_workload_contract)

    protections = subparsers.add_parser("verify-protections")
    protections.add_argument("--repository", required=True)
    protections.add_argument("--default-branch", required=True)
    protections.add_argument("--source-branch")
    protections.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    protections.add_argument("--gh", default=os.environ.get("GH", "gh"))
    protections.set_defaults(handler=verify_protections)

    policies = subparsers.add_parser("verify-policies")
    policies.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    policies.add_argument("--baselines", type=Path, default=DEFAULT_BASELINES)
    policies.add_argument("--support-matrix", type=Path, required=True)
    policies.add_argument("--packages-dir", type=Path, required=True)
    policies.add_argument("--manifest", type=Path, required=True)
    policies.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    add_manifest_expectations(policies)
    add_package_id_options(policies)
    policies.set_defaults(handler=verify_policies)

    compare_api = subparsers.add_parser("compare-api")
    compare_api.add_argument("--baseline-dir", type=Path, required=True)
    compare_api.add_argument("--current-dir", type=Path, required=True)
    compare_api.add_argument("--release-version")
    compare_api.add_argument("--baseline-version")
    compare_api.add_argument("--approved-breaks-json", type=Path)
    compare_api.set_defaults(handler=compare_api_command)

    api_baseline = subparsers.add_parser("verify-api-baseline")
    api_baseline.add_argument("--baseline-dir", type=Path, required=True)
    api_baseline.add_argument("--version", required=True)
    api_baseline.set_defaults(
        handler=lambda args: verify_api_baseline_manifest(
            args.baseline_dir, args.version
        )
    )

    msbuild_baseline = subparsers.add_parser("verify-msbuild-contracts")
    msbuild_baseline.add_argument("--packages-dir", type=Path, required=True)
    msbuild_baseline.add_argument("--baseline-dir", type=Path, required=True)
    msbuild_baseline.add_argument("--version", required=True)
    msbuild_baseline.set_defaults(
        handler=lambda args: verify_msbuild_contracts(
            args.packages_dir, args.baseline_dir, args.version
        )
    )

    attestation = subparsers.add_parser("verify-attestation-report")
    attestation.add_argument("--report", type=Path, required=True)
    attestation.add_argument("--repository", required=True)
    attestation.add_argument("--source-commit", required=True)
    attestation.add_argument("--source-ref", required=True)
    attestation.add_argument("--run-id", required=True)
    attestation.add_argument("--run-attempt", required=True)
    attestation.add_argument("--subject-name", required=True)
    attestation.add_argument("--subject-digest", required=True)
    attestation.set_defaults(handler=verify_attestation_report)

    nuget_config = subparsers.add_parser("write-nuget-config")
    nuget_config.add_argument("--base", type=Path, required=True)
    nuget_config.add_argument("--packages-dir", type=Path, required=True)
    nuget_config.add_argument("--output", type=Path, required=True)
    nuget_config.set_defaults(handler=write_release_nuget_config)

    publication = subparsers.add_parser("plan-publication")
    publication.add_argument("--manifest", type=Path, required=True)
    publication.add_argument("--output", type=Path, required=True)
    publication.add_argument("--feed-base")
    publication.add_argument("--symbol-feed-base")
    publication.add_argument("--existing-packages-dir", type=Path)
    publication.add_argument("--execute", action="store_true")
    publication.add_argument("--source")
    publication.add_argument(
        "--api-key-env", default="MAUI_TIZEN_NUGET_TOKEN"
    )
    publication.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    publication.add_argument("--certificate-sha256")
    publication.set_defaults(handler=plan_publication)

    return parser


def main() -> int:
    try:
        args = build_parser().parse_args()
        args.handler(args)
        return 0
    except ContractError as exc:
        print(f"release contract failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
