#!/usr/bin/env python3
"""Build a new, isolated merchant-travel app. Never launch or overwrite it.

Requires dotnet, codesign and UnityPy in the build interpreter. The launcher has
no third-party Python dependencies. Original Contents and all save files are
read-only inputs (save bodies are not read at all by this builder).
"""
from __future__ import annotations

import argparse
import datetime as dt
import copy
from contextlib import contextmanager
import hashlib
import importlib
import json
import os
from pathlib import Path
import plistlib
import shlex
import shutil
import subprocess
import sys
import types

import merchant_travel_guard as guard
_merchant_guard = guard


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_NAME = "DREDGE 商人传送体验版.app"
EXPECTED_METHODS = {
    "System.Void SteamManager::Awake()", "System.Void SaveManager::Init()",
    "System.Void PlayerColorCustomizer::RefreshBoatColors()", "System.Void AbilityBarUI::OnCurrentPressComplete()",
}
VOYAGE_METHODS = EXPECTED_METHODS | {
    "System.Boolean SerializedCrabPotPOIData::AdjustDurability(System.Single)",
    "System.Void TrawlNetAbility::Update()",
    "System.Void TooltipSectionDeployableDetails::RefreshUI()",
    "System.Void OccasionalGridPanel::RefreshDurabilityUI()",
    "System.Void NetPanel::RefreshUI()",
    "System.Void SpiralMinigame::GenerateTargets()",
    "System.Void PlayerSanity::Update()",
    "System.Void SerializableGrid::Init(GridConfiguration,System.Boolean)",
    "System.Void SaveData::Init()",
    "System.Void UpgradeManager::OnUpgradesChanged(UpgradeData)",
}
PAINTER_METHODS = VOYAGE_METHODS | {
    "System.Void PlayerColorCustomizer::RefreshBoatFlag()",
    "System.Void DredgeDialogueRunner::Awake()",
    "System.Void DredgeDialogueRunner::StartDialogue(System.String)",
}
PROFILES = {"merchant-travel": EXPECTED_METHODS, "voyage-experience": VOYAGE_METHODS, "voyage-painter": PAINTER_METHODS}
VOYAGE_MAX_STACK_CHANGES = [
    {"method": "System.Boolean SerializedCrabPotPOIData::AdjustDurability(System.Single)", "original": 5, "patched": 6},
    {"method": "System.Void NetPanel::RefreshUI()", "original": 5, "patched": 6},
    {"method": "System.Void OccasionalGridPanel::RefreshDurabilityUI()", "original": 5, "patched": 6},
    {"method": "System.Void PlayerSanity::Update()", "original": 4, "patched": 5},
    {"method": "System.Void TooltipSectionDeployableDetails::RefreshUI()", "original": 5, "patched": 6},
    {"method": "System.Void TrawlNetAbility::Update()", "original": 4, "patched": 5},
]
ORIGINAL_MVID = "d810863b-fda8-4056-bdde-77f7c349ae63"
VOYAGE_IDENTITY = {
    "IDENTIFIER": ("local.jinke.dredge.merchanttravel.qa.20261018", "local.jinke.dredge.voyage.qa.20261019"),
    "COMPANY": ("Jinke Local Tests", "Jinke Local Tests"),
    "PRODUCT": ("DREDGE Merchant Travel QA 20261018", "DREDGE Voyage QA 20261019"),
}
PAINTER_IDENTITY = {
    "IDENTIFIER": ("local.jinke.dredge.merchanttravel.qa.20261018", "local.jinke.dredge.voyage.painter.qa.20261019"),
    "COMPANY": ("Jinke Local Tests", "Jinke Local Tests"),
    "PRODUCT": ("DREDGE Merchant Travel QA 20261018", "DREDGE Voyage Painter QA 20261019"),
}
PROFILE_IDENTITIES = {"voyage-experience": VOYAGE_IDENTITY, "voyage-painter": PAINTER_IDENTITY}
PAINTER_SHARED_SOURCES = ("AtlasLayout.cs", "PackContract.cs", "SkinJson.cs", "SkinAssets.cs", "RendererOverrides.cs")
PAINTER_ASSET_FILES = ("manifest.json", "ramen/flag.png", "octopus/flag.png", "moon/flag.png")
PAINTER_ASSET_REL = "Resources/Data/StreamingAssets/JinkeSkins"


def guard_source(profile: str) -> str:
    require_profile(profile)
    source = Path(__file__).with_name("merchant_travel_guard.py").read_text(encoding="utf-8")
    if profile in PROFILE_IDENTITIES:
        for name, (original, replacement) in PROFILE_IDENTITIES[profile].items():
            line = name + " = " + json.dumps(original)
            guard.require(source.count(line + "\n") == 1, "Guard identity declaration changed: " + name)
            source = source.replace(line + "\n", name + " = " + json.dumps(replacement) + "\n", 1)
    compile(source, "isolated-launch-guard", "exec")
    return source


def guard_for_profile(profile: str):
    require_profile(profile)
    if profile == "merchant-travel":
        return _merchant_guard
    selected = types.ModuleType("merchant_travel_guard_" + profile.replace("-", "_"))
    exec(compile(guard_source(profile), "isolated-launch-guard", "exec"), selected.__dict__)
    # All callers catch the same fail-closed exception while the functions and
    # constants stay bound to the selected module's isolated global namespace.
    selected.Refusal = _merchant_guard.Refusal
    return selected


@contextmanager
def profile_context(profile: str):
    """Single-process build/QA context for the fixed audited identities."""
    global guard
    previous = guard
    guard = guard_for_profile(profile)
    try:
        yield guard
    finally:
        guard = previous


def run(*args) -> None:
    subprocess.run([str(value) for value in args], cwd=ROOT, check=True)


def write_json(path: Path, value: dict, *, readonly: bool = False) -> None:
    guard.no_symlinks(path)
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    if readonly:
        path.chmod(0o444)


def require_new_output(path: Path) -> None:
    path = guard.no_symlinks(path)
    output = guard.no_symlinks(ROOT / "output")
    guard.require(guard.is_child(path, output), "Output must be a new path below this workspace's output/ directory.")
    guard.require(not path.exists() and not path.is_symlink(), "Refusing to overwrite existing output: " + str(path))


def validate_name(name: str) -> str:
    guard.require(name.endswith(".app") and Path(name).name == name and name not in (".app", "..app"), "--name must be a single nonempty .app filename.")
    guard.require(not any(ord(character) < 32 or ord(character) == 127 for character in name), "Control characters are forbidden in app names.")
    return name


def require_profile(profile: str) -> set[str]:
    guard.require(profile in PROFILES, "Unknown fixed build profile: " + profile)
    return PROFILES[profile]


def paths_for(name: str, profile: str = "merchant-travel") -> tuple[Path, Path, Path]:
    require_profile(profile)
    name = validate_name(name)
    app = ROOT / "output" / name
    staging = ROOT / "output/builds" / (name[:-4] + "-" + profile)
    launcher = ROOT / "output" / (name[:-4] + " — 启动.command")
    for path in (app, staging, launcher):
        require_new_output(path)
    guard.require(not guard.is_child(app, ROOT / "Contents"), "Original Contents is read-only.")
    return app, staging, launcher


def preflight(name: str, profile: str = "merchant-travel"):
    paths = paths_for(name, profile)
    source = guard.no_symlinks(ROOT / "Contents")
    original = source / "Resources/Data/Managed/Assembly-CSharp.dll"
    guard.require(guard.sha(original) == guard.ORIGINAL_SHA, "Original game assembly fingerprint differs; refusing to build.")
    guard.check_profile(guard.profile_root())
    for command in ("dotnet", "codesign"):
        guard.require(shutil.which(command) is not None, "Required build tool is unavailable: " + command)
    unity = importlib.import_module("UnityPy")
    baseline = guard.tree_manifest(source)
    return paths, unity, baseline


def build_managed(staging: Path) -> tuple[Path, Path, dict]:
    artifacts = staging / "managed"
    artifacts.mkdir()
    commands = []
    sources = managed_sources()
    sdk = subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip()
    for project in ("MerchantTravel", "Weaver"):
        command = ["dotnet", "build", str(ROOT / "src" / project / (project + ".csproj")), "-c", "Release", "--no-incremental", "--artifacts-path", str(artifacts)]
        if project == "Weaver":
            command.append("-p:RestoreLockedMode=true")
        commands.append(command)
        run(*command)
    guard.require(managed_sources() == sources, "Managed source changed during compilation; rebuild in a fresh destination.")
    runtime = artifacts / "bin/MerchantTravel/release/Jinke.DredgeMerchantTravel.dll"
    weaver = artifacts / "bin/Weaver/release/Weaver.dll"
    guard.require(runtime.is_file() and weaver.is_file(), "Fresh managed build artifacts are missing; stale bin/obj are never used.")
    provenance = {
        "schemaVersion": 1, "sdk": sdk, "commands": commands,
        "sources": sources,
        "runtimeArtifactPath": str(runtime), "runtimeArtifactSha256": guard.sha(runtime),
        "weaverArtifactPath": str(weaver), "weaverArtifactSha256": guard.sha(weaver),
    }
    write_json(staging / "managed-build.json", provenance, readonly=True)
    return runtime, weaver, provenance


def managed_sources() -> dict:
    result = {}
    for project in ("MerchantTravel", "VoyageExperience", "Weaver"):
        directory = ROOT / "src" / project
        if not directory.exists():
            continue
        for path in directory.rglob("*"):
            relative = path.relative_to(directory)
            if any(part.lower() in ("bin", "obj", "tests") or guard.ignored(part) for part in relative.parts):
                continue
            guard.no_symlinks(path)
            if path.is_file() and (path.suffix in (".cs", ".csproj") or path.name == "packages.lock.json"):
                result[path.relative_to(ROOT).as_posix()] = guard.sha(path)
    # MerchantTravel.csproj compiles these shared appearance primitives into
    # the single runtime. Old F8 UI, preferences and logging stay excluded.
    painter = ROOT / "src/Painter"
    if painter.exists():
        for path in painter.iterdir():
            if not guard.ignored(path.name) and path.suffix == ".cs":
                guard.no_symlinks(path)
                if path.is_file():
                    result[path.relative_to(ROOT).as_posix()] = guard.sha(path)
    for name in PAINTER_SHARED_SOURCES:
        path = ROOT / "src/CosmeticPack" / name
        if path.exists():
            result[path.relative_to(ROOT).as_posix()] = guard.sha(path)
    return result


def painter_assets() -> dict:
    source = ROOT / "assets/JinkeSkins"
    manifest = guard.tree_manifest(source)
    guard.require(set(manifest) == set(PAINTER_ASSET_FILES), "Painter assets must contain exactly the approved manifest and three flags.")
    return manifest


def copy_painter_assets(app: Path, expected: dict) -> None:
    guard.require(painter_assets() == expected, "Painter assets changed during the build.")
    destination = app / "Contents" / PAINTER_ASSET_REL
    guard.require(not destination.exists(), "Original already contains painter assets.")
    copy_tree(ROOT / "assets/JinkeSkins", destination, expected)
    guard.require(guard.tree_manifest(destination) == expected, "Copied painter assets differ from their source fingerprints.")


def copy_tree(source: Path, destination: Path, baseline: dict) -> None:
    guard.no_symlinks(source)
    guard.no_symlinks(destination)
    destination.mkdir()
    for relative, expected in baseline.items():
        input_path = source / relative
        output_path = destination / relative
        guard.require(guard.sha(input_path) == expected["sha256"], "Original source changed during copy: " + relative)
        guard.no_symlinks(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with input_path.open("rb") as input_stream, output_path.open("xb") as output_stream:
            shutil.copyfileobj(input_stream, output_stream, length=1024 * 1024)
        output_path.chmod(expected["mode"])
        guard.require(guard.sha(output_path) == expected["sha256"], "Copied bytes differ: " + relative)


def patch_identity(app: Path, unity) -> dict:
    managers = app / "Contents/Resources/Data/globalgamemanagers"
    environment = unity.load(str(managers))
    settings = [obj for obj in environment.objects if obj.type.name == "PlayerSettings"]
    guard.require(len(settings) == 1, "Expected exactly one PlayerSettings object.")
    data = settings[0].read_typetree()
    original_settings = copy.deepcopy(data)
    original_objects = {str(obj.path_id): {"type": obj.type.name, "sha256": hashlib.sha256(obj.get_raw_data()).hexdigest()} for obj in environment.objects}
    guard.require(data["companyName"] == "Black Salt Games" and data["productName"] == "DREDGE", "Unexpected original Unity identity.")
    data["companyName"], data["productName"] = guard.COMPANY, guard.PRODUCT
    settings[0].save_typetree(data)
    managers.write_bytes(environment.file.save())
    reread = unity.load(str(managers))
    rewritten_settings = [obj.read_typetree() for obj in reread.objects if obj.type.name == "PlayerSettings"]
    expected_settings = copy.deepcopy(original_settings)
    expected_settings["companyName"], expected_settings["productName"] = guard.COMPANY, guard.PRODUCT
    guard.require(rewritten_settings == [expected_settings], "PlayerSettings changed beyond the two approved identity fields.")
    rewritten_objects = {str(obj.path_id): {"type": obj.type.name, "sha256": hashlib.sha256(obj.get_raw_data()).hexdigest()} for obj in reread.objects}
    settings_id = str(settings[0].path_id)
    guard.require(original_objects.keys() == rewritten_objects.keys(), "Unity identity rewrite changed the serialized object set.")
    for path_id, before in original_objects.items():
        if path_id != settings_id:
            guard.require(before == rewritten_objects[path_id], "Unity identity rewrite changed another object: " + path_id)
    (app / "Contents/Resources/Data/app.info").write_text(guard.COMPANY + "\n" + guard.PRODUCT + "\n", encoding="utf-8")
    info_path = app / "Contents/Info.plist"
    info = plistlib.loads(info_path.read_bytes())
    guard.require(info.get("CFBundleIdentifier") == "com.blacksaltgames.dredge" and info.get("CFBundleExecutable") == "DREDGE", "Unexpected original macOS identity.")
    info["CFBundleIdentifier"] = guard.IDENTIFIER
    names = {VOYAGE_IDENTITY["IDENTIFIER"][1]: "DREDGE Voyage QA", PAINTER_IDENTITY["IDENTIFIER"][1]: "DREDGE Voyage Painter QA"}
    info["CFBundleName"] = names.get(guard.IDENTIFIER, "DREDGE Merchant Travel QA")
    info_path.write_bytes(plistlib.dumps(info))
    return {"playerSettingsPathId": settings_id, "changedPlayerSettingsFields": ["companyName", "productName"], "otherObjectsByteIdentical": len(original_objects) - 1, "objectsBefore": original_objects, "objectsAfter": rewritten_objects}


def validate_copy_delta(app: Path, baseline: dict, profile: str = "merchant-travel", *, asset_manifest: dict | None = None) -> list[dict]:
    require_profile(profile)
    actual = guard.tree_manifest(app / "Contents")
    expected_changes = {
        "Info.plist", "MacOS/DREDGE", "_CodeSignature/CodeResources",
        "Resources/Data/Managed/Assembly-CSharp.dll", "Resources/Data/Managed/Jinke.DredgeMerchantTravel.dll",
        "Resources/Data/globalgamemanagers", "Resources/Data/app.info", "Resources/steam_appid.txt",
        "Resources/merchant-travel-proof.json", "Resources/merchant-travel-files.json", "Resources/merchant_travel_guard.py",
    }
    if profile == "voyage-painter":
        guard.require(isinstance(asset_manifest, dict) and set(asset_manifest) == set(PAINTER_ASSET_FILES), "Painter copy audit requires the exact source asset fingerprints.")
        expected_assets = {PAINTER_ASSET_REL + "/" + relative: record for relative, record in asset_manifest.items()}
        actual_assets = {relative: record for relative, record in actual.items() if relative.startswith(PAINTER_ASSET_REL + "/")}
        guard.require(actual_assets == expected_assets, "Painter asset content or file set differs from the source fingerprints.")
        guard.require(not any(relative.startswith(PAINTER_ASSET_REL + "/") for relative in baseline), "Original already contains painter assets.")
        expected_changes.update(expected_assets)
    changes = []
    for relative in sorted(baseline.keys() | actual.keys()):
        before, after = baseline.get(relative), actual.get(relative)
        if before != after:
            guard.require(relative in expected_changes and after is not None, "Unapproved app copy change: " + relative)
            changes.append({"path": relative, "before": before, "after": after})
    return changes


def validate_weaver_audit(path: Path, original: Path, patched: Path, runtime: Path, profile: str = "merchant-travel") -> dict:
    expected_methods = require_profile(profile)
    report = guard.read_json(path)
    guard.require(report.get("sourceSha256") == guard.sha(original) and report.get("outputSha256") == guard.sha(patched) and report.get("runtimeSha256") == guard.sha(runtime), "Weaver audit fingerprints do not match current artifacts.")
    # The Weaver owns semantic IL verification. Do not infer the hook contract
    # from a generic success status: require the exact merchant method set.
    detail = report.get("audit", {})
    guard.require(report.get("profile") == profile and detail.get("passed") is True, "Weaver did not pass the " + profile + " semantic audit.")
    guard.require(report.get("sourceMvid") == ORIGINAL_MVID, "Weaver audited an unexpected source module.")
    references = detail.get("addedAssemblyReferences", [])
    guard.require(len(references) == 1 and references[0].split(",", 1)[0] == "Jinke.DredgeMerchantTravel", "Weaver added unexpected assembly references.")
    expected_headers = VOYAGE_MAX_STACK_CHANGES if profile in ("voyage-experience", "voyage-painter") else []
    guard.require(detail.get("maxStackHeaderChanges") == expected_headers, "Weaver changed unrelated IL stack headers.")
    changed = detail.get("changedMethods", [])
    if isinstance(changed, list):
        guard.require(all(isinstance(method, dict) for method in changed), "Weaver omitted method body verification details.")
        names = {method.get("method") for method in changed}
        guard.require(len(changed) == len(expected_methods) and names == expected_methods, "Weaver audit does not contain exactly the approved " + profile + " hooks.")
        for method in changed:
            original_hash = method.get("originalBodySha256")
            guard.require(isinstance(original_hash, str) and len(original_hash) == 64 and method.get("restoredBodySha256") == original_hash, "Weaver could not restore an original method body.")
    else:
        raise guard.Refusal("Weaver audit must enumerate changed methods, not just a count.")
    return report


def write_proof(app: Path, *, source_manifest_sha256: str, weaver_audit_sha256: str, role: str = "production-only") -> dict:
    guard.no_symlinks(app)
    guard.require(guard.is_child(app, guard.no_symlinks(ROOT / "output")), "App must be in output/.")
    proof = {
        "schemaVersion": 1, "mode": "merchant-travel-isolated", "validationRole": role,
        "bundleIdentifier": guard.IDENTIFIER, "company": guard.COMPANY, "product": guard.PRODUCT,
        "appPath": str(app), "persistentDataPath": str(guard.profile_root()),
        "allowedOutputRoot": str(ROOT / "output"), "originalContentsRoot": str(ROOT / "Contents"),
        "originalAssemblySha256": guard.ORIGINAL_SHA, "steamAppId": int(guard.APP_ID),
        "sourceManifestSha256": source_manifest_sha256, "weaverAuditSha256": weaver_audit_sha256,
        "launchRequired": True,
        "cloudScope": "Distinct Unity profile; no Cloud setting changes. Steam Auto-Cloud effects need separate native observation.",
    }
    for key, relative in guard.CORE_FILES.items():
        proof[key] = guard.sha(app / relative)
    write_json(app / guard.PROOF_REL, proof)
    write_json(app / guard.MANIFEST_REL, {
        "schemaVersion": 1,
        "signatureVerifiedSeparately": ["Contents/MacOS/DREDGE", "Contents/_CodeSignature/"],
        "selfExcluded": guard.MANIFEST_REL,
        "ignoredMetadata": ["._*", ".DS_Store"],
        "files": guard.tree_manifest(app, app=True),
    })
    return proof


def sign_app(app: Path) -> None:
    remove_generated_metadata(app)
    run("codesign", "--force", "--sign", "-", "--preserve-metadata=entitlements", app)
    run("codesign", "--verify", "--deep", "--strict", app)


def remove_generated_metadata(app: Path) -> int:
    """External-drive AppleDouble files are metadata, not copied game content.

    macOS creates them again while writing the new copy. codesign otherwise
    mistakes entries in code-only folders for unsigned executable components.
    This operation is explicitly limited to our new app below output/.
    """
    app = guard.no_symlinks(app)
    guard.require(app.suffix == ".app" and guard.is_child(app, guard.no_symlinks(ROOT / "output")), "Metadata cleanup is limited to an isolated output .app.")
    removed = 0
    for path in app.rglob("*"):
        if guard.ignored(path.name):
            guard.regular_file(path)
            path.unlink()
            removed += 1
    return removed


def refresh_qa_metadata(app: Path, *, weaver_audit_sha256: str | None = None) -> dict:
    """Reseal one explicit instrumented copy, never the delivered app.

    Parent QA tooling may alter an independently copied app, then call this.
    Existing metadata is replaced only under output/runtime/ after validating
    its fixed identity and source fingerprint. No game or save is opened.
    """
    app = guard.no_symlinks(app)
    guard.require(app.suffix == ".app" and guard.is_child(app, guard.no_symlinks(ROOT / "output/runtime")), "QA reseal is limited to .app copies under output/runtime/.")
    list(guard.files_below(app))
    old = guard.read_json(app / guard.PROOF_REL)
    guard.require(old.get("bundleIdentifier") == guard.IDENTIFIER and old.get("originalAssemblySha256") == guard.ORIGINAL_SHA, "QA source proof is not a merchant-travel build.")
    info = plistlib.loads((app / "Contents/Info.plist").read_bytes())
    guard.require(info.get("CFBundleIdentifier") == guard.IDENTIFIER, "QA copy changed the isolated identity.")
    audit_hash = weaver_audit_sha256 or old["weaverAuditSha256"]
    guard.require(isinstance(audit_hash, str) and len(audit_hash) == 64 and all(character in "0123456789abcdef" for character in audit_hash), "Invalid QA Weaver audit fingerprint.")
    for relative in (guard.PROOF_REL, guard.MANIFEST_REL):
        guard.regular_file(app / relative)
        (app / relative).unlink()
    proof = write_proof(app, source_manifest_sha256=old["sourceManifestSha256"], weaver_audit_sha256=audit_hash, role="qa-instrumented")
    sign_app(app)
    # sign_app has just performed strict/deep verification; there were no
    # intervening writes. Avoid reading the entire signed game a second time.
    guard.verify_app(app, signature=False)
    return proof


def make_launcher(app: Path, launcher: Path) -> None:
    python = Path(sys.executable).resolve()
    guard.regular_file(python)
    helper_hash = guard.sha(app / guard.GUARD_REL)
    script = "\n".join([
        "#!/bin/zsh", "set -eu", "launch_dir=\"${0:A:h}\"",
        "app=\"$launch_dir\"/" + shlex.quote(app.name),
        "helper=\"$app/" + guard.GUARD_REL + "\"",
        "if [[ ! -f \"$helper\" || -L \"$helper\" ]]; then print -u2 'REFUSED: Missing or linked launcher helper.'; exit 1; fi",
        "actual=$(/usr/bin/shasum -a 256 \"$helper\")",
        "if [[ \"${actual%% *}\" != " + shlex.quote(helper_hash) + " ]]; then print -u2 'REFUSED: Launcher helper changed.'; exit 1; fi",
        "# The hash-pinned helper verifies the complete app manifest and signature.",
        "exec " + shlex.quote(str(python)) + " \"$helper\" --app \"$app\"",
        "",
    ])
    with launcher.open("x", encoding="utf-8") as stream:
        stream.write(script)
    launcher.chmod(0o755)


def build(name: str, profile: str = "merchant-travel") -> dict:
    with profile_context(profile):
        return _build(name, profile)


def _build(name: str, profile: str) -> dict:
    require_profile(profile)
    (app, staging, launcher), unity, baseline = preflight(name, profile)
    staging.mkdir(parents=True)
    source_report = {"schemaVersion": 1, "source": str(ROOT / "Contents"), "ignoredMetadata": ["._*", ".DS_Store"], "files": baseline}
    source_manifest = staging / "original-contents-before.json"
    write_json(source_manifest, source_report, readonly=True)
    try:
        assets = painter_assets() if profile == "voyage-painter" else None
        if assets is not None:
            write_json(staging / "painter-asset-sources.json", {"schemaVersion": 1, "source": str(ROOT / "assets/JinkeSkins"), "files": assets}, readonly=True)
        runtime, weaver, managed = build_managed(staging)
        original = ROOT / "Contents/Resources/Data/Managed/Assembly-CSharp.dll"
        patched = staging / "Assembly-CSharp.dll"
        run("dotnet", weaver, "--" + profile, original, patched, runtime)
        audit_path = Path(str(patched) + ".audit.json")
        validate_weaver_audit(audit_path, original, patched, runtime, profile)
        app.mkdir()
        copy_tree(ROOT / "Contents", app / "Contents", baseline)
        managed_dir = app / "Contents/Resources/Data/Managed"
        shutil.copyfile(patched, managed_dir / "Assembly-CSharp.dll")
        runtime_target = managed_dir / "Jinke.DredgeMerchantTravel.dll"
        guard.require(not runtime_target.exists(), "Original already contains merchant runtime.")
        shutil.copyfile(runtime, runtime_target)
        if assets is not None:
            copy_painter_assets(app, assets)
        identity_audit = patch_identity(app, unity)
        write_json(staging / "unity-identity-audit.json", identity_audit, readonly=True)
        for directory in ("Contents/Resources",):
            marker = app / directory / "steam_appid.txt"
            guard.require(not marker.exists(), "Unexpected existing Steam marker in original package.")
            marker.write_text(guard.APP_ID + "\n", encoding="ascii")
        (app / guard.GUARD_REL).write_text(guard_source(profile), encoding="utf-8")
        write_proof(app, source_manifest_sha256=guard.sha(source_manifest), weaver_audit_sha256=guard.sha(audit_path))
        sign_app(app)
        guard.verify_app(app, signature=False)
        copy_changes = validate_copy_delta(app, baseline, profile, asset_manifest=assets)
        sealed = staging / "sealed-app-files.json"
        write_json(sealed, {"schemaVersion": 1, "app": str(app), "files": guard.tree_manifest(app)}, readonly=True)
        # Do not publish a launcher until all source, copy, IL and signature
        # checks have succeeded. A partial failed .app remains fail-closed.
        guard.require(guard.tree_manifest(ROOT / "Contents") == baseline, "Original Contents changed during the build.")
        if assets is not None:
            guard.require(painter_assets() == assets, "Painter assets changed before the build completed.")
        make_launcher(app, launcher)
        report = {
            "schemaVersion": 1, "status": "built-and-statically-verified", "builtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "patchProfile": profile,
            "app": str(app), "launcher": str(launcher), "profile": str(guard.profile_root()),
            "originalContentsManifest": str(source_manifest), "originalContentsManifestSha256": guard.sha(source_manifest),
            "sealedAppManifest": str(sealed), "sealedAppManifestSha256": guard.sha(sealed),
            "managedBuild": managed, "weaverAudit": str(audit_path), "weaverAuditSha256": guard.sha(audit_path),
            "painterAssetSources": assets,
            "unityIdentityAudit": str(staging / "unity-identity-audit.json"), "copyChanges": copy_changes,
            "launcherSha256": guard.sha(launcher), "gameLaunched": False, "saveFilesReadOrWritten": False,
            "nativeValidation": "Not performed by the builder. App identity, real save strategy, gameplay and Steam Auto-Cloud require a separate native run.",
        }
        write_json(staging / "build-report.json", report, readonly=True)
        return report
    finally:
        after = guard.tree_manifest(ROOT / "Contents")
        unchanged = after == baseline
        write_json(staging / "original-contents-after.json", {**source_report, "files": after, "unchanged": unchanged}, readonly=True)
        guard.require(unchanged, "Original Contents changed; build cannot be accepted.")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", default=DEFAULT_NAME)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(build(args.name), ensure_ascii=False, indent=2))
        return 0
    except (guard.Refusal, OSError, ValueError, KeyError, ImportError, subprocess.CalledProcessError) as error:
        print("REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
