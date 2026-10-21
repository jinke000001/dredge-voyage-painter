"""Round-trip the actual owned runtime payload into a single-sided art preview.

Not the game Shader/GPU. Atlas cells are decoded from the exported RGBA bytes;
native hull/roof paint is represented by the approved Blender colours. Reuses
only the owned cameras, stage and attachment guides from the approved .blend.
"""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Vector


def linear(byte):
    s = byte / 255
    return s / 12.92 if s <= .04045 else ((s + .055) / 1.055) ** 2.4


def material(name, color, emission):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    result.use_backface_culling = True
    tree = result.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = .85
    bsdf.inputs["Specular IOR Level"].default_value = .22
    bsdf.inputs["Emission Color"].default_value = (*emission, 1)
    bsdf.inputs["Emission Strength"].default_value = 4
    # Use Eevee's raster culling, like Unity Cull Back. Simulating this with
    # Cycles transparent backfaces gives ambiguous coincident-ray hits for
    # paired front/back panels and is not a useful raster verification.
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--resource", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--style", choices=("cthulhu", "cyberpunk"), required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    if args.output.exists() or args.output.is_symlink() or not args.output.is_absolute():
        raise ValueError("Use a new absolute output directory")
    args.output.mkdir(mode=0o700)
    source_sha = hashlib.sha256(args.source.read_bytes()).hexdigest()
    binary = args.resource.read_bytes()
    binary_sha = hashlib.sha256(binary).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(args.source), use_scripts=False)
    offset = 0

    def take(fmt):
        nonlocal offset
        result = struct.unpack_from(fmt, binary, offset)
        offset += struct.calcsize(fmt)
        return result

    if binary[:8] != b"JBSKIN01":
        raise ValueError("Bad runtime resource")
    offset = 8
    width, height = take("<2i")
    if (width, height) != (64, 64):
        raise ValueError("Bad atlas")
    size = width * height * 4
    albedo, emission = binary[offset:offset + size], binary[offset + size:offset + size * 2]
    offset += size * 2
    if take("<i")[0] != 4:
        raise ValueError("Bad state count")
    materials = []
    for i in range(64):
        pixel = ((i // 8 * 8 + 4) * width + i % 8 * 8 + 4) * 4
        materials.append(material("RUNTIME_CELL_%02d" % i,
                                  tuple(linear(b) for b in albedo[pixel:pixel + 3]),
                                  tuple(linear(b) for b in emission[pixel:pixel + 3])))
    native = ("CULT_hull", "CULT_roof") if args.style == "cthulhu" else ("CYBER_PREVIEW_cy_graphite",) * 2
    for i, source in enumerate(native):
        mat = bpy.data.materials[source]
        color = mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value[:3]
        ramps = [node for node in mat.node_tree.nodes if node.type == "VALTORGB"]
        if ramps:
            ramp = ramps[0].color_ramp.elements
            color = [(ramp[0].color[j] + ramp[-1].color[j]) / 2 for j in range(3)]
        materials.append(material("PAINT_APPROXIMATION_%d" % i, color, (0, 0, 0)))
    objects = []
    for state, name in enumerate(("Sturdy", "Ok", "Poor", "Critical")):
        scene = bpy.data.scenes["%02d_%s" % (state + 1, name)]
        bpy.context.window.scene = scene
        collection = bpy.data.collections["%02d_%s_BODY_AND_GLASS" % (state + 1, name.upper())]
        scene.collection.children.unlink(collection)
        count = take("<i")[0]
        rows = [take("<8f") for _ in range(count)]
        points = [(v[0], v[2], v[1]) for v in rows]
        normals = [(v[3], v[5], v[4]) for v in rows]
        faces, material_indices = [], []
        for slot in range(2):
            indices = take("<%di" % take("<i")[0])
            for i in range(0, len(indices), 3):
                a, b, c = indices[i:i + 3]
                faces.append((a, c, b))
                u, v = rows[a][6:]
                material_indices.append((64 if v < .5 else 65) if slot == 0 else int(u * 8) + int(v * 8) * 8)
        mesh = bpy.data.meshes.new("DecodedRuntime_" + name)
        mesh.from_pydata(points, [], faces)
        mesh.update()
        mesh.materials.clear()
        for mat in materials:
            mesh.materials.append(mat)
        for polygon, index in zip(mesh.polygons, material_indices):
            polygon.material_index = index
            polygon.use_smooth = True
        mesh.normals_split_custom_set([normals[loop.vertex_index] for loop in mesh.loops])
        obj = bpy.data.objects.new(mesh.name, mesh)
        scene.collection.objects.link(obj)
        objects.append(obj)
        scene.render.engine = "BLENDER_EEVEE"
        scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
        scene.render.resolution_percentage = 100
    if offset != len(binary):
        raise ValueError("Unexpected binary suffix")
    jobs = [(0, "01_Hero_Front_Quarter", "01_Runtime_Sturdy_Hero.png", "basic"),
            (3, "02_Starboard_Profile", "02_Runtime_Critical_Starboard.png", "basic")]
    native_references = bpy.data.collections.get("NATIVE_ForeLamp_REFERENCE_NOT_EXPORT")
    if native_references is not None and "03_ForeLamp_Closeup" in bpy.data.objects:
        jobs.extend([(0, "03_ForeLamp_Closeup", "03_Runtime_Basic_ForeLamp.png", "basic"),
                     (0, "03_ForeLamp_Closeup", "04_Runtime_Enhanced_ForeLamp.png", "enhanced"),
                     (3, "03_ForeLamp_Closeup", "05_Runtime_Critical_Enhanced_ForeLamp.png", "enhanced")])
    for index, camera, filename, light_mode in jobs:
        scene = bpy.data.scenes["%02d_%s" % (index + 1, ("Sturdy", "Ok", "Poor", "Critical")[index])]
        bpy.context.window.scene = scene
        scene.camera = bpy.data.objects[camera]
        scene.camera.data.ortho_scale = 2.7 if camera == "03_ForeLamp_Closeup" else 6.35 if index == 0 else 5.6
        if native_references is not None:
            for obj in native_references.objects:
                obj.hide_render = obj.get("native_lamp_mode") not in ("both", light_mode)
        scene.render.filepath = str(args.output / filename)
        bpy.ops.render.render(write_still=True)
    bpy.context.window.scene = bpy.data.scenes["01_Sturdy"]
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output / "runtime-roundtrip.blend"))
    assert hashlib.sha256(args.source.read_bytes()).hexdigest() == source_sha
    assert hashlib.sha256(args.resource.read_bytes()).hexdigest() == binary_sha
    report = {"source_sha256": source_sha, "resource_sha256": binary_sha,
              "four_states_decoded": True, "single_sided": True,
              "author_corner_normals_reimported": True,
              "renders": [job[2] for job in jobs], "game_render": False,
              "private_native_reference_for_fit_only": native_references is not None,
              "lamp_reference_modes": sorted({job[3] for job in jobs}),
              "native_paint_approximation": True, "native_shader_used": False}
    (args.output / "roundtrip-report.json").write_text(json.dumps(report, indent=2) + "\n")
    print("ROUNDTRIP_OK " + args.style)


if __name__ == "__main__":
    main()
