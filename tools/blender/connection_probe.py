"""Verify Blender authoring/export only; this does not build or run the game.

Run in a fresh process, for example:
  blender --background --factory-startup --python-exit-code 1 \
    --python tools/blender/connection_probe.py -- --output /absolute/new/directory

Only the newly created output directory is written. The only file reopened is
the .blend made by this invocation. No external models, preferences or sockets
are used. The asymmetric calibration solid is deliberately not a boat model.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy


OBJECT_NAME = "ConnectionProbe_AsymmetricSolid"
MESH_NAME = "ConnectionProbe_Mesh"
MATERIAL_NAMES = ("ProbeBody", "ProbeMarker")
SCHEMA = "jinke.blender.connection-probe.mesh.v1"
SCOPE = "Blender authoring, editable blend reopen and mesh-data export only; not Unity execution or game installation."

# Blender +X right, +Y forward, +Z up -> Unity +X right, +Y up, +Z forward.
# Swapping Y/Z is orthogonal with determinant -1. Map normals the same way,
# then reverse each triangle's last two corners so the geometric normal agrees
# with the mapped authored normal. This also preserves UV/normal corner pairing.
COORDINATES = {
    "source": "Blender local coordinates: X right, Y forward, Z up",
    "target": "Unity mesh local coordinates: X right, Y up, Z forward",
    "position_mapping": ["x", "z", "y"],
    "normal_mapping": ["nx", "nz", "ny"],
    "determinant": -1,
    "triangle_corner_order": [0, 2, 1],
    "units": "1 Blender unit = 1 Unity unit; object transforms are identity",
}


def write_new_json(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write("\n")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def xyz_to_unity(value):
    return [round(float(value[0]), 8), round(float(value[2]), 8), round(float(value[1]), 8)]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def subtract(a, b):
    return [x - y for x, y in zip(a, b)]


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def create_probe():
    # Invocation requires --factory-startup and background mode. Removing that
    # process's default cube/camera/light never touches another Blender session.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    mesh = bpy.data.meshes.new(MESH_NAME)
    vertices = [
        (-0.6, -0.4, 0.0), (0.8, -0.4, 0.0), (0.8, 0.55, 0.0), (-0.6, 0.55, 0.0),
        (-0.45, -0.25, 0.72), (0.55, -0.25, 0.96), (0.75, 0.4, 1.21), (-0.45, 0.4, 0.91),
    ]
    # Twelve explicit triangles avoid any implicit non-planar-quad triangulation
    # decision. Every face is author-specified flat shading for this tiny probe.
    faces = [
        (0, 3, 2), (0, 2, 1), (0, 1, 5), (0, 5, 4),
        (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6),
        (3, 0, 4), (3, 4, 7), (4, 5, 6), (4, 6, 7),
    ]
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(OBJECT_NAME, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for name, color in zip(MATERIAL_NAMES, ((0.16, 0.42, 0.65, 1), (0.95, 0.31, 0.08, 1))):
        material = bpy.data.materials.new(name)
        material.diffuse_color = color
        material.use_nodes = True
        principled = material.node_tree.nodes.get("Principled BSDF")
        check(principled is not None, "Default Principled material node is unavailable.")
        principled.inputs["Base Color"].default_value = color
        material.node_tree.nodes.active = principled
        mesh.materials.append(material)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for face in mesh.polygons:
        face.use_smooth = False
        face.material_index = 1 if face.index % 4 == 0 else 0
        for loop_index, uv in zip(face.loop_indices, ((0.1, 0.1), (0.9, 0.1), (0.1, 0.9))):
            uv_layer.data[loop_index].uv = uv
    mesh.update()
    return obj


def export_data(obj):
    check(obj is not None and obj.type == "MESH", "The authored probe object is missing.")
    check(len(bpy.context.scene.objects) == 1, "The probe scene must contain exactly one object.")
    check(obj.name == OBJECT_NAME and obj.data.name == MESH_NAME, "Probe identity changed.")
    check(len(obj.data.vertices) == 8 and len(obj.data.polygons) == 12, "Probe topology changed.")
    check([slot.material.name for slot in obj.material_slots] == list(MATERIAL_NAMES), "Two material slots were not preserved in order.")
    check(all(abs(obj.matrix_world[i][j] - (1 if i == j else 0)) < 1e-7 for i in range(4) for j in range(4)), "Probe transforms must remain identity.")
    mesh = obj.data
    check(len(mesh.uv_layers) == 1 and mesh.uv_layers.active is not None, "The authored UV layer is missing.")
    mesh.calc_loop_triangles()
    if hasattr(mesh, "corner_normals"):
        authored_normals = [tuple(normal.vector) for normal in mesh.corner_normals]
    elif hasattr(mesh, "calc_normals_split"):
        mesh.calc_normals_split()
        authored_normals = [tuple(loop.normal) for loop in mesh.loops]
    else:
        raise RuntimeError("This Blender version does not expose authored corner normals.")
    check(len(authored_normals) == len(mesh.loops), "Authored corner normal count differs.")
    positions, normals, uv0 = [], [], []
    indices = [[], []]
    corners = {}
    for triangle in mesh.loop_triangles:
        slot = triangle.material_index
        check(slot in (0, 1), "Unexpected material index.")
        for ordinal in (0, 2, 1):
            loop_index = triangle.loops[ordinal]
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index]
            position = xyz_to_unity(vertex.co)
            normal = xyz_to_unity(authored_normals[loop_index])
            uv = [round(float(x), 8) for x in mesh.uv_layers.active.data[loop_index].uv]
            key = tuple(position + normal + uv)
            if key not in corners:
                corners[key] = len(positions)
                positions.append(position)
                normals.append(normal)
                uv0.append(uv)
            indices[slot].append(corners[key])
    result = {
        "schema": SCHEMA,
        "scope": SCOPE,
        "object": OBJECT_NAME,
        "coordinate_system": COORDINATES,
        "materials": list(MATERIAL_NAMES),
        "positions": positions,
        "normals": normals,
        "uv0": uv0,
        "submesh_indices": indices,
        "normal_source": "authored per-corner Blender normals, mapped without recalculation in the exporter",
    }
    validate_data(result)
    return result


def validate_data(data):
    vertices = data["positions"]
    check(0 < len(vertices) <= 36, "Unexpected exported vertex count.")
    check(len(data["normals"]) == len(vertices) == len(data["uv0"]), "Per-corner attributes differ in length.")
    check(all(math.isfinite(x) for key in ("positions", "normals", "uv0") for row in data[key] for x in row), "Non-finite geometry attribute.")
    check(all(abs(dot(normal, normal) - 1) < 1e-5 for normal in data["normals"]), "Author normals are not unit length.")
    check(len(data["submesh_indices"]) == 2, "Exactly two submeshes are required.")
    count = 0
    for indices in data["submesh_indices"]:
        check(len(indices) > 0 and len(indices) % 3 == 0, "A submesh is empty or malformed.")
        check(all(isinstance(index, int) and 0 <= index < len(vertices) for index in indices), "A triangle index is invalid.")
        for offset in range(0, len(indices), 3):
            a, b, c = indices[offset:offset + 3]
            face_normal = cross(subtract(vertices[b], vertices[a]), subtract(vertices[c], vertices[a]))
            length = math.sqrt(dot(face_normal, face_normal))
            check(length > 1e-8, "Zero-area triangle.")
            check(all(dot(face_normal, data["normals"][index]) / length > 0.999 for index in (a, b, c)), "Axis/winding conversion disagrees with author normals.")
            count += 1
    check(count == 12, "Exported triangle count changed.")
    check(xyz_to_unity((1, 0, 0)) == [1, 0, 0] and xyz_to_unity((0, 1, 0)) == [0, 0, 1]
          and xyz_to_unity((0, 0, 1)) == [0, 1, 0], "Axis conversion basis check failed.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="New, nonexistent output directory; parent must exist.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    check(bpy.app.background and "--factory-startup" in sys.argv, "Use a background --factory-startup process only.")
    requested = args.output.expanduser()
    check(requested.is_absolute(), "--output must be an absolute path.")
    check(not requested.exists() and not requested.is_symlink(), "Refusing to overwrite an existing output path.")
    output = requested.resolve()
    check(output.parent.is_dir(), "The output parent directory must already exist.")
    output.mkdir(mode=0o700, exist_ok=False)
    verification_path = output / "verification.json"
    try:
        before = export_data(create_probe())
        mesh_path = output / "mesh-unity.json"
        blend_path = output / "probe.blend"
        write_new_json(mesh_path, before)
        check(not blend_path.exists(), "Refusing to overwrite probe.blend.")
        result = bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), check_existing=True, compress=False)
        check("FINISHED" in result and blend_path.is_file(), "Editable blend save failed.")
        result = bpy.ops.wm.open_mainfile(filepath=str(blend_path), load_ui=False, use_scripts=False)
        check("FINISHED" in result, "Reopening the authored blend failed.")
        after = export_data(bpy.data.objects.get(OBJECT_NAME))
        check(before == after, "Reopened blend data differs from the authored export.")
        check(json.loads(mesh_path.read_text(encoding="utf-8")) == after, "Saved JSON differs from reopened author data.")
        verification = {
            "success": True,
            "scope": SCOPE,
            "blender_version": bpy.app.version_string,
            "object": OBJECT_NAME,
            "object_count": len(bpy.context.scene.objects),
            "source_vertices": 8,
            "source_triangles": 12,
            "exported_vertices": len(after["positions"]),
            "submesh_index_counts": [len(indices) for indices in after["submesh_indices"]],
            "materials": list(MATERIAL_NAMES),
            "reopened_blend_matches_export": True,
            "axis_and_winding_checked": True,
            "authored_normals_preserved": True,
            "unity_execution": "not performed",
            "game_installation": "not performed",
            "coordinate_system": COORDINATES,
            "files": {"probe.blend": digest(blend_path), "mesh-unity.json": digest(mesh_path)},
        }
        write_new_json(verification_path, verification)
        print("CONNECTION_PROBE " + json.dumps(verification, ensure_ascii=False, sort_keys=True))
    except Exception as error:
        if not verification_path.exists():
            write_new_json(verification_path, {"success": False, "scope": SCOPE, "error": str(error)})
        raise


if __name__ == "__main__":
    main()
