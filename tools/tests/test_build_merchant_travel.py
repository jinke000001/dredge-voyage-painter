#!/usr/bin/env python3
"""Isolation tests only. Never execute the Unity player or open a real save.

Run: python3 -m unittest discover -s tools/tests -p test_build_merchant_travel.py -v
Optional real fresh-compilation tests: JINKE_MERCHANT_BUILD_INTEGRATION=1 ...
"""
from __future__ import annotations

import json
import copy
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

WORKSPACE = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(WORKSPACE / "tools"))
import build_merchant_travel as builder
import build_voyage_experience as voyage_builder
import build_painter as painter_builder
import merchant_travel_guard as guard


class Fixture(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="merchant-builder-", dir=str(Path(tempfile.gettempdir()).resolve()))
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.profile = self.home / "Library/Application Support" / guard.IDENTIFIER
        self.app = self.root / "output/Test Merchant.app"
        self.patchers = [patch.object(builder, "ROOT", self.root), patch.object(guard, "profile_root", return_value=self.profile)]
        for patcher in self.patchers:
            patcher.start()

    def tearDown(self):
        for patcher in reversed(self.patchers):
            patcher.stop()
        self.temporary.cleanup()

    def fake_app(self, *, painter_assets=False):
        for relative in guard.CORE_FILES.values():
            path = self.app / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((relative + " fixture").encode())
        executable = self.app / "Contents/MacOS/DREDGE"
        executable.parent.mkdir(parents=True, exist_ok=True)
        executable.write_bytes(b"Not an executable. Never run.")
        (self.app / "Contents/Info.plist").write_bytes(plistlib.dumps({"CFBundleIdentifier": guard.IDENTIFIER, "CFBundleExecutable": "DREDGE"}))
        (self.app / "Contents/Resources/Data/app.info").write_text(guard.COMPANY + "\n" + guard.PRODUCT + "\n")
        (self.app / "Contents/Resources/payload.bin").write_bytes(b"PAYLOAD1")
        (self.app / "Contents/Resources/unsealed.bin").write_bytes(b"FALLBACK")
        for directory in ("Contents/Resources",):
            (self.app / directory / "steam_appid.txt").write_text(guard.APP_ID + "\n")
        if painter_assets:
            for relative in builder.PAINTER_ASSET_FILES:
                path = self.app / "Contents" / builder.PAINTER_ASSET_REL / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes((relative + " painter fixture").encode())
        builder.write_proof(self.app, source_manifest_sha256="a" * 64, weaver_audit_sha256="b" * 64)
        manifest = json.loads((self.app / guard.MANIFEST_REL).read_text())["files"]
        sealed = {relative.removeprefix("Contents/"): {"hash2": bytes.fromhex(value["sha256"])} for relative, value in manifest.items() if not relative.endswith("/unsealed.bin")}
        envelope = self.app / "Contents/_CodeSignature/CodeResources"
        envelope.parent.mkdir()
        envelope.write_bytes(plistlib.dumps({"files2": sealed}))


class IsolatedFixture(Fixture):
    def test_valid_static_fixture_never_opens_profile_or_launches(self):
        self.fake_app()
        with patch.object(guard.subprocess, "run") as run:
            proof = guard.verify_app(self.app)
        self.assertEqual(proof["persistentDataPath"], str(self.profile))
        self.assertFalse(self.profile.exists())
        run.assert_called_once_with(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(self.app)], check=True)

    def test_wrong_identity_rejected_before_signature_or_game(self):
        self.fake_app()
        proof = self.app / guard.PROOF_REL
        value = json.loads(proof.read_text())
        value["bundleIdentifier"] = "com.blacksaltgames.dredge"
        proof.write_text(json.dumps(value))
        with patch.object(guard.subprocess, "run") as run, self.assertRaisesRegex(guard.Refusal, "bundleIdentifier"):
            guard.verify_app(self.app)
        run.assert_not_called()

    def test_formal_profile_cannot_be_selected(self):
        self.fake_app()
        proof = self.app / guard.PROOF_REL
        value = json.loads(proof.read_text())
        value["persistentDataPath"] = str(self.home / "Library/Application Support/Black Salt Games/DREDGE")
        proof.write_text(json.dumps(value))
        with self.assertRaisesRegex(guard.Refusal, "persistentDataPath"):
            guard.verify_app(self.app, signature=False)

    def test_each_core_file_tamper_is_refused(self):
        self.fake_app()
        for relative in guard.CORE_FILES.values():
            with self.subTest(relative=relative):
                path = self.app / relative
                before = path.read_bytes()
                path.write_bytes(before + b"modified")
                with self.assertRaisesRegex(guard.Refusal, "fingerprint mismatch"):
                    guard.verify_app(self.app, signature=False)
                path.write_bytes(before)

    def test_unlisted_payload_is_refused(self):
        self.fake_app()
        (self.app / "Contents/Resources/unlisted.dll").write_bytes(b"unexpected")
        with self.assertRaisesRegex(guard.Refusal, "manifest differs"):
            guard.verify_app(self.app, signature=False)

    def test_invalid_signature_blocks_even_with_valid_resource_hashes(self):
        self.fake_app()
        with patch.object(guard.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "codesign")), self.assertRaises(subprocess.CalledProcessError):
            guard.verify_app(self.app)

    def test_launch_skips_only_resource_sha_covered_by_matching_seal(self):
        self.fake_app()
        hashes = []
        actual_sha = guard.sha
        def tracked(path):
            hashes.append(Path(path).relative_to(self.app).as_posix())
            return actual_sha(path)
        with patch.object(guard, "sha", side_effect=tracked), patch.object(guard.subprocess, "run") as run:
            guard.verify_app(self.app)
        self.assertNotIn("Contents/Resources/payload.bin", hashes)
        self.assertIn("Contents/Resources/unsealed.bin", hashes)
        self.assertTrue(set(guard.CORE_FILES.values()).issubset(hashes))
        run.assert_called_once_with(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(self.app)], check=True)

    def test_matching_signature_requirement_does_not_replace_exact_nested_hash(self):
        self.fake_app()
        envelope = self.app / "Contents/_CodeSignature/CodeResources"
        seal = plistlib.loads(envelope.read_bytes())
        seal["files2"]["Resources/payload.bin"] = {"cdhash": b"x" * 20, "requirement": "same designated requirement"}
        envelope.write_bytes(plistlib.dumps(seal))
        (self.app / "Contents/Resources/payload.bin").write_bytes(b"CHANGED1")
        with patch.object(guard.subprocess, "run") as run, self.assertRaisesRegex(guard.Refusal, "nested-code"):
            guard.verify_app(self.app)
        run.assert_not_called()

    def test_unsealed_same_size_modification_is_still_hashed(self):
        self.fake_app()
        (self.app / "Contents/Resources/unsealed.bin").write_bytes(b"CHANGED2")
        with patch.object(guard.subprocess, "run") as run, self.assertRaisesRegex(guard.Refusal, "Unsealed"):
            guard.verify_app(self.app)
        run.assert_not_called()

    def test_new_resource_seal_must_match_original_manifest(self):
        self.fake_app()
        envelope = self.app / "Contents/_CodeSignature/CodeResources"
        seal = plistlib.loads(envelope.read_bytes())
        seal["files2"]["Resources/payload.bin"]["hash2"] = b"x" * 32
        envelope.write_bytes(plistlib.dumps(seal))
        with self.assertRaisesRegex(guard.Refusal, "Signed resource fingerprint"):
            guard.verify_app(self.app)

    def test_fast_path_cannot_omit_codesign(self):
        with self.assertRaisesRegex(guard.Refusal, "Cannot omit both"):
            guard.verify_app(self.app, signature=False, full_hash=False)

    def test_fast_path_preserves_inventory_sizes_modes_and_extra_file_checks(self):
        self.fake_app()
        target = self.app / "Contents/Resources/payload.bin"
        original_mode = target.stat().st_mode & 0o777
        target.chmod(0o600 if original_mode != 0o600 else 0o644)
        with self.assertRaisesRegex(guard.Refusal, "inventory, size or mode"):
            guard.verify_app(self.app)
        target.chmod(original_mode)
        target.write_bytes(b"new larger size")
        with self.assertRaisesRegex(guard.Refusal, "inventory, size or mode"):
            guard.verify_app(self.app)
        target.write_bytes(b"PAYLOAD1")
        (self.app / "Contents/Resources/extra.bin").write_bytes(b"unexpected")
        with self.assertRaisesRegex(guard.Refusal, "inventory, size or mode"):
            guard.verify_app(self.app)

    def test_check_only_retains_complete_sha_audit(self):
        with patch.object(guard, "verify_app", return_value={"persistentDataPath": str(self.profile)}) as verify, patch("builtins.print"):
            self.assertEqual(guard.main(["--app", str(self.app), "--check-only"]), 0)
        verify.assert_called_once_with(self.app, full_hash=True)

    @unittest.skipUnless(sys.platform == "darwin" and shutil.which("codesign"), "Requires local macOS codesign, never runs game code.")
    def test_real_codesign_rejects_same_size_resource_tamper_and_manifest_reseal(self):
        self.fake_app()
        # Only sign a tiny fixture copy of a system executable. Never execute it.
        shutil.copyfile("/bin/echo", self.app / "Contents/MacOS/DREDGE")
        builder.sign_app(self.app)
        guard.verify_app(self.app)
        (self.app / "Contents/Resources/payload.bin").write_bytes(b"CHANGED1")
        with self.assertRaises(subprocess.CalledProcessError):
            guard.verify_app(self.app)
        builder.sign_app(self.app)
        with self.assertRaisesRegex(guard.Refusal, "Signed resource fingerprint"):
            guard.verify_app(self.app)

    def test_linked_app_and_linked_profile_are_rejected(self):
        self.fake_app()
        link = self.root / "output/Alias.app"
        link.symlink_to(self.app, target_is_directory=True)
        with self.assertRaisesRegex(guard.Refusal, "symlink"):
            guard.verify_app(link, signature=False)
        self.profile.parent.mkdir(parents=True)
        official_like = self.root / "formal-placeholder"
        official_like.mkdir()
        self.profile.symlink_to(official_like, target_is_directory=True)
        with self.assertRaisesRegex(guard.Refusal, "symlink"):
            guard.verify_app(self.app, signature=False)

    def test_linked_save_subdirectory_is_rejected_without_opening_contents(self):
        self.fake_app()
        self.profile.mkdir(parents=True)
        target = self.root / "do-not-read"
        target.mkdir()
        (target / "dredge-save0.bin").write_bytes(b"fixture only")
        (self.profile / "saves").symlink_to(target, target_is_directory=True)
        with self.assertRaisesRegex(guard.Refusal, "symlink"):
            guard.verify_app(self.app, signature=False)

    def test_hardlinked_profile_file_is_rejected(self):
        self.fake_app()
        self.profile.mkdir(parents=True)
        target = self.root / "fixture.bin"
        target.write_bytes(b"fixture")
        os.link(target, self.profile / "dredge-settings.bin")
        with self.assertRaisesRegex(guard.Refusal, "hard link"):
            guard.verify_app(self.app, signature=False)

    def test_no_existing_output_is_reused(self):
        for kind in ("app", "staging", "launcher"):
            with self.subTest(kind=kind):
                app, stage, launch = builder.paths_for("New.app")
                selected = {"app": app, "staging": stage, "launcher": launch}[kind]
                selected.parent.mkdir(parents=True, exist_ok=True)
                selected.write_bytes(b"existing")
                with self.assertRaisesRegex(guard.Refusal, "overwrite"):
                    builder.paths_for("New.app")
                selected.unlink()

    def test_linked_output_parent_cannot_target_original(self):
        source = self.root / "Contents"
        source.mkdir()
        (self.root / "output").symlink_to(source, target_is_directory=True)
        with self.assertRaisesRegex(guard.Refusal, "symlink"):
            builder.paths_for("New.app")
        self.assertEqual(list(source.iterdir()), [])

    def test_unsafe_app_names_are_rejected(self):
        for name in ("../Contents.app", "/tmp/X.app", "a/b.app", ".app", "a\n.app", "no-suffix"):
            with self.subTest(name=name), self.assertRaises(guard.Refusal):
                builder.paths_for(name)

    def test_original_hash_mismatch_stops_before_dependency_or_build(self):
        original = self.root / "Contents/Resources/Data/Managed/Assembly-CSharp.dll"
        original.parent.mkdir(parents=True)
        original.write_bytes(b"wrong original")
        with patch.object(builder, "build_managed") as managed, self.assertRaisesRegex(guard.Refusal, "fingerprint"):
            builder.build("New.app")
        managed.assert_not_called()
        self.assertFalse((self.root / "output").exists())

    def test_source_copy_is_new_bytes_and_detects_mid_copy_change(self):
        source = self.root / "Contents"
        source.mkdir()
        (source / "data").write_bytes(b"original")
        baseline = guard.tree_manifest(source)
        copied = self.root / "copy"
        builder.copy_tree(source, copied, baseline)
        self.assertEqual((copied / "data").read_bytes(), b"original")
        self.assertNotEqual((copied / "data").stat().st_ino, (source / "data").stat().st_ino)
        (source / "data").write_bytes(b"changed")
        with self.assertRaisesRegex(guard.Refusal, "source changed"):
            builder.copy_tree(source, self.root / "copy2", baseline)

    def test_missing_fresh_artifacts_cannot_fall_back_to_stale_bin(self):
        stage = self.root / "stage"
        stage.mkdir()
        stale = self.root / "src/MerchantTravel/bin/Release/net472/Jinke.DredgeMerchantTravel.dll"
        stale.parent.mkdir(parents=True)
        stale.write_bytes(b"stale")
        with patch.object(builder, "run"), patch.object(builder.subprocess, "check_output", return_value="10.0.test"), self.assertRaisesRegex(guard.Refusal, "Fresh managed"):
            builder.build_managed(stage)
        self.assertEqual(stale.read_bytes(), b"stale")

    def test_failed_build_preserves_original_manifest_and_publishes_no_launcher(self):
        source = self.root / "Contents"
        source.mkdir()
        (source / "data").write_bytes(b"original")
        baseline = guard.tree_manifest(source)
        paths = builder.paths_for("Fail.app")
        with patch.object(builder, "preflight", return_value=(paths, None, baseline)), patch.object(builder, "build_managed", side_effect=guard.Refusal("intentional")), self.assertRaisesRegex(guard.Refusal, "intentional"):
            builder.build("Fail.app")
        app, stage, launcher = paths
        self.assertFalse(app.exists())
        self.assertFalse(launcher.exists())
        self.assertTrue(json.loads((stage / "original-contents-after.json").read_text())["unchanged"])
        with self.assertRaises(FileExistsError):
            builder.write_json(stage / "original-contents-before.json", {"overwrite": True})

    def test_extra_scene_change_is_rejected_by_copy_allowlist(self):
        self.fake_app()
        contents = self.app / "Contents"
        baseline = guard.tree_manifest(contents)
        scene = contents / "Resources/Data/level0"
        scene.write_bytes(b"unapproved scene")
        with self.assertRaisesRegex(guard.Refusal, "Unapproved app copy change"):
            builder.validate_copy_delta(self.app, baseline)

    def test_unsafe_semantic_audits_are_refused(self):
        artifacts = self.root / "artifacts"
        artifacts.mkdir()
        original, patched, runtime = (artifacts / name for name in ("original", "patched", "runtime"))
        for path in (original, patched, runtime):
            path.write_bytes(path.name.encode())
        valid = {
            "profile": "merchant-travel", "sourceMvid": builder.ORIGINAL_MVID,
            "sourceSha256": guard.sha(original), "outputSha256": guard.sha(patched), "runtimeSha256": guard.sha(runtime),
            "audit": {"passed": True, "addedAssemblyReferences": ["Jinke.DredgeMerchantTravel, Version=1.0.0.0"], "maxStackHeaderChanges": [],
                "changedMethods": [{"method": method, "originalBodySha256": "a" * 64, "restoredBodySha256": "a" * 64} for method in builder.EXPECTED_METHODS]},
        }
        report = artifacts / "audit.json"
        report.write_text(json.dumps(valid))
        builder.validate_weaver_audit(report, original, patched, runtime)
        cases = []
        value = copy.deepcopy(valid); value["audit"]["passed"] = False; cases.append(value)
        value = copy.deepcopy(valid); value["profile"] = "skins"; cases.append(value)
        value = copy.deepcopy(valid); value["sourceMvid"] = "wrong"; cases.append(value)
        value = copy.deepcopy(valid); value["audit"]["addedAssemblyReferences"].append("Extra"); cases.append(value)
        value = copy.deepcopy(valid); value["audit"]["changedMethods"][0]["method"] += "Fake"; cases.append(value)
        value = copy.deepcopy(valid); value["audit"]["changedMethods"][0]["restoredBodySha256"] = "b" * 64; cases.append(value)
        # New builds must have the radial hook. Already sealed historical apps
        # are checked by guard.verify_app against their own proof, not this build gate.
        value = copy.deepcopy(valid); value["audit"]["changedMethods"] = [method for method in value["audit"]["changedMethods"] if "AbilityBarUI" not in method["method"]]; cases.append(value)
        for index, value in enumerate(cases):
            report.write_text(json.dumps(value))
            with self.subTest(index=index), self.assertRaises(guard.Refusal):
                builder.validate_weaver_audit(report, original, patched, runtime)

    def test_voyage_audit_requires_all_new_hooks_and_the_original_merchant_hooks(self):
        artifacts = self.root / "artifacts"
        artifacts.mkdir()
        original, patched, runtime = (artifacts / name for name in ("original", "patched", "runtime"))
        for path in (original, patched, runtime):
            path.write_bytes(path.name.encode())
        value = {
            "profile": "voyage-experience", "sourceMvid": builder.ORIGINAL_MVID,
            "sourceSha256": guard.sha(original), "outputSha256": guard.sha(patched), "runtimeSha256": guard.sha(runtime),
            "audit": {"passed": True, "addedAssemblyReferences": ["Jinke.DredgeMerchantTravel, Version=1.0.0.0"], "maxStackHeaderChanges": builder.VOYAGE_MAX_STACK_CHANGES,
                "changedMethods": [{"method": method, "originalBodySha256": "a" * 64, "restoredBodySha256": "a" * 64} for method in sorted(builder.VOYAGE_METHODS)]},
        }
        report = artifacts / "audit.json"
        report.write_text(json.dumps(value))
        builder.validate_weaver_audit(report, original, patched, runtime, "voyage-experience")
        with self.assertRaisesRegex(guard.Refusal, "semantic audit"):
            builder.validate_weaver_audit(report, original, patched, runtime)
        for headers in ([], builder.VOYAGE_MAX_STACK_CHANGES + [{"method": "unrelated", "original": 1, "patched": 2}]):
            unsafe = copy.deepcopy(value)
            unsafe["audit"]["maxStackHeaderChanges"] = headers
            report.write_text(json.dumps(unsafe))
            with self.assertRaisesRegex(guard.Refusal, "stack headers"):
                builder.validate_weaver_audit(report, original, patched, runtime, "voyage-experience")
        for missing in builder.VOYAGE_METHODS:
            incomplete = copy.deepcopy(value)
            incomplete["audit"]["changedMethods"] = [entry for entry in value["audit"]["changedMethods"] if entry["method"] != missing]
            report.write_text(json.dumps(incomplete))
            with self.subTest(missing=missing), self.assertRaisesRegex(guard.Refusal, "approved voyage-experience hooks"):
                builder.validate_weaver_audit(report, original, patched, runtime, "voyage-experience")

    def test_voyage_builder_has_fixed_profile_and_a_new_default_destination(self):
        with patch.object(voyage_builder.builder, "build", return_value={}) as build, patch("builtins.print"):
            self.assertEqual(voyage_builder.main([]), 0)
        build.assert_called_once_with("DREDGE 航行体验版 2026-10-19.app", "voyage-experience")
        app, stage, launcher = builder.paths_for("New Voyage.app", "voyage-experience")
        self.assertEqual(stage.name, "New Voyage-voyage-experience")
        self.assertTrue(builder.EXPECTED_METHODS < builder.VOYAGE_METHODS)
        with self.assertRaisesRegex(guard.Refusal, "Unknown fixed build profile"):
            builder.build("New.app", "anything")
        self.assertFalse(app.exists())
        self.assertFalse(launcher.exists())

    def test_source_fingerprints_include_voyage_but_not_old_bin_or_tests(self):
        voyage = self.root / "src/VoyageExperience"
        voyage.mkdir(parents=True)
        source = voyage / "Runtime.cs"
        source.write_text("// first version")
        for folder in ("bin", "obj", "tests"):
            (voyage / folder).mkdir()
            (voyage / folder / "Ignored.cs").write_text("// not a runtime source")
        before = builder.managed_sources()
        self.assertEqual(set(before), {"src/VoyageExperience/Runtime.cs"})
        source.write_text("// changed version")
        self.assertNotEqual(builder.managed_sources(), before)

    def test_ship_appearance_sources_are_fingerprinted_but_test_suites_are_excluded(self):
        directory = self.root / "src/ShipAppearance"
        directory.mkdir(parents=True)
        source = directory / "Boat5MeshFactory.cs"
        source.write_text("// first geometry")
        for name in ("Tests", "GeometryTests", "bin", "obj"):
            (directory / name).mkdir()
            (directory / name / "Ignored.cs").write_text("// not compiled into the runtime")
        before = builder.managed_sources()
        self.assertEqual(set(before), {"src/ShipAppearance/Boat5MeshFactory.cs"})
        source.write_text("// changed geometry")
        self.assertNotEqual(builder.managed_sources(), before)

    def test_voyage_guard_has_a_distinct_fixed_profile_and_preserves_merchant_guard(self):
        original_identifier = guard.IDENTIFIER
        selected = builder.guard_for_profile("voyage-experience")
        self.assertEqual(selected.IDENTIFIER, "local.jinke.dredge.voyage.qa.20261019")
        self.assertEqual(selected.COMPANY, "Jinke Local Tests")
        self.assertEqual(selected.PRODUCT, "DREDGE Voyage QA 20261019")
        self.assertIs(selected.Refusal, guard.Refusal)
        with patch.object(selected, "user_home", return_value=self.home):
            voyage_profile = self.home / "Library/Application Support/local.jinke.dredge.voyage.qa.20261019"
            self.assertEqual(selected.check_profile(voyage_profile), voyage_profile)
            for rejected in (self.profile, self.home / "Library/Application Support/Black Salt Games/DREDGE",
                             self.home / "Library/Application Support/local.jinke.dredge.voyage.qa.20261018"):
                with self.assertRaisesRegex(guard.Refusal, "fixed isolated"):
                    selected.check_profile(rejected)
        self.assertFalse(self.profile.exists())
        with builder.profile_context("voyage-experience") as context:
            self.assertIs(builder.guard, context)
            self.assertEqual(context.IDENTIFIER, selected.IDENTIFIER)
        self.assertIs(builder.guard, guard)
        self.assertEqual(guard.IDENTIFIER, original_identifier)
        with self.assertRaisesRegex(guard.Refusal, "Unknown fixed"):
            builder.guard_for_profile("custom-path")

    def test_generated_voyage_guard_only_changes_fixed_identity_constants(self):
        source = builder.guard_source("merchant-travel")
        generated = builder.guard_source("voyage-experience")
        restored = generated
        for name, (original, replacement) in builder.VOYAGE_IDENTITY.items():
            self.assertEqual(generated.count(name + " = " + json.dumps(replacement) + "\n"), 1)
            restored = restored.replace(name + " = " + json.dumps(replacement) + "\n", name + " = " + json.dumps(original) + "\n", 1)
        self.assertEqual(restored, source)

    def test_voyage_includes_both_native_durability_panels(self):
        self.assertEqual(len(builder.VOYAGE_METHODS), 14)
        for method in ("System.Void OccasionalGridPanel::RefreshDurabilityUI()", "System.Void NetPanel::RefreshUI()"):
            self.assertIn(method, builder.VOYAGE_METHODS)
            self.assertNotIn(method, builder.EXPECTED_METHODS)
            self.assertIn({"method": method, "original": 5, "patched": 6}, builder.VOYAGE_MAX_STACK_CHANGES)

    def test_painter_builder_keeps_voyage_and_adds_only_flag_and_dialogue_lifecycle_hooks(self):
        with patch.object(painter_builder.builder, "build", return_value={}) as build, patch("builtins.print"):
            self.assertEqual(painter_builder.main([]), 0)
        build.assert_called_once_with("DREDGE 油漆匠整合版.app", "voyage-painter")
        self.assertEqual(len(builder.EXPECTED_METHODS), 4)
        self.assertEqual(len(builder.VOYAGE_METHODS), 14)
        self.assertEqual(builder.PAINTER_METHODS - builder.VOYAGE_METHODS, {
            "System.Void PlayerColorCustomizer::RefreshBoatFlag()", "System.Void DredgeDialogueRunner::Awake()",
            "System.Void DredgeDialogueRunner::StartDialogue(System.String)",
        })
        app, stage, launcher = builder.paths_for("Painter.app", "voyage-painter")
        self.assertEqual(stage.name, "Painter-voyage-painter")
        stage.mkdir(parents=True)
        (stage / "existing.dll").write_bytes(b"previous output")
        with self.assertRaisesRegex(guard.Refusal, "overwrite"):
            builder.paths_for("Painter.app", "voyage-painter")
        self.assertFalse(app.exists())
        self.assertFalse(launcher.exists())

    def test_painter_guard_changes_only_fixed_identity_and_rejects_other_profiles(self):
        selected = builder.guard_for_profile("voyage-painter")
        self.assertEqual(selected.IDENTIFIER, "local.jinke.dredge.voyage.painter.qa.20261019")
        self.assertEqual(selected.PRODUCT, "DREDGE Voyage Painter QA 20261019")
        self.assertEqual(selected.COMPANY, guard.COMPANY)
        generated = builder.guard_source("voyage-painter")
        for name, (original, replacement) in builder.PAINTER_IDENTITY.items():
            generated = generated.replace(name + " = " + json.dumps(replacement) + "\n", name + " = " + json.dumps(original) + "\n", 1)
        self.assertEqual(generated, builder.guard_source("merchant-travel"))
        with patch.object(selected, "user_home", return_value=self.home):
            profile = self.home / "Library/Application Support" / selected.IDENTIFIER
            self.assertEqual(selected.check_profile(profile), profile)
            for rejected in (self.profile, self.home / "Library/Application Support/local.jinke.dredge.voyage.qa.20261019",
                             self.home / "Library/Application Support/Black Salt Games/DREDGE"):
                with self.subTest(rejected=rejected), self.assertRaisesRegex(guard.Refusal, "fixed isolated"):
                    selected.check_profile(rejected)
        self.assertFalse(profile.exists())

    def test_painter_audit_requires_all_seventeen_methods_and_voyage_stack_headers(self):
        artifacts = self.root / "artifacts"
        artifacts.mkdir()
        original, patched, runtime = (artifacts / name for name in ("original", "patched", "runtime"))
        for path in (original, patched, runtime):
            path.write_bytes(path.name.encode())
        value = {
            "profile": "voyage-painter", "sourceMvid": builder.ORIGINAL_MVID,
            "sourceSha256": guard.sha(original), "outputSha256": guard.sha(patched), "runtimeSha256": guard.sha(runtime),
            "audit": {"passed": True, "addedAssemblyReferences": ["Jinke.DredgeMerchantTravel, Version=2.2.0.0"],
                "maxStackHeaderChanges": builder.VOYAGE_MAX_STACK_CHANGES,
                "changedMethods": [{"method": method, "originalBodySha256": "a" * 64, "restoredBodySha256": "a" * 64} for method in sorted(builder.PAINTER_METHODS)]},
        }
        report = artifacts / "audit.json"
        report.write_text(json.dumps(value))
        builder.validate_weaver_audit(report, original, patched, runtime, "voyage-painter")
        for profile in ("merchant-travel", "voyage-experience"):
            with self.subTest(profile=profile), self.assertRaisesRegex(guard.Refusal, "semantic audit"):
                builder.validate_weaver_audit(report, original, patched, runtime, profile)
        for missing in builder.PAINTER_METHODS:
            incomplete = copy.deepcopy(value)
            incomplete["audit"]["changedMethods"] = [entry for entry in value["audit"]["changedMethods"] if entry["method"] != missing]
            report.write_text(json.dumps(incomplete))
            with self.subTest(missing=missing), self.assertRaisesRegex(guard.Refusal, "approved voyage-painter hooks"):
                builder.validate_weaver_audit(report, original, patched, runtime, "voyage-painter")
        value["audit"]["maxStackHeaderChanges"] = []
        report.write_text(json.dumps(value))
        with self.assertRaisesRegex(guard.Refusal, "stack headers"):
            builder.validate_weaver_audit(report, original, patched, runtime, "voyage-painter")

    def painter_source_fixture(self):
        source = self.root / "assets/JinkeSkins"
        for relative in builder.PAINTER_ASSET_FILES:
            path = source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((relative + " original fixture").encode())
        return source

    def test_painter_assets_are_four_exact_fingerprinted_copies_with_no_old_profile_allowance(self):
        self.painter_source_fixture()
        expected = builder.painter_assets()
        (self.app / "Contents/Resources/Data/StreamingAssets").mkdir(parents=True)
        baseline = guard.tree_manifest(self.app / "Contents")
        builder.copy_painter_assets(self.app, expected)
        changes = builder.validate_copy_delta(self.app, baseline, "voyage-painter", asset_manifest=expected)
        self.assertEqual(len(changes), 4)
        for profile in ("merchant-travel", "voyage-experience"):
            with self.subTest(profile=profile), self.assertRaisesRegex(guard.Refusal, "Unapproved app copy change"):
                builder.validate_copy_delta(self.app, baseline, profile)
        flag = self.app / "Contents" / builder.PAINTER_ASSET_REL / "ramen/flag.png"
        flag.write_bytes(b"changed texture")
        with self.assertRaisesRegex(guard.Refusal, "asset content"):
            builder.validate_copy_delta(self.app, baseline, "voyage-painter", asset_manifest=expected)

    def test_painter_rejects_extra_missing_or_changed_source_assets(self):
        source = self.painter_source_fixture()
        expected = builder.painter_assets()
        extra = source / "unexpected.png"
        extra.write_bytes(b"unapproved")
        with self.assertRaisesRegex(guard.Refusal, "exactly the approved"):
            builder.painter_assets()
        extra.unlink()
        flag = source / "moon/flag.png"
        original = flag.read_bytes()
        flag.unlink()
        with self.assertRaisesRegex(guard.Refusal, "exactly the approved"):
            builder.painter_assets()
        flag.write_bytes(original + b"changed while compiling")
        with self.assertRaisesRegex(guard.Refusal, "assets changed"):
            builder.copy_painter_assets(self.app, expected)
        self.assertFalse(self.app.exists())

    def test_painter_copy_audit_rejects_extra_or_missing_delivered_assets(self):
        source = self.painter_source_fixture()
        expected = builder.painter_assets()
        (self.app / "Contents/Resources/Data/StreamingAssets").mkdir(parents=True)
        builder.copy_painter_assets(self.app, expected)
        extra = self.app / "Contents" / builder.PAINTER_ASSET_REL / "extra.png"
        extra.write_bytes(b"unexpected")
        with self.assertRaisesRegex(guard.Refusal, "asset content"):
            builder.validate_copy_delta(self.app, {}, "voyage-painter", asset_manifest=expected)
        extra.unlink()
        (self.app / "Contents" / builder.PAINTER_ASSET_REL / "moon/flag.png").unlink()
        with self.assertRaisesRegex(guard.Refusal, "asset content"):
            builder.validate_copy_delta(self.app, {}, "voyage-painter", asset_manifest=expected)
        self.assertEqual((source / "moon/flag.png").read_bytes(), b"moon/flag.png original fixture")

    def test_complete_app_manifest_protects_each_painter_asset(self):
        self.fake_app(painter_assets=True)
        guard.verify_app(self.app, signature=False)
        for relative in builder.PAINTER_ASSET_FILES:
            path = self.app / "Contents" / builder.PAINTER_ASSET_REL / relative
            original = path.read_bytes()
            path.write_bytes(original + b"tampered")
            with self.subTest(relative=relative), self.assertRaises(guard.Refusal):
                guard.verify_app(self.app, signature=False)
            path.write_bytes(original)

    def test_source_fingerprints_include_painter_top_level_and_only_shared_cosmetic_primitives(self):
        painter = self.root / "src/Painter"
        painter.mkdir(parents=True)
        (painter / "NativePainter.cs").write_text("// native dialogue")
        (painter / "Tests").mkdir()
        (painter / "Tests/Ignored.cs").write_text("// fixture")
        cosmetic = self.root / "src/CosmeticPack"
        cosmetic.mkdir(parents=True)
        for name in (*builder.PAINTER_SHARED_SOURCES, "SkinWindow.cs", "SettingsStore.cs", "SkinLog.cs"):
            (cosmetic / name).write_text("// " + name)
        sources = builder.managed_sources()
        expected = {"src/Painter/NativePainter.cs"} | {"src/CosmeticPack/" + name for name in builder.PAINTER_SHARED_SOURCES}
        self.assertEqual(set(sources), expected)
        (painter / "NativePainter.cs").write_text("// revised dialogue")
        self.assertNotEqual(builder.managed_sources(), sources)

    def test_launch_environment_is_exact_and_rejects_injection(self):
        self.fake_app()
        proof = guard.verify_app(self.app, signature=False)
        with patch.dict(os.environ, {}, clear=True):
            environment = guard.launch_environment(self.app, proof)
            self.assertEqual(environment[guard.PROOF_ENV], str(self.app / guard.PROOF_REL))
            self.assertEqual(environment[guard.PROFILE_ENV], str(self.profile))
            self.assertEqual(environment["SteamAppId"], "1562430")
        with patch.dict(os.environ, {"MONO_PATH": "/bad"}), self.assertRaisesRegex(guard.Refusal, "injection"):
            guard.launch_environment(self.app, proof)

    def test_no_steam_is_refused_without_starting_steam_or_game(self):
        with patch.object(guard, "verify_app", return_value={}), patch.object(guard, "process_running", side_effect=[False, False]), patch.object(guard.subprocess, "run") as run, self.assertRaisesRegex(guard.Refusal, "Steam is not running"):
            guard.launch(self.app)
        run.assert_not_called()

    def test_duplicate_game_is_refused(self):
        with patch.object(guard, "verify_app", return_value={}), patch.object(guard, "process_running", return_value=True), patch.object(guard.subprocess, "run") as run, self.assertRaisesRegex(guard.Refusal, "already running"):
            guard.launch(self.app)
        run.assert_not_called()

    def test_launch_announces_verification_before_long_checks(self):
        events = []
        def refuse(app):
            events.append("verify")
            raise guard.Refusal("fixture stop")
        with patch.object(guard, "verify_app", side_effect=refuse), patch("builtins.print", side_effect=lambda *args, **kwargs: events.append(args[0])), self.assertRaises(guard.Refusal):
            guard.launch(self.app)
        self.assertEqual(events, ["正在核验独立游戏副本…", "verify"])

    def test_reseal_rejects_delivered_app_and_original(self):
        self.fake_app()
        for path in (self.app, self.root / "Contents"):
            with self.subTest(path=path), self.assertRaisesRegex(guard.Refusal, "QA reseal"):
                builder.refresh_qa_metadata(path)

    def test_invalid_qa_audit_does_not_remove_existing_proof(self):
        self.app = self.root / "output/runtime/QA.app"
        self.fake_app()
        before = (self.app / guard.PROOF_REL).read_bytes()
        with self.assertRaisesRegex(guard.Refusal, "Invalid QA Weaver"):
            builder.refresh_qa_metadata(self.app, weaver_audit_sha256="wrong")
        self.assertEqual((self.app / guard.PROOF_REL).read_bytes(), before)
        self.assertTrue((self.app / guard.MANIFEST_REL).exists())

    def test_signature_metadata_cleanup_is_copy_only(self):
        self.fake_app()
        metadata = self.app / "Contents/MacOS/._DREDGE"
        metadata.write_bytes(b"AppleDouble fixture")
        before = (self.app / "Contents/MacOS/DREDGE").read_bytes()
        self.assertEqual(builder.remove_generated_metadata(self.app), 1)
        self.assertFalse(metadata.exists())
        self.assertEqual((self.app / "Contents/MacOS/DREDGE").read_bytes(), before)
        with self.assertRaisesRegex(guard.Refusal, "Metadata cleanup"):
            builder.remove_generated_metadata(self.root / "Contents")

    def test_generated_launcher_quotes_special_filename(self):
        self.app = self.root / "output/Quote ' $(never-run).app"
        self.fake_app()
        launcher = self.root / "output/test.command"
        builder.make_launcher(self.app, launcher)
        subprocess.run(["/bin/zsh", "-n", str(launcher)], check=True)
        self.assertIn(guard.sha(self.app / guard.GUARD_REL), launcher.read_text())
        self.assertIn('--app "$app"', launcher.read_text())
        self.assertFalse((self.root / "never-run").exists())


@unittest.skipUnless(os.environ.get("JINKE_MERCHANT_BUILD_INTEGRATION") == "1", "Opt in to real fresh dotnet builds.")
class FreshCompilationTests(Fixture):
    def copy_projects(self):
        for project in ("MerchantTravel", "VoyageExperience", "Painter", "Weaver"):
            origin = WORKSPACE / "src" / project
            if not origin.exists():
                continue
            destination = self.root / "src" / project
            destination.mkdir(parents=True)
            for source in origin.iterdir():
                if source.is_file() and not source.name.startswith("._") and source.suffix in (".cs", ".csproj", ".json"):
                    shutil.copyfile(source, destination / source.name)
        cosmetic = self.root / "src/CosmeticPack"
        cosmetic.mkdir(parents=True)
        for name in builder.PAINTER_SHARED_SOURCES:
            shutil.copyfile(WORKSPACE / "src/CosmeticPack" / name, cosmetic / name)
        managed = self.root / "Contents/Resources/Data/Managed"
        managed.mkdir(parents=True)
        project = ET.parse(WORKSPACE / "src/MerchantTravel/MerchantTravel.csproj")
        names = {Path(node.text).name for node in project.findall(".//HintPath")}
        for name in names:
            shutil.copyfile(WORKSPACE / "Contents/Resources/Data/Managed" / name, managed / name)
        return managed

    def test_real_build_error_never_uses_stale_runtime(self):
        self.copy_projects()
        stale = self.root / "src/MerchantTravel/bin/Release/net472/Jinke.DredgeMerchantTravel.dll"
        stale.parent.mkdir(parents=True)
        stale.write_bytes(b"stale fixture")
        (self.root / "src/MerchantTravel/Invalid.cs").write_text("#error Intentional build isolation test\n")
        stage = self.root / "output/builds/error"
        stage.mkdir(parents=True)
        with self.assertRaises(subprocess.CalledProcessError):
            builder.build_managed(stage)
        self.assertEqual(stale.read_bytes(), b"stale fixture")
        self.assertFalse((stage / "managed-build.json").exists())

    def test_real_fresh_build_and_four_hook_semantic_audit(self):
        managed = self.copy_projects()
        stage = self.root / "output/builds/fresh"
        stage.mkdir(parents=True)
        runtime, weaver, report = builder.build_managed(stage)
        self.assertTrue(runtime.is_relative_to(stage / "managed"))
        self.assertEqual(report["runtimeArtifactSha256"], guard.sha(runtime))
        original = managed / "Assembly-CSharp.dll"
        output = stage / "Assembly-CSharp.dll"
        builder.run("dotnet", weaver, "--merchant-travel", original, output, runtime)
        audit = builder.validate_weaver_audit(Path(str(output) + ".audit.json"), original, output, runtime)
        self.assertTrue(audit["audit"]["passed"])
        self.assertEqual(len(audit["audit"]["changedMethods"]), 4)

    def test_real_fresh_build_and_voyage_semantic_audit(self):
        managed = self.copy_projects()
        stage = self.root / "output/builds/fresh-voyage"
        stage.mkdir(parents=True)
        runtime, weaver, report = builder.build_managed(stage)
        self.assertTrue(any(path.startswith("src/VoyageExperience/") for path in report["sources"]))
        original = managed / "Assembly-CSharp.dll"
        output = stage / "Assembly-CSharp.dll"
        builder.run("dotnet", weaver, "--voyage-experience", original, output, runtime)
        audit = builder.validate_weaver_audit(Path(str(output) + ".audit.json"), original, output, runtime, "voyage-experience")
        self.assertEqual(len(audit["audit"]["changedMethods"]), len(builder.VOYAGE_METHODS))

    def test_real_fresh_build_and_painter_semantic_audit(self):
        managed = self.copy_projects()
        stage = self.root / "output/builds/fresh-painter"
        stage.mkdir(parents=True)
        runtime, weaver, report = builder.build_managed(stage)
        self.assertTrue(any(path.startswith("src/Painter/") for path in report["sources"]))
        for name in builder.PAINTER_SHARED_SOURCES:
            self.assertIn("src/CosmeticPack/" + name, report["sources"])
        original = managed / "Assembly-CSharp.dll"
        output = stage / "Assembly-CSharp.dll"
        builder.run("dotnet", weaver, "--voyage-painter", original, output, runtime)
        audit = builder.validate_weaver_audit(Path(str(output) + ".audit.json"), original, output, runtime, "voyage-painter")
        self.assertEqual(len(audit["audit"]["changedMethods"]), 17)


if __name__ == "__main__":
    unittest.main()
