"""Offline tests against the shipped Yarn VM and a read-only extraction of its painter program."""
from pathlib import Path
import hashlib
import json
import subprocess
import UnityPy

ROOT = Path(__file__).resolve().parents[3]
bundle = ROOT / "Contents/Resources/Data/StreamingAssets/aa/StandaloneOSX/gamescene_scenes_all_82c85a7d3173dc27b7c4f7b6017300d3.bundle"
expected = "e30356f731a9b07fc10518a801feee608591b0c5c00007b03e8a12915705443d"
assert hashlib.sha256(bundle.read_bytes()).hexdigest() == expected, "Original bundle identity changed"
environment = UnityPy.load(str(bundle))
project = next(obj for obj in environment.objects if obj.path_id == 10829).read_typetree()
program = bytes(project["compiledYarnProgram"])
assert hashlib.sha256(program).hexdigest() == "0d40d34a713e80c9dfa71e744ffcb25b7600c69a8336158f5351c9d926b52fd3"
destination = ROOT / "output/runtime/painter-dialogue-tests"
destination.mkdir(parents=True, exist_ok=True)
(destination / "original-program.bytes").write_bytes(program)
metadata = project["lineMetadata"]["_lineMetadata"]
(destination / "original-metadata.json").write_text(json.dumps(dict(zip(metadata["keys"], metadata["values"]))), encoding="utf-8")
artifacts = destination / "artifacts"
subprocess.run(["dotnet", "build", str(Path(__file__).with_name("PainterDialogue.Tests.csproj")), "--artifacts-path", str(artifacts), "--no-incremental"], cwd=ROOT, check=True)
subprocess.run(["dotnet", str(artifacts / "bin/PainterDialogue.Tests/debug/PainterDialogue.Tests.dll"), str(destination / "original-program.bytes"), str(destination / "original-metadata.json")], cwd=ROOT, check=True)
assert hashlib.sha256(bundle.read_bytes()).hexdigest() == expected, "Read-only source bundle changed"
