"""Exercise real managed compilation, stopping before any game copy or signing."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
BUILDER = ROOT / 'tools/build_local.py'
OLD_TIME = 1_000_000_000
OPTIONS = None


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def legacy_hashes(root):
    return {str(path.relative_to(root)):sha(path)
            for project in ('CosmeticPack', 'Weaver') for folder in ('bin', 'obj')
            for path in (root / 'src' / project / folder).rglob('*')
            if path.is_file() and path.name != '.DS_Store'
            and not any(part.startswith('._') for part in path.parts)}


def load_builder(path, workspace):
    module = types.ModuleType('build_local_test')
    module.__file__ = str(path)
    exec(compile(path.read_text(), str(path), 'exec'), module.__dict__)
    module.ROOT = workspace
    return module


class GameCopyBoundary(Exception):
    pass


class FreshBuildTests(unittest.TestCase):
    def workspace(self, name, projects=False):
        root = OPTIONS.workspace / name
        managed = root / 'Contents/Resources/Data/Managed'
        managed.mkdir(parents=True)
        reference_names = ['Assembly-CSharp', 'mscorlib', 'System', 'System.Core', 'netstandard', 'Newtonsoft.Json',
                           'UnityEngine.CoreModule', 'UnityEngine.IMGUIModule', 'UnityEngine.ImageConversionModule',
                           'UnityEngine.InputLegacyModule', 'UnityEngine.TextRenderingModule', 'UnityEngine.UIModule', 'UnityEngine.UI']
        for name in reference_names if projects else ['Assembly-CSharp']:
            shutil.copyfile(ROOT / 'Contents/Resources/Data/Managed' / (name + '.dll'), managed / (name + '.dll'))
        if projects:
            for project in ('CosmeticPack', 'Weaver'):
                source = ROOT / 'src' / project
                destination = root / 'src' / project
                destination.mkdir(parents=True)
                for path in source.iterdir():
                    if path.is_file() and not path.name.startswith('._') and (path.suffix in ('.cs', '.csproj') or path.name == 'packages.lock.json'):
                        shutil.copy2(path, destination / path.name)
                # Keep valid stale binaries and previous intermediates in their old locations.
                for folder in ('bin', 'obj'):
                    shutil.copytree(source / folder, destination / folder, ignore=shutil.ignore_patterns('._*', '.DS_Store'))
        return root

    def invoke(self, root, name, baseline=False, fake_build=False):
        source = OPTIONS.baseline_builder if baseline else BUILDER
        module = load_builder(source, root)
        calls, copies = [], []

        def observed_run(*args):
            command = [str(value) for value in args]
            self.assertEqual(command[0], 'dotnet', 'Tests must never invoke signing or a game process')
            if fake_build:
                calls.append({'command':command, 'returncode':0, 'output':'test: successful process without artifacts'})
                return
            completed = subprocess.run(command, cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            calls.append({'command':command, 'returncode':completed.returncode, 'output':completed.stdout})
            completed.check_returncode()

        def stop_before_copy(source, destination):
            copies.append({'source':str(source), 'destination':str(destination)})
            raise GameCopyBoundary('Stopped before copying any game content')

        error = None
        try:
            with mock.patch.object(sys, 'argv', [str(source), '--name', name]), \
                    mock.patch.object(module, 'run', side_effect=observed_run), \
                    mock.patch.object(module, 'copy_tree', side_effect=stop_before_copy):
                module.main()
        except (Exception, SystemExit) as exception:
            error = exception
        outcome = {'errorType':type(error).__name__ if error else None, 'error':str(error), 'commands':calls, 'copyBoundary':copies}
        log_name = root.name + '-' + name.replace('/', '_').replace('\\', '_') + '.json'
        (OPTIONS.evidence / 'commands' / log_name).write_text(json.dumps(outcome, ensure_ascii=False, indent=2))
        return types.SimpleNamespace(error=error, calls=calls, copies=copies,
                                     staging=root / 'output/builds' / name.removesuffix('.app'))

    def assert_old_builder_reaches_copy(self, root, name):
        if OPTIONS.baseline_builder is None:
            return None
        old = self.invoke(root, name, baseline=True)
        self.assertIsInstance(old.error, GameCopyBoundary, str(old.error))
        self.assertFalse(any(call['command'][1] == 'build' for call in old.calls))
        return old

    def assert_compile_error_blocks(self, project, marker):
        root = self.workspace(project.lower() + '-compile-error', projects=True)
        old_runtime = root / 'src/CosmeticPack/bin/Release/net472/Jinke.DredgeSkins.dll'
        old_weaver = root / 'src/Weaver/bin/Release/net10.0/Weaver.dll'
        before = {str(path):sha(path) for path in (old_runtime, old_weaver)}
        legacy_before = legacy_hashes(root)
        source = root / 'src' / project / 'BuildFreshnessError.cs'
        source.write_text('#error ' + marker + '\n')
        os.utime(source, (OLD_TIME, OLD_TIME))
        self.assertLess(source.stat().st_mtime, min(old_runtime.stat().st_mtime, old_weaver.stat().st_mtime))
        result = self.invoke(root, 'CompileMustFail.app')
        self.assertIsInstance(result.error, subprocess.CalledProcessError, str(result.error))
        self.assertIn(marker, result.error.stdout, 'Must fail on the changed source, not an unrelated restore failure')
        self.assertFalse(result.copies)
        self.assertFalse((result.staging / 'Assembly-CSharp.dll').exists())
        self.assertTrue(all(call['command'][1] == 'build' for call in result.calls))
        self.assertEqual(before, {str(path):sha(path) for path in (old_runtime, old_weaver)})
        self.assertEqual(legacy_before, legacy_hashes(root))
        # The exact same stale-bin/backdated-source fixture passes the old script's managed stage.
        self.assert_old_builder_reaches_copy(root, 'OldBuilderIncorrectlyContinues.app')

    def test_backdated_runtime_error_blocks_valid_stale_binary(self):
        self.assert_compile_error_blocks('CosmeticPack', 'FRESH_RUNTIME_SOURCE_MUST_COMPILE')

    def test_backdated_weaver_error_blocks_valid_stale_binary(self):
        self.assert_compile_error_blocks('Weaver', 'FRESH_WEAVER_SOURCE_MUST_COMPILE')

    def test_changed_source_markers_reach_fresh_binaries_and_formal_audit(self):
        root = self.workspace('successful-source-change', projects=True)
        markers = {'CosmeticPack':'CURRENT_RUNTIME_SOURCE_20261017', 'Weaver':'CURRENT_WEAVER_SOURCE_20261017'}
        old_paths = {'CosmeticPack':root / 'src/CosmeticPack/bin/Release/net472/Jinke.DredgeSkins.dll',
                     'Weaver':root / 'src/Weaver/bin/Release/net10.0/Weaver.dll'}
        before = {key:sha(path) for key, path in old_paths.items()}
        legacy_before = legacy_hashes(root)
        for project, marker in markers.items():
            source = root / 'src' / project / 'BuildFreshnessMarker.cs'
            source.write_text('public static class BuildFreshnessMarker { public const string Value = "' + marker + '"; }\n')
            os.utime(source, (OLD_TIME, OLD_TIME))
            self.assertNotIn(marker.encode('utf-16-le'), old_paths[project].read_bytes())
        result = self.invoke(root, 'FreshSource.app')
        self.assertIsInstance(result.error, GameCopyBoundary, str(result.error))
        provenance = json.loads((result.staging / 'managed-build.json').read_text())
        fresh_paths = {'CosmeticPack':Path(provenance['runtimeArtifactPath']), 'Weaver':Path(provenance['weaverArtifactPath'])}
        for project, path in fresh_paths.items():
            self.assertIn(markers[project].encode('utf-16-le'), path.read_bytes())
            self.assertTrue(path.is_relative_to(result.staging / 'managed'))
            self.assertNotEqual(before[project], sha(path))
        self.assertEqual(before, {key:sha(path) for key, path in old_paths.items()})
        self.assertEqual(legacy_before, legacy_hashes(root))
        self.assertTrue((result.staging / 'managed/obj/CosmeticPack/project.assets.json').is_file())
        self.assertTrue((result.staging / 'managed/obj/Weaver/project.assets.json').is_file())
        self.assertTrue(provenance['sdkVersion'])
        self.assertEqual(provenance['commands'], [call['command'] for call in result.calls[:2]])
        self.assertTrue(all('--no-incremental' in command and '--artifacts-path' in command for command in provenance['commands']))
        self.assertIn('-p:RestoreLockedMode=true', provenance['commands'][1])
        self.assertEqual(result.calls[-1]['command'][1], provenance['weaverArtifactPath'])
        self.assertEqual(provenance['weaverArtifactSha256'], sha(fresh_paths['Weaver']))
        audit = json.loads((result.staging / 'Assembly-CSharp.dll.audit.json').read_text())
        self.assertTrue(audit['audit']['passed'])
        self.assertEqual(audit['audit']['unchangedMethodBodies'], 9373)
        self.assertEqual(audit['runtimeSha256'], provenance['runtimeArtifactSha256'])
        self.assertEqual(audit['runtimeSha256'], sha(result.staging / 'Jinke.DredgeSkins.dll'))
        old = self.assert_old_builder_reaches_copy(root, 'OldBuilderUsesStaleSource.app')
        if old:
            self.assertNotIn(markers['CosmeticPack'].encode('utf-16-le'), (old.staging / 'Jinke.DredgeSkins.dll').read_bytes())
            self.assertEqual(old.calls[-1]['command'][1], str(old_paths['Weaver']))

    def test_existing_outputs_and_invalid_names_are_rejected_before_build(self):
        for kind in ('app', 'staging', 'launcher'):
            with self.subTest(kind=kind):
                root = self.workspace('existing-' + kind)
                path = {'app':root / 'output/Existing.app', 'staging':root / 'output/builds/Existing',
                        'launcher':root / 'output/Existing — 启动.command'}[kind]
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('do not replace')
                result = self.invoke(root, 'Existing.app')
                self.assertIsInstance(result.error, SystemExit)
                self.assertIn('Refusing to overwrite', str(result.error))
                self.assertEqual(path.read_text(), 'do not replace')
                self.assertFalse(result.calls or result.copies)
        root = self.workspace('invalid-name')
        result = self.invoke(root, '../Escape.app')
        self.assertIsInstance(result.error, SystemExit)
        self.assertIn('basename', str(result.error))
        self.assertFalse(result.calls or result.copies)

    def test_linked_staging_cannot_write_into_original_contents(self):
        root = self.workspace('linked-output')
        (root / 'output').mkdir()
        (root / 'output/builds').symlink_to(root / 'Contents', target_is_directory=True)
        original = root / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
        before = sha(original)
        result = self.invoke(root, 'MustRefuse.app')
        self.assertIsInstance(result.error, SystemExit)
        self.assertIn('escapes', str(result.error))
        self.assertFalse(result.calls or result.copies)
        self.assertFalse((root / 'Contents/MustRefuse').exists())
        self.assertEqual(before, sha(original))

    def test_wrong_original_fingerprint_stops_before_build(self):
        root = self.workspace('wrong-original')
        (root / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll').write_bytes(b'not the verified game')
        result = self.invoke(root, 'WrongOriginal.app')
        self.assertIsInstance(result.error, SystemExit)
        self.assertIn('fingerprint', str(result.error))
        self.assertFalse(result.calls or result.copies)

    def test_missing_fresh_artifacts_never_falls_back_to_valid_old_bin(self):
        root = self.workspace('missing-artifacts', projects=True)
        result = self.invoke(root, 'NoFreshArtifacts.app', fake_build=True)
        self.assertIsInstance(result.error, SystemExit)
        self.assertIn('fresh artifacts', str(result.error))
        self.assertEqual(len(result.calls), 2)
        self.assertFalse(result.copies)
        self.assertFalse((result.staging / 'Jinke.DredgeSkins.dll').exists())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True, help='New directory for compact results and command logs')
    parser.add_argument('--workspace', type=Path, required=True, help='New ignored or temporary directory for fixture sources, bin and obj')
    parser.add_argument('--baseline-builder', type=Path, help='Read-only copy of the old script for differential verification')
    OPTIONS = parser.parse_args()
    OPTIONS.evidence = OPTIONS.evidence.resolve()
    OPTIONS.evidence.mkdir(parents=True, exist_ok=False)
    (OPTIONS.evidence / 'commands').mkdir()
    OPTIONS.workspace = OPTIONS.workspace.resolve()
    OPTIONS.workspace.mkdir(parents=True, exist_ok=False)
    if OPTIONS.baseline_builder:
        OPTIONS.baseline_builder = OPTIONS.baseline_builder.resolve(strict=True)
    start = time.monotonic()
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(FreshBuildTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    report = {'scope':'Managed build and formal IL audit only; game copy/signing/execution are stopped at the copy boundary',
              'testsRun':result.testsRun, 'passed':result.wasSuccessful(), 'elapsedSeconds':round(time.monotonic()-start, 3),
              'builderSha256':sha(BUILDER), 'baselineBuilderSha256':sha(OPTIONS.baseline_builder) if OPTIONS.baseline_builder else None,
              'failures':[test.id() for test, _ in result.failures], 'errors':[test.id() for test, _ in result.errors]}
    (OPTIONS.evidence / 'results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print(json.dumps(report, ensure_ascii=False, indent=2))
    raise SystemExit(0 if result.wasSuccessful() else 1)
