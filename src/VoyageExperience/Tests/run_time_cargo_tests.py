"""Read shipped layout assets and run the isolated C# simulation; never launch Unity."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
try:
    import UnityPy
except ImportError:
    # Reuse the already installed read-only asset tooling when the caller's
    # interpreter does not carry it. This does not install any package.
    cache = Path.home() / '.cache/uv/archive-v0'
    if cache.exists():
        sys.path.extend(str(p) for p in cache.iterdir() if p.is_dir())
    import UnityPy

root = Path(__file__).resolve().parents[3]
bundles = root / 'Contents/Resources/Data/StreamingAssets/aa/StandaloneOSX'
sources = [next(bundles.glob('gridconfigs_assets_all_*.bundle')),
           next(bundles.glob('upgradedata_assets_all_*.bundle'))]
data = {'sources': [{'name': p.name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sources], 'hulls': [], 'upgrades': []}
for index, path in enumerate(sources):
    for obj in UnityPy.load(str(path)).objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        tree = obj.read_typetree()
        if index == 0 and re.fullmatch(r'Tier[1-5]Hull', tree.get('m_Name', '')):
            data['hulls'].append({'tier': int(tree['m_Name'][4]), 'data': tree})
        if index == 1 and tree.get('id') and 'cellGroupConfigs' in tree:
            data['upgrades'].append(tree)
assert len(data['hulls']) == 5, 'All five native hulls must be inspected'
assert len(data['upgrades']) >= 16, 'Native slot upgrade collection unexpectedly incomplete'
safe_counts = {}
for hull in sorted(data['hulls'], key=lambda value: value['tier']):
    tier = hull['tier']
    # Native groups apply in sequence, so the final group at each coordinate
    # determines its restriction. Exclude every available native slot upgrade,
    # including upgrades the current save has not bought yet.
    original = {}
    for group in hull['data']['cellGroupConfigs']:
        for cell in group['cells']:
            original[(cell['x'], cell['y'])] = group
    reserved = {(cell['x'], cell['y']) for upgrade in data['upgrades'] if upgrade['tier'] == tier
                for group in upgrade['cellGroupConfigs'] for cell in group['cells']}
    safe = [point for point, group in original.items() if point not in reserved
            and group['isHidden'] and group['damageImmune']
            and group['itemType'] == 0 and group['itemSubtype'] == 0]
    safe_counts[tier] = len(safe)
assert min(safe_counts.values()) == 16, 'Shared eight-level capacity must match the shipped smallest hull'
data['safe_unused_cells'] = safe_counts
with tempfile.TemporaryDirectory(prefix='dredge-time-cargo-tests-') as temporary:
    temp = Path(temporary)
    fixture = temp / 'native-layouts.json'
    fixture.write_text(json.dumps(data), encoding='utf-8')
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1')
    command = ['dotnet', 'run', '--project', str(Path(__file__).with_name('TimeCargo.Tests.csproj')),
               '--artifacts-path', str(temp / 'artifacts'), '--', str(fixture)]
    result = subprocess.run(command, cwd=root, env=env)
    print(json.dumps({'sources': data['sources'], 'hulls': len(data['hulls']),
                      'native_slot_upgrades': len(data['upgrades']), 'safe_unused_cells': safe_counts,
                      'common_maximum_two_cell_levels': min(safe_counts.values()) // 2,
                      'scope': 'simulation only; original bundles read; game not executed'}, ensure_ascii=False))
    raise SystemExit(result.returncode)
