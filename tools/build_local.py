"""Build a local, reversible .app copy. The supplied Contents tree stays read-only."""
from pathlib import Path
import argparse, hashlib, json, plistlib, shutil, subprocess, os, stat, shlex

ROOT = Path(__file__).resolve().parents[1]
ORIGINAL_SHA = 'a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def run(*args):
    subprocess.run([str(x) for x in args], cwd=ROOT, check=True)

def build_managed(staging):
    # A new artifacts directory isolates both bin and obj from previously tested builds.
    artifacts = staging / 'managed'
    artifacts.mkdir()
    sdk = subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip()
    commands = []
    for project in ('CosmeticPack', 'Weaver'):
        command = ['dotnet', 'build', str(ROOT / 'src' / project / (project + '.csproj')),
                   '-c', 'Release', '--no-incremental', '--artifacts-path', str(artifacts)]
        if project == 'Weaver':
            command.append('-p:RestoreLockedMode=true')
        run(*command)
        commands.append(command)
    # These single-target projects use the SDK artifacts layout's Release pivot.
    runtime = artifacts / 'bin/CosmeticPack/release/Jinke.DredgeSkins.dll'
    weaver = artifacts / 'bin/Weaver/release/Weaver.dll'
    if not runtime.is_file() or not weaver.is_file():
        raise SystemExit('Managed build did not produce the expected fresh artifacts')
    provenance = {'sdkVersion':sdk, 'commands':commands,
                  'runtimeArtifactPath':str(runtime), 'runtimeArtifactSha256':sha(runtime),
                  'weaverArtifactPath':str(weaver), 'weaverArtifactSha256':sha(weaver)}
    with (staging / 'managed-build.json').open('x') as stream:
        json.dump(provenance, stream, ensure_ascii=False, indent=2)
    return runtime, weaver, provenance

def require_new_output(path):
    # Resolve existing ancestors before creating any staging file. A linked
    # output/builds directory must never redirect a write into the source tree.
    if not path.resolve().is_relative_to(ROOT / 'output'):
        raise SystemExit('Output path escapes the local output directory: ' + str(path))
    if path.exists() or path.is_symlink():
        raise SystemExit('Refusing to overwrite an existing output: ' + str(path))

def copy_tree(source, destination):
    """Copy bytes/modes only: ExFAT AppleDouble metadata is not a game resource."""
    destination.mkdir(parents=True)
    for base, dirs, files in os.walk(source):
        dirs[:] = [d for d in dirs if not d.startswith('._')]
        relative = Path(base).relative_to(source)
        for name in dirs:
            (destination / relative / name).mkdir()
        for name in files:
            if name.startswith('._') or name == '.DS_Store':
                continue
            src = Path(base) / name
            dst = destination / relative / name
            shutil.copyfile(src, dst)
            dst.chmod(stat.S_IMODE(src.stat().st_mode))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--qa', action='store_true', help='Use a separate company/product and save location for native QA')
    parser.add_argument('--name', default=None, help='New .app basename inside output, never an existing destination')
    args = parser.parse_args()
    name = args.name or ('DREDGE-Skins-QA.app' if args.qa else 'DREDGE 外观版.app')
    if '/' in name or '\\' in name or not name.endswith('.app') or name.startswith('.'):
        raise SystemExit('Expected a new .app basename')
    dest = ROOT / 'output' / ('runtime' if args.qa else '') / name
    staging = ROOT / 'output' / 'builds' / name.removesuffix('.app')
    launcher = dest.parent / (name.removesuffix('.app') + ' — 启动.command')
    for path in (dest, staging, launcher):
        require_new_output(path)
    original = ROOT / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
    if sha(original) != ORIGINAL_SHA:
        raise SystemExit('Unsupported original game fingerprint')
    staging.mkdir(parents=True)
    runtime, weaver, managed_build = build_managed(staging)
    runtime_snapshot = staging / runtime.name
    shutil.copy2(runtime, runtime_snapshot)
    runtime = runtime_snapshot
    patched = staging / 'Assembly-CSharp.dll'
    run('dotnet', weaver, original, patched, runtime)
    dest.parent.mkdir(parents=True, exist_ok=True)
    copy_tree(ROOT / 'Contents', dest / 'Contents')
    data = dest / 'Contents/Resources/Data'
    shutil.copyfile(patched, data / 'Managed/Assembly-CSharp.dll')
    shutil.copyfile(runtime, data / 'Managed' / runtime.name)
    copy_tree(ROOT / 'assets/JinkeSkins', data / 'StreamingAssets/JinkeSkins')
    extra_changes = []
    if args.qa:
        import UnityPy
        settings_path = data / 'globalgamemanagers'
        env = UnityPy.load(str(settings_path))
        found = False
        for obj in env.objects:
            if obj.type.name == 'PlayerSettings':
                tree = obj.read_typetree()
                tree['companyName'] = 'Jinke Local Tests'
                tree['productName'] = 'DREDGE Skins QA 20261016'
                obj.save_typetree(tree)
                found = True
        if not found:
            raise RuntimeError('Cannot isolate QA save profile')
        settings_path.write_bytes(env.file.save())
        (data / 'app.info').write_text('Jinke Local Tests\nDREDGE Skins QA 20261016')
        info = dest / 'Contents/Info.plist'
        plist = plistlib.loads(info.read_bytes())
        plist['CFBundleIdentifier'] = 'local.jinke.dredge.skins.qa.20261016'
        plist['CFBundleName'] = 'DREDGE Skins QA'
        info.write_bytes(plistlib.dumps(plist))
        extra_changes = ['QA-only PlayerSettings companyName/productName and app.info', 'QA-only CFBundleIdentifier/CFBundleName']
    # Steam's documented local-development launch prevents RestartAppIfNecessary
    # from redirecting this local build to the installed Steam copy. It still
    # requires the running Steam client and the user's existing game licence.
    # Text resources belong in Resources, not macOS's code-only MacOS directory.
    (dest / 'Contents/Resources/steam_appid.txt').write_text('1562430\n')
    extra_changes.append('Steam local-development steam_appid.txt; authentication unchanged')
    # Only the newly created local copy is signed. No settings or installed applications are changed.
    for sidecar in dest.rglob('._*'):
        if sidecar.is_file():
            sidecar.unlink()
    run('codesign', '--force', '--sign', '-', '--preserve-metadata=entitlements', dest)
    run('codesign', '--verify', '--deep', '--strict', dest)
    lines = ['#!/bin/zsh', 'set -eu',
             'if /usr/bin/pgrep -x DREDGE >/dev/null; then',
             '  print "请先正常退出已经运行的 DREDGE，再打开皮肤版。"',
             '  read "?按回车关闭此窗口。"', '  exit 1', 'fi',
             'launch_dir="${0:A:h}"',
             'cd -- "$launch_dir"/' + shlex.quote(name + '/Contents/Resources'),
             'if ! /usr/bin/pgrep -x steam_osx >/dev/null; then',
             '  /usr/bin/open -a Steam',
             '  print "请等 Steam 启动并完成登录，再重新打开此启动器。"',
             '  read "?按回车关闭此窗口。"', '  exit 1', 'fi',
             'export SteamAppId=1562430', 'export SteamGameId=1562430']
    if args.qa:
        lines.append('export JINKE_DREDGE_SKINS_SETTINGS_DIR=' + shlex.quote(str(ROOT / 'output/runtime/qa-settings')))
        lines.append('exec ../MacOS/DREDGE -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile ' + shlex.quote(str(dest.parent / (name.removesuffix('.app') + '-player.log'))))
    else:
        lines += ['mkdir -p "$HOME/Library/Logs/JinkeDredgeSkins"',
                  'exec ../MacOS/DREDGE -logFile "$HOME/Library/Logs/JinkeDredgeSkins/player.log" "$@"']
    with launcher.open('x') as stream:
        stream.write('\n'.join(lines) + '\n')
    launcher.chmod(0o755)
    report = {'app':str(dest), 'qa':args.qa, 'originalAssemblySha256':sha(original),
              'patchedAssemblySha256':sha(patched), 'runtimeSha256':sha(runtime),
              'managedBuild':managed_build,
              'extraChanges':extra_changes, 'launcher':str(launcher), 'signature':'local ad-hoc; verification passed',
              'nativeRuntimeTest':'not implied by this build'}
    (staging / 'build.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print(json.dumps(report, ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
