"""Read-only inventory of the supplied Unity content; never writes into Contents."""
from pathlib import Path
import json, re, hashlib
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'Contents/Resources/Data'
OUT = ROOT / 'research/evidence'
OUT.mkdir(parents=True, exist_ok=True)
files = sorted((DATA / 'StreamingAssets/aa/StandaloneOSX').glob('*.bundle'))
files += [DATA / n for n in ('resources.assets', 'sharedassets0.assets', 'globalgamemanagers.assets')]
rows, errors = [], []
for path in files:
    if path.name.startswith('._'):
        continue
    try:
        env = UnityPy.load(str(path))
        for obj in env.objects:
            if obj.type.name not in ('Texture2D', 'Material', 'Mesh', 'MonoBehaviour', 'MonoScript', 'GameObject'):
                continue
            try:
                tree = obj.read_typetree()
                name = tree.get('m_Name', '')
                row = {'bundle': str(path.relative_to(ROOT)), 'asset': obj.assets_file.name,
                       'path_id': obj.path_id, 'type': obj.type.name, 'name': name}
                if obj.type.name == 'Texture2D':
                    row.update({k: tree.get(k) for k in ('m_Width', 'm_Height', 'm_TextureFormat', 'm_MipCount')})
                if re.search(r'flag|paint|player|boat|hull|bunting', name, re.I):
                    row['details'] = tree if obj.type.name not in ('Texture2D', 'Mesh') else {k:v for k,v in tree.items() if k not in ('image data', 'm_StreamData', 'm_VertexData', 'm_IndexBuffer', 'm_CompressedMesh')}
                rows.append(row)
            except Exception as exc:
                errors.append({'bundle': path.name, 'path_id': obj.path_id, 'type': obj.type.name, 'error': str(exc)[:180]})
    except Exception as exc:
        errors.append({'bundle':path.name, 'error':str(exc)[:200]})
result = {'unitypy_version':UnityPy.__version__, 'files_examined':len(files), 'objects':rows, 'errors':errors}
(OUT / 'asset-inventory.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
matches = [r for r in rows if re.search(r'flag|paint|player|boat|hull|bunting', r['name'], re.I)]
print(json.dumps({'files':len(files),'rows':len(rows),'errors':len(errors),'matches':[{k:v for k,v in r.items() if k!='details'} for r in matches]},ensure_ascii=False,indent=2))
