#!/usr/bin/env python3
"""Rebuild and run the one explicit merchant-travel QA app, never a delivery.

Usage: python3 tools/run_merchant_travel_qa.py 05
Creates fresh QA-managed-05 and session-05 under output/runtime/merchanttravel-testing.
The original package and formal saves are never written; save contents are not read.
This runner does not prepare fixtures, change Steam preferences, or infer acceptance
from a successful process exit. Inspect the separate probe results for acceptance.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import datetime as dt
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys

import build_merchant_travel as builder
import merchant_travel_guard as guard


ROOT = builder.ROOT
QA_ROOT = ROOT / "output/runtime/merchanttravel-testing"
QA_APP = ROOT / "output/runtime/DREDGE-MerchantTravel-QA-03.app"
ORIGINAL = ROOT / "Contents/Resources/Data/Managed/Assembly-CSharp.dll"
QA_PROJECT = ROOT / "src/MerchantTravelQA/MerchantTravelQA.csproj"
PATCH_PROJECT = ROOT / "src/MerchantTravelQA/PatchHelper/PatchHelper.csproj"
PROBE_NAME = "Jinke.DredgeMerchantTravel.QA"
QA_HOOKS = {
    "System.Void PlayerColorCustomizer::RefreshBoatColors()": PROBE_NAME + ".Probe.AfterPlayer(object)",
    "System.Void ContinueOrNewButton::Start()": PROBE_NAME + ".Probe.AfterTitle(object)",
}
COPY_PATHS = {
    "Contents/Resources/Data/Managed/Assembly-CSharp.dll",
    "Contents/Resources/Data/Managed/Jinke.DredgeMerchantTravel.dll",
    "Contents/Resources/Data/Managed/Jinke.DredgeMerchantTravel.QA.dll",
    guard.GUARD_REL,
}


def utc() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat()


def session_paths(name: str) -> tuple[Path, Path]:
    guard.require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,39}", name) is not None,
                  "Use a short name containing only letters, digits and hyphens, such as 05.")
    root = guard.no_symlinks(QA_ROOT)
    guard.require(root.is_dir(), "The existing explicit QA evidence root is required.")
    staging, session = root / ("QA-managed-" + name), root / ("session-" + name)
    for path in (staging, session):
        guard.no_symlinks(path)
        guard.require(not path.exists(), "Refusing to reuse a staging or session path: " + str(path))
    return staging, session


def require_idle() -> None:
    guard.require(not guard.process_running("DREDGE"), "DREDGE is running. Exit it before rebuilding the QA app.")
    guard.require(guard.process_running("steam_osx"), "Steam must already be running and signed in.")


@contextmanager
def launch_lock():
    # Share the delivered launcher's lock, so it cannot launch during QA mutation.
    directory = guard.no_symlinks(ROOT / "output/runtime/merchant-travel-launches")
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / "launch.lock"
    if path.exists() or path.is_symlink():
        guard.regular_file(path)
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "w") as stream:
        info = os.fstat(stream.fileno())
        guard.require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1, "Unsafe launch lock.")
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise guard.Refusal("Another merchant-travel runner owns the launch lock.") from error
        yield


def source_snapshot() -> dict:
    result = builder.managed_sources()
    for path in (ROOT / "src/MerchantTravelQA").rglob("*"):
        relative = path.relative_to(ROOT / "src/MerchantTravelQA")
        if any(part.lower() in ("bin", "obj") or guard.ignored(part) for part in relative.parts):
            continue
        guard.no_symlinks(path)
        if path.is_file() and (path.suffix in (".cs", ".csproj") or path.name == "packages.lock.json"):
            result[path.relative_to(ROOT).as_posix()] = guard.sha(path)
    for name in ("build_merchant_travel.py", "merchant_travel_guard.py", "run_merchant_travel_qa.py"):
        path = ROOT / "tools" / name
        result[path.relative_to(ROOT).as_posix()] = guard.sha(path)
    return result


def compile_probe(staging: Path) -> tuple[Path, Path, list]:
    # Keep the PatchHelper ProjectReference rebuild separate from release artifacts.
    artifacts = staging / "qa-managed"
    commands = []
    for project in (QA_PROJECT, PATCH_PROJECT):
        guard.regular_file(project)
        command = ["dotnet", "build", str(project), "-c", "Release", "--no-incremental",
                   "--artifacts-path", str(artifacts), "-p:RestoreLockedMode=true"]
        builder.run(*command)
        commands.append(command)
    probe = artifacts / "bin/MerchantTravelQA/release/Jinke.DredgeMerchantTravel.QA.dll"
    helper = artifacts / "bin/PatchHelper/release/PatchHelper.dll"
    for path in (probe, helper):
        guard.regular_file(path)
    return probe, helper, commands


def validate_qa_audit(path: Path, release_audit: dict, paths: dict) -> dict:
    report = guard.read_json(path)
    guard.require(report.get("QA_ONLY_DO_NOT_SHIP") is True, "PatchHelper did not identify its output as QA-only.")
    fields = {"original": "originalSha256", "releasePatched": "releasePatchSha256",
              "runtime": "unchangedReleaseRuntimeSha256", "probe": "probeSha256", "qaOutput": "qaOutputSha256"}
    guard.require(report.get("paths") == {key: str(value) for key, value in paths.items()}, "QA audit paths differ from fresh inputs.")
    for key, field in fields.items():
        guard.require(report.get(field) == guard.sha(paths[key]), "QA audit artifact fingerprint differs: " + key)
    guard.require(report.get("releaseAudit") == release_audit["audit"], "PatchHelper did not independently reproduce the release semantic audit.")
    audit = report.get("qaAudit", {})
    guard.require(audit.get("passed") is True and audit.get("releaseRuntimeUnmodified") is True,
                  "QA semantic audit failed or modified the release runtime.")
    reference = audit.get("addedAssemblyReference", "")
    guard.require(isinstance(reference, str) and reference.split(",", 1)[0] == PROBE_NAME, "Unexpected QA assembly reference.")
    changed = audit.get("changedMethods", [])
    guard.require(isinstance(changed, list) and len(changed) == 2 and all(isinstance(item, dict) for item in changed),
                  "QA audit must contain exactly two additional methods.")
    guard.require({item.get("method"): item.get("addedCall") for item in changed} == QA_HOOKS, "Unexpected QA hook set.")
    for item in changed:
        before = item.get("beforeBodySha256")
        guard.require(isinstance(before, str) and re.fullmatch(r"[0-9a-f]{64}", before) is not None
                      and item.get("restoredBodySha256") == before, "QA hook did not restore its release method body.")
    guard.require(audit.get("unchangedMethodBodies") == release_audit["audit"]["totalMethodBodies"] - 2,
                  "Unexpected number of unchanged QA method bodies.")
    return report


def install_file(source: Path, relative: str, token: str) -> None:
    guard.require(relative in COPY_PATHS, "Runner copy is outside its four-file allowlist.")
    target = guard.no_symlinks(QA_APP / relative)
    mode = stat.S_IMODE(guard.regular_file(target).st_mode)
    temporary = target.with_name(target.name + ".qa-runner-" + token)
    guard.no_symlinks(temporary)
    fingerprint = guard.sha(source)
    created = False
    try:
        with temporary.open("xb") as writer:
            created = True
            with source.open("rb") as reader:
                shutil.copyfileobj(reader, writer, length=1024 * 1024)
        temporary.chmod(mode)
        guard.require(guard.sha(temporary) == fingerprint, "QA copy fingerprint differs: " + relative)
        os.replace(temporary, target)
    finally:
        if created and temporary.exists():
            guard.regular_file(temporary)
            temporary.unlink()


def validate_reseal_delta(before: dict, after: dict) -> list[str]:
    allowed = COPY_PATHS | {guard.PROOF_REL}
    changed = [name for name in sorted(before.keys() | after.keys()) if before.get(name) != after.get(name)]
    guard.require(all(name in allowed and name in before and name in after for name in changed),
                  "QA reseal changed another app file; refusing launch.")
    return changed


def prepare_and_launch(name: str) -> int:
    # Fail before hashing/building/writing when another native run is active.
    require_idle()
    staging, session = session_paths(name)
    guard.no_symlinks(QA_APP)
    guard.require(QA_APP.is_dir(), "The fixed QA-03 app must already exist; this runner cannot create an app.")
    for command in ("dotnet", "codesign"):
        guard.require(shutil.which(command) is not None, "Required tool is unavailable: " + command)
    guard.require(guard.sha(ORIGINAL) == guard.ORIGINAL_SHA, "Original assembly fingerprint differs.")
    with launch_lock():
        require_idle()
        print("正在核验现有 QA 副本并准备全新编译…", flush=True)
        old_proof = guard.verify_app(QA_APP)
        guard.require(old_proof.get("validationRole") == "qa-instrumented", "Refusing to instrument a production-only app.")
        # Validate environment before starting an expensive build or changing QA files.
        guard.launch_environment(QA_APP, old_proof)
        before = guard.read_json(QA_APP / guard.MANIFEST_REL)["files"]
        staging.mkdir()
        session.mkdir()
        phase = "compile"
        try:
            sources = source_snapshot()
            runtime, weaver, managed = builder.build_managed(staging)
            release = staging / "Assembly-CSharp.dll"
            builder.run("dotnet", weaver, "--merchant-travel", ORIGINAL, release, runtime)
            release_audit_path = Path(str(release) + ".audit.json")
            release_audit = builder.validate_weaver_audit(release_audit_path, ORIGINAL, release, runtime)
            probe, helper, qa_commands = compile_probe(staging)
            qa_output = staging / "QA-Assembly-CSharp.dll"
            paths = {"original": ORIGINAL, "releasePatched": release, "runtime": runtime, "probe": probe, "qaOutput": qa_output}
            builder.run("dotnet", helper, *(paths[key] for key in ("original", "releasePatched", "runtime", "probe", "qaOutput")))
            qa_audit_path = Path(str(qa_output) + ".qa-only.audit.json")
            qa_audit = validate_qa_audit(qa_audit_path, release_audit, paths)
            guard.require(source_snapshot() == sources, "Sources changed during QA compilation; use a new session name after they are frozen.")
            guard.require(guard.sha(runtime) == managed["runtimeArtifactSha256"], "QA compilation replaced the fresh release runtime.")
            guard.require(guard.sha(ORIGINAL) == guard.ORIGINAL_SHA, "Original assembly changed during QA compilation.")
            builder.write_json(staging / "qa-build.json", {
                "schemaVersion": 1, "QA_ONLY_DO_NOT_SHIP": True, "utc": utc(), "sources": sources,
                "qaBuildCommands": qa_commands, "probeSha256": guard.sha(probe), "patchHelperSha256": guard.sha(helper),
                "releaseAuditSha256": guard.sha(release_audit_path), "qaAuditSha256": guard.sha(qa_audit_path),
                "qaOutputSha256": qa_audit["qaOutputSha256"],
            }, readonly=True)
            phase = "install-and-reseal"
            require_idle()
            copies = {
                "Contents/Resources/Data/Managed/Assembly-CSharp.dll": qa_output,
                "Contents/Resources/Data/Managed/Jinke.DredgeMerchantTravel.dll": runtime,
                "Contents/Resources/Data/Managed/Jinke.DredgeMerchantTravel.QA.dll": probe,
                guard.GUARD_REL: ROOT / "tools/merchant_travel_guard.py",
            }
            for relative, source in copies.items():
                install_file(source, relative, name)
            print("正在重封 QA 副本并进行完整文件核验…", flush=True)
            proof = builder.refresh_qa_metadata(QA_APP, weaver_audit_sha256=guard.sha(release_audit_path))
            changed = validate_reseal_delta(before, guard.read_json(QA_APP / guard.MANIFEST_REL)["files"])
            guard.require(source_snapshot() == sources, "Sources changed before launch; refusing to run a stale QA snapshot.")
            builder.write_json(staging / "reseal.json", {
                "utc": utc(), "app": str(QA_APP), "changedManifestFiles": changed,
                "proofSha256": guard.sha(QA_APP / guard.PROOF_REL), "manifestSha256": guard.sha(QA_APP / guard.MANIFEST_REL),
                "validation": "refresh_qa_metadata completed strict/deep codesign and full file SHA audit",
            }, readonly=True)
            phase = "launch"
            require_idle()
            environment = guard.launch_environment(QA_APP, proof)
            environment.update({"JINKE_MERCHANT_QA": "1", "JINKE_MERCHANT_QA_OUTPUT": str(session)})
            player_log = session / "player.log"
            with player_log.open("x", encoding="utf-8"):
                pass
            command = [str(QA_APP / "Contents/MacOS/DREDGE"), "-screen-width", "1280", "-screen-height", "800",
                       "-screen-fullscreen", "0", "-logFile", str(player_log)]
            builder.write_json(session / "launch.json", {
                "utc": utc(), "state": "launch-requested", "app": str(QA_APP), "managedBuild": str(staging),
                "command": command, "cwd": str(QA_APP / "Contents/Resources"),
                "runtimeSha256": proof["runtimeSha256"], "probeSha256": guard.sha(probe),
                "environment": {key: environment[key] for key in (guard.PROOF_ENV, guard.PROFILE_ENV, "SteamAppId", "SteamGameId", "JINKE_MERCHANT_QA", "JINKE_MERCHANT_QA_OUTPUT")},
            }, readonly=True)
            print("启动独立 QA 会话：" + str(session), flush=True)
            with (session / "console.log").open("x", encoding="utf-8") as console:
                process = subprocess.Popen(command, cwd=QA_APP / "Contents/Resources", env=environment,
                                           stdout=console, stderr=subprocess.STDOUT)
                builder.write_json(session / "process.json", {"utc": utc(), "pid": process.pid, "state": "native-process-started"}, readonly=True)
                while True:
                    try:
                        return_code = process.wait()
                        break
                    except KeyboardInterrupt:
                        print("QA 进程仍由此 runner 跟踪；请在游戏中退出。", flush=True)
            builder.write_json(session / "exit.json", {
                "utc": utc(), "pid": process.pid, "returnCode": return_code, "state": "native-process-exited",
                "acceptance": "Process exit is not gameplay acceptance; inspect the separate probe and UI evidence.",
            }, readonly=True)
            return return_code
        except Exception as error:
            builder.write_json(session / "runner-failed.json", {"utc": utc(), "phase": phase, "error": type(error).__name__ + ": " + str(error)}, readonly=True)
            raise


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("name", help="New short suffix for both QA-managed-NAME and session-NAME; existing paths are refused.")
    args = parser.parse_args(argv)
    try:
        return prepare_and_launch(args.name)
    except (guard.Refusal, OSError, ValueError, subprocess.CalledProcessError) as error:
        print("QA RUNNER REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
