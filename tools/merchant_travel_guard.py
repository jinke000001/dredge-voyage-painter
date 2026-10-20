#!/usr/bin/env python3
"""Fail-closed local launcher for the isolated merchant-travel copy.

This helper uses only the standard library. It never opens a formal save and
never changes Steam preferences. Steam Auto-Cloud behavior requires separate
native evidence; a different Unity profile is not proof of no Cloud activity.
"""
from __future__ import annotations

import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import plistlib
import pwd
import stat
import subprocess
import sys


IDENTIFIER = "local.jinke.dredge.merchanttravel.qa.20261018"
COMPANY = "Jinke Local Tests"
PRODUCT = "DREDGE Merchant Travel QA 20261018"
ORIGINAL_SHA = "a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729"
APP_ID = "1562430"
PROOF_REL = "Contents/Resources/merchant-travel-proof.json"
MANIFEST_REL = "Contents/Resources/merchant-travel-files.json"
GUARD_REL = "Contents/Resources/merchant_travel_guard.py"
PROOF_ENV = "JINKE_DREDGE_MERCHANT_TRAVEL_PROOF"
PROFILE_ENV = "JINKE_DREDGE_MERCHANT_TRAVEL_PROFILE"
IGNORED_NAMES = {".DS_Store"}
CORE_FILES = {
    "assemblySha256": "Contents/Resources/Data/Managed/Assembly-CSharp.dll",
    "runtimeSha256": "Contents/Resources/Data/Managed/Jinke.DredgeMerchantTravel.dll",
    "globalGameManagersSha256": "Contents/Resources/Data/globalgamemanagers",
    "infoPlistSha256": "Contents/Info.plist",
    "appInfoSha256": "Contents/Resources/Data/app.info",
    "launchGuardSha256": GUARD_REL,
}


class Refusal(RuntimeError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Refusal(message)


def absolute(path: str | Path) -> Path:
    return Path(os.path.abspath(os.fspath(path)))


def is_child(path: Path, root: Path) -> bool:
    return path != root and path.is_relative_to(root)


def no_symlinks(path: str | Path) -> Path:
    """Check before resolving: resolving first would erase the evidence."""
    path = absolute(path)
    for part in [path, *path.parents]:
        try:
            mode = part.lstat().st_mode
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(mode), "Path contains a symlink: " + str(part))
    return path


def regular_file(path: Path) -> os.stat_result:
    no_symlinks(path)
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode), "Expected a regular file: " + str(path))
    require(info.st_nlink == 1, "Hard-linked files are forbidden: " + str(path))
    return info


def sha(path: str | Path) -> str:
    path = Path(path)
    before = regular_file(path)
    digest = hashlib.sha256()
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as stream:
        opened = os.fstat(stream.fileno())
        require((before.st_dev, before.st_ino) == (opened.st_dev, opened.st_ino), "File changed during validation: " + str(path))
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
        after = os.fstat(stream.fileno())
        require((opened.st_size, opened.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "File changed while hashing: " + str(path))
    return digest.hexdigest()


def ignored(name: str) -> bool:
    return name.startswith("._") or name in IGNORED_NAMES


def files_below(root: Path):
    """Inspect every entry, including ignored metadata, without following links."""
    no_symlinks(root)
    require(root.is_dir(), "Missing directory: " + str(root))
    def traversal_failed(error):
        raise error

    for base, directories, files in os.walk(root, followlinks=False, onerror=traversal_failed):
        directories.sort()
        files.sort()
        for name in directories + files:
            path = Path(base) / name
            info = path.lstat()
            require(not stat.S_ISLNK(info.st_mode), "Tree contains a symlink: " + str(path))
            require(stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode), "Unsupported filesystem entry: " + str(path))
            if stat.S_ISREG(info.st_mode):
                require(info.st_nlink == 1, "Tree contains a hard link: " + str(path))
        directories[:] = [name for name in directories if not ignored(name)]
        for name in files:
            if not ignored(name):
                yield Path(base) / name


def tree_manifest(root: Path, *, app: bool = False, hash_contents: bool = True) -> dict:
    result = {}
    for path in files_below(root):
        relative = path.relative_to(root).as_posix()
        # The main Mach-O contains the signature which seals this manifest.
        # Including its bytes here would create a cryptographic cycle. codesign
        # verifies it; the build's external sealed manifest records every byte.
        if app and (relative in (MANIFEST_REL, "Contents/MacOS/DREDGE") or relative.startswith("Contents/_CodeSignature/")):
            continue
        info = path.stat()
        record = {"bytes": info.st_size, "mode": stat.S_IMODE(info.st_mode)}
        if hash_contents:
            record["sha256"] = sha(path)
        result[relative] = record
    return result


def user_home() -> Path:
    return absolute(pwd.getpwuid(os.getuid()).pw_dir)


def profile_root() -> Path:
    return user_home() / "Library" / "Application Support" / IDENTIFIER


def check_profile(path: str | Path) -> Path:
    path = no_symlinks(path)
    require(path == profile_root(), "Save profile must be the fixed isolated merchant-travel profile.")
    if path.exists():
        # File metadata only. No save is opened or deserialized by this launcher.
        list(files_below(path))
    return path


def read_json(path: Path) -> dict:
    regular_file(path)
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict), "Expected a JSON object: " + str(path))
    return value


def verify_manifest(app: Path, saved: dict, *, full_hash: bool, known_hashes: dict) -> None:
    require(saved.get("schemaVersion") == 1 and isinstance(saved.get("files"), dict), "Unsupported app file manifest.")
    expected = saved["files"]
    actual = tree_manifest(app, app=True, hash_contents=full_hash)
    if full_hash:
        require(expected == actual, "App file manifest differs; rebuild or reseal the explicit QA copy.")
        return
    metadata = {relative: {"bytes": value["bytes"], "mode": value["mode"]} for relative, value in expected.items()}
    require(metadata == actual, "App file inventory, size or mode differs; rebuild or reseal the explicit QA copy.")
    envelope = app / "Contents/_CodeSignature/CodeResources"
    regular_file(envelope)
    sealed = plistlib.loads(envelope.read_bytes()).get("files2")
    require(isinstance(sealed, dict) and bool(sealed), "A version-2 resource envelope is required.")
    for relative, value in expected.items():
        resource = sealed.get(relative.removeprefix("Contents/"))
        sealed_hash = resource.get("hash2") if isinstance(resource, dict) else None
        if isinstance(sealed_hash, bytes) and len(sealed_hash) == 32:
            # Strict codesign verification below checks these same file bytes.
            # Bind its SHA-256 expectation to our original manifest, rather than
            # accepting any newly signed content. Apple TN3126 documents hash2.
            require(sealed_hash.hex() == value["sha256"], "Signed resource fingerprint differs from app manifest: " + relative)
        else:
            # Nested code can satisfy its designated requirement after being
            # replaced. Keep exact whole-file SHA checks for those entries and
            # for any file without a direct SHA-256 resource seal.
            fingerprint = known_hashes.get(relative)
            if fingerprint is None:
                fingerprint = sha(app / relative)
            require(fingerprint == value["sha256"], "Unsealed or nested-code file fingerprint differs: " + relative)


def verify_app(app: str | Path, *, signature: bool = True, full_hash: bool | None = None) -> dict:
    if full_hash is None:
        full_hash = not signature
    require(signature or full_hash, "Cannot omit both signature validation and the complete hash audit.")
    app = no_symlinks(app)
    require(app.suffix == ".app" and app.is_dir(), "Expected an existing isolated .app directory.")
    proof_path = app / PROOF_REL
    proof = read_json(proof_path)
    expected = {
        "schemaVersion": 1,
        "mode": "merchant-travel-isolated",
        "bundleIdentifier": IDENTIFIER,
        "company": COMPANY,
        "product": PRODUCT,
        "appPath": str(app),
        "persistentDataPath": str(profile_root()),
        "originalAssemblySha256": ORIGINAL_SHA,
        "steamAppId": int(APP_ID),
    }
    for key, value in expected.items():
        require(proof.get(key) == value, "Application proof mismatch: " + key)
    require(proof.get("validationRole") in ("production-only", "qa-instrumented"), "Unrecognized app role.")
    output = no_symlinks(proof["allowedOutputRoot"])
    source = no_symlinks(proof["originalContentsRoot"])
    require(output.name == "output" and source == output.parent / "Contents", "Unexpected workspace layout.")
    require(is_child(app, output) and not is_child(app, source), "App is outside the isolated output tree.")
    check_profile(proof["persistentDataPath"])
    require(os.environ.get("HOME", str(user_home())) == str(user_home()), "HOME differs from this user's real profile.")
    known_hashes = {}
    for key, relative in CORE_FILES.items():
        known_hashes[relative] = sha(app / relative)
        require(proof.get(key) == known_hashes[relative], "Core file fingerprint mismatch: " + relative)
    plist = plistlib.loads((app / "Contents/Info.plist").read_bytes())
    require(plist.get("CFBundleIdentifier") == IDENTIFIER and plist.get("CFBundleExecutable") == "DREDGE", "Bundle identity or executable differs.")
    require((app / "Contents/Resources/Data/app.info").read_text(encoding="utf-8").splitlines() == [COMPANY, PRODUCT], "Unity company/product differs.")
    for directory in ("Contents/Resources",):
        marker = app / directory / "steam_appid.txt"
        regular_file(marker)
        require(marker.read_text(encoding="ascii") == APP_ID + "\n", "Wrong Steam app marker.")
    saved = read_json(app / MANIFEST_REL)
    verify_manifest(app, saved, full_hash=full_hash, known_hashes=known_hashes)
    if signature:
        subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(app)], check=True)
    return proof


def process_running(name: str) -> bool:
    result = subprocess.run(["/usr/bin/pgrep", "-x", name], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    require(result.returncode in (0, 1), "Cannot determine running process state: " + name)
    return result.returncode == 0


def launch_environment(app: Path, proof: dict) -> dict:
    environment = dict(os.environ)
    # Reject external native/managed injection rather than silently ignoring it.
    require(not any(value for key, value in environment.items() if key.startswith("DYLD_") or key in ("MONO_PATH", "MONO_ENV_OPTIONS")), "External runtime injection variables are set.")
    environment.pop("JINKE_DREDGE_SKINS_SETTINGS_DIR", None)
    environment[PROOF_ENV] = str(app / PROOF_REL)
    environment[PROFILE_ENV] = proof["persistentDataPath"]
    environment["SteamAppId"] = APP_ID
    environment["SteamGameId"] = APP_ID
    return environment


def launch(app: str | Path) -> int:
    print("正在核验独立游戏副本…", flush=True)
    app = absolute(app)
    proof = verify_app(app)
    require(not process_running("DREDGE"), "DREDGE is already running. Quit it before opening this isolated copy.")
    require(process_running("steam_osx"), "Steam is not running. Open Steam and sign in, then use this launcher again.")
    environment = launch_environment(app, proof)
    logs = no_symlinks(Path(proof["allowedOutputRoot"]) / "runtime" / "merchant-travel-launches")
    logs.mkdir(parents=True, exist_ok=True)
    lock_path = logs / "launch.lock"
    if lock_path.exists() or lock_path.is_symlink():
        regular_file(lock_path)
    descriptor = os.open(lock_path, os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "w") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise Refusal("Another merchant-travel launcher already owns the launch lock.") from error
        require(not process_running("DREDGE"), "DREDGE started during validation; refusing a second instance.")
        stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
        log = logs / (stamp + "-player.log")
        with log.open("x", encoding="utf-8"):
            pass
        command = [str(app / "Contents/MacOS/DREDGE"), "-screen-width", "1280", "-screen-height", "800", "-screen-fullscreen", "0", "-logFile", str(log)]
        print("独立体验版存档：" + proof["persistentDataPath"], flush=True)
        print("本次日志：" + str(log), flush=True)
        result = subprocess.run(command, cwd=app / "Contents/Resources", env=environment, check=False)
        return result.returncode


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--check-only", action="store_true", help="Run the complete file SHA-256 audit and signature check without launching or creating save files.")
    args = parser.parse_args(argv)
    try:
        if args.check_only:
            proof = verify_app(args.app, full_hash=True)
            print(json.dumps({"status": "verified-static-only", "app": str(absolute(args.app)), "profile": proof["persistentDataPath"], "gameLaunched": False}, ensure_ascii=False))
            return 0
        return launch(args.app)
    except (Refusal, OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print("REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
