#!/usr/bin/env python3
"""Ship-style identity and build-boundary fixtures; never open a game save."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

WORKSPACE = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(WORKSPACE / "tools"))
import build_merchant_travel as builder
import build_ship_styles as styles
import merchant_travel_guard as guard


class ShipStylesIsolationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ship-styles-builder-", dir=str(Path(tempfile.gettempdir()).resolve()))
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.root_patch = patch.object(builder, "ROOT", self.root)
        self.root_patch.start()

    def tearDown(self):
        self.root_patch.stop()
        self.temporary.cleanup()

    def test_wrapper_selects_fixed_new_identity_with_original_seventeen_hooks(self):
        with patch.object(styles.builder, "build", return_value={}) as build, patch("builtins.print"):
            self.assertEqual(styles.main([]), 0)
        build.assert_called_once_with("DREDGE 双船型外观预览 2.3.0.app", "voyage-painter", identity_variant="ship-styles")
        self.assertEqual(len(builder.require_profile("voyage-painter")), 17)
        self.assertEqual(builder.ORIGINAL_MVID, "d810863b-fda8-4056-bdde-77f7c349ae63")

    def test_new_guard_changes_exactly_the_fixed_identity_constants(self):
        generated = builder.guard_source("voyage-painter", "ship-styles")
        for name, (original, replacement) in builder.SHIP_STYLES_IDENTITY.items():
            self.assertEqual(generated.count(name + " = " + json.dumps(replacement) + "\n"), 1)
            generated = generated.replace(name + " = " + json.dumps(replacement) + "\n", name + " = " + json.dumps(original) + "\n", 1)
        self.assertEqual(generated, builder.guard_source("merchant-travel"))
        self.assertEqual(builder.guard_for_profile("voyage-painter").IDENTIFIER, "local.jinke.dredge.voyage.painter.qa.20261019")
        self.assertEqual(guard.IDENTIFIER, "local.jinke.dredge.merchanttravel.qa.20261018")

    def test_new_guard_rejects_formal_and_previous_profiles_without_creating_any(self):
        selected = builder.guard_for_profile("voyage-painter", "ship-styles")
        self.assertEqual(selected.IDENTIFIER, "local.jinke.dredge.shipstyles.qa.20261021")
        self.assertEqual(selected.PRODUCT, "DREDGE Ship Styles QA 20261021")
        self.assertIs(selected.Refusal, guard.Refusal)
        with patch.object(selected, "user_home", return_value=self.home):
            target = self.home / "Library/Application Support" / selected.IDENTIFIER
            self.assertEqual(selected.check_profile(target), target)
            for identifier in (guard.IDENTIFIER, builder.PAINTER_IDENTITY["IDENTIFIER"][1], builder.VOYAGE_IDENTITY["IDENTIFIER"][1], "com.blacksaltgames.dredge", "Black Salt Games/DREDGE"):
                with self.subTest(identifier=identifier), self.assertRaisesRegex(guard.Refusal, "fixed isolated"):
                    selected.check_profile(self.home / "Library/Application Support" / identifier)
            self.assertFalse(target.exists())
        self.assertFalse(self.home.exists())

    def test_fixed_context_restores_original_guard_even_when_build_fails(self):
        def fail(name, profile, variant):
            self.assertEqual(builder.guard.IDENTIFIER, builder.SHIP_STYLES_IDENTITY["IDENTIFIER"][1])
            self.assertEqual((name, profile, variant), ("New.app", "voyage-painter", "ship-styles"))
            raise RuntimeError("fixture interruption")
        with patch.object(builder, "_build", side_effect=fail), self.assertRaisesRegex(RuntimeError, "fixture interruption"):
            builder.build("New.app", "voyage-painter", identity_variant="ship-styles")
        self.assertIs(builder.guard, guard)
        self.assertFalse((self.root / "output").exists())

    def test_no_custom_identity_or_other_patch_profile_can_be_selected(self):
        for profile, variant in (("voyage-painter", "custom"), ("merchant-travel", "ship-styles"), ("voyage-experience", "ship-styles")):
            with self.subTest(profile=profile, variant=variant), self.assertRaisesRegex(guard.Refusal, "fixed identity"):
                builder.build("New.app", profile, identity_variant=variant)
        self.assertIs(builder.guard, guard)
        self.assertFalse((self.root / "output").exists())

    def test_existing_app_staging_and_launcher_are_never_overwritten(self):
        for blocked_index in range(3):
            name = "Existing%d.app" % blocked_index
            paths = builder.paths_for(name, "voyage-painter")
            blocked = paths[blocked_index]
            blocked.parent.mkdir(parents=True, exist_ok=True)
            if blocked_index < 2:
                blocked.mkdir()
                marker = blocked / "keep.txt"
            else:
                marker = blocked
            marker.write_text("old artifact")
            with self.assertRaisesRegex(guard.Refusal, "overwrite"):
                builder.paths_for(name, "voyage-painter")
            self.assertEqual(marker.read_text(), "old artifact")

    def test_embedded_authored_resources_are_in_fresh_build_fingerprints(self):
        resource = self.root / "src/ShipAppearance/Resources/cthulhu-v1.bin"
        resource.parent.mkdir(parents=True)
        resource.write_bytes(b"authored resource v1")
        ignored = self.root / "src/VoyageExperience/Resources/ignored.bin"
        ignored.parent.mkdir(parents=True)
        ignored.write_bytes(b"not a compiled ship resource")
        before = builder.managed_sources()
        self.assertEqual(set(before), {"src/ShipAppearance/Resources/cthulhu-v1.bin"})
        resource.write_bytes(b"authored resource v2")
        self.assertNotEqual(builder.managed_sources(), before)


if __name__ == "__main__":
    unittest.main()
