"""Export owned, approved four-state Blender art to a bounded two-slot resource.

Headless Blender only; never opens a game, app, save or original game asset.
Evaluated modifiers and split corner normals are baked. X/Z/Y mapping to Unity
has negative determinant, so triangle order is reversed with the mapped normals.
See https://docs.blender.org/api/5.2/bpy.types.Mesh.html .
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Vector


STATES = ("Sturdy", "Ok", "Poor", "Critical")
SOURCE_HASHES = {
    "cthulhu": "5e13bf19bbf7399da954b976442cdaab3f34c7cfab6543413fee9ed443b4891a",
    "cyberpunk": "bf52a0ec63eef910f053a44b922eaa4677432c96cd0a54064761985eeafaae93",
}
FORELAMP_SOURCE_HASHES = {
    "cthulhu": "d41f456f371c4a21dcea2f9329d55ba87c0278a6037599fbaba7e6db60a50542",
    "cyberpunk": "380afe9d378ea74c5a2438a3480e1438b70d25843820b0044d15c5f86a16768e",
}
SIZE, CELL = 64, 8
HULL_UV, ROOF_UV = (.5625, .0625), (.5625, .9375)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def unity(v):
    return Vector((v.x, v.z, v.y))


def paint_uv(obj, mat, style):
    """Only primary hull/roof panels use the original native paint mask."""
    source = obj.get("source_object", "")
    if source in ("Hull_Flared_Chine", "Sheer_Bulwark_and_Cap"):
        if mat.name == ("CULT_hull" if style == "cthulhu" else "CYBER_PREVIEW_cy_graphite"):
            return HULL_UV
    if source == "Roof_Visor" and mat.name == ("CULT_roof" if style == "cthulhu" else "CYBER_PREVIEW_cy_graphite"):
        return ROOF_UV
    return None


def material_colors(mat):
    node = mat.node_tree.nodes.get("Principled BSDF") if mat.use_nodes else None
    if node is None:
        raise ValueError("Expected an owned Principled material: " + mat.name)
    color = tuple(node.inputs["Base Color"].default_value[:3])
    ramps = [n for n in mat.node_tree.nodes if n.type == "VALTORGB"]
    if ramps:
        elements = ramps[0].color_ramp.elements
        color = tuple((elements[0].color[i] + elements[-1].color[i]) / 2 for i in range(3))
    glow_color = tuple(node.inputs["Emission Color"].default_value[:3])
    strength = node.inputs["Emission Strength"].default_value
    emission = tuple(v * strength / 4 for v in glow_color)
    if mat.name in ("CULT_glass", "CYBER_PREVIEW_cy_glass"):
        # Warm windows, coloured ornaments; all follow the same native switch.
        emission = (.16, .115, .04)
    if not all(math.isfinite(x) and 0 <= x <= 1 for x in color + emission):
        raise ValueError("Atlas colour outside bounded RGB: " + mat.name)
    return color, emission


def srgb(value):
    return round(255 * (12.92 * value if value <= .0031308 else 1.055 * value ** (1 / 2.4) - .055))


def atlas(materials):
    if not 0 < len(materials) <= 64:
        raise ValueError("Atlas must have 1..64 colour cells")
    images = [bytearray((0, 0, 0, 255) * SIZE * SIZE) for _ in range(2)]
    records = {}
    for index, mat in enumerate(materials):
        x, y = index % 8, index // 8
        colors = material_colors(mat)
        for image, color in zip(images, colors):
            rgba = bytes([srgb(v) for v in color] + [255])
            for py in range(y * CELL, (y + 1) * CELL):
                start = (py * SIZE + x * CELL) * 4
                image[start:start + CELL * 4] = rgba * CELL
        records[mat.name] = {"uv": ((x + .5) / 8, (y + .5) / 8),
                             "albedo_linear": colors[0], "emission_linear": colors[1]}
    return images, records


def snapshot(guides):
    return {obj.name: ([list(v.co) for v in obj.data.vertices], [list(r) for r in obj.matrix_world])
            for obj in guides.objects}


def export_state(index, style, cells):
    scene = bpy.data.scenes["%02d_%s" % (index + 1, STATES[index])]
    bpy.context.window.scene = scene
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    collection = bpy.data.collections["%02d_%s_BODY_AND_GLASS" % (index + 1, STATES[index].upper())]
    vertices, groups = [], [[], []]
    objects, skipped, repaired = [], 0, []
    backfaces = 0
    min_alignment = 1.0
    for obj in sorted(collection.objects, key=lambda obj: obj.name):
        if obj.type != "MESH" or obj.get("exclude_from_runtime_export"):
            raise ValueError("Unexpected body object: " + obj.name)
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        try:
            mesh.calc_loop_triangles()
            edge_uses = {}
            for polygon in mesh.polygons:
                for edge in polygon.edge_keys:
                    edge = tuple(sorted(edge))
                    edge_uses[edge] = edge_uses.get(edge, 0) + 1
            # Owned preview panels are deliberately thin and were viewed from
            # both sides in Blender. Original game shaders use Cull Back, so
            # retain their appearance without changing the shared shader by
            # exporting a second face (with reversed normal) for open meshes.
            thin = any(uses == 1 for uses in edge_uses.values())
            normal_matrix = evaluated.matrix_world.to_3x3().inverted().transposed()
            before = len(vertices)
            for triangle in mesh.loop_triangles:
                material = mesh.materials[triangle.material_index]
                uv = paint_uv(obj, material, style)
                slot = 0 if uv is not None else 1
                if uv is None:
                    uv = cells[material.name]["uv"]
                rows = []
                # Swap two corners after the handedness conversion. We never
                # recalculate artist normals in Unity or mutate source meshes.
                for loop_index in (triangle.loops[0], triangle.loops[2], triangle.loops[1]):
                    p = unity(evaluated.matrix_world @ mesh.vertices[mesh.loops[loop_index].vertex_index].co)
                    n = unity(normal_matrix @ mesh.corner_normals[loop_index].vector).normalized()
                    # Validate exactly the float32 values that will be uploaded.
                    rows.append(struct.unpack("<8f", struct.pack("<8f", *p, *n, *uv)))
                a, b, c = (Vector(row[:3]) for row in rows)
                cross = (b - a).cross(c - a)
                if cross.length_squared <= 1e-20:
                    skipped += 1
                    continue
                mean = sum((Vector(row[3:6]) for row in rows), Vector()).normalized()
                alignment = cross.normalized().dot(mean)
                if alignment <= 0:
                    # Some curved ornaments have strongly nonplanar quads.
                    # Their tessellation contains locally reversed triangles
                    # although the source polygon/corner normals face outward.
                    # Normalize only these triangle windings to those artist
                    # normals. Keep every position and corner normal intact.
                    # This also prevents one-sided Unity culling from exposing
                    # holes that Blender's two-sided preview had concealed.
                    if mesh.polygons[triangle.polygon_index].use_smooth:
                        raise ValueError("Unexpected smooth normal/winding mismatch: " + obj.name)
                    rows[1], rows[2] = rows[2], rows[1]
                    alignment = -alignment
                    repaired.append({"object": obj.name, "triangle": triangle.index,
                                     "polygon": triangle.polygon_index})
                min_alignment = min(min_alignment, alignment)
                start = len(vertices)
                vertices.extend(rows)
                groups[slot].extend((start, start + 1, start + 2))
                if thin:
                    start = len(vertices)
                    vertices.extend(tuple(row[:3]) + tuple(-n for n in row[3:6]) + tuple(row[6:])
                                    for row in (rows[0], rows[2], rows[1]))
                    groups[slot].extend((start, start + 1, start + 2))
                    backfaces += 1
            objects.append({"name": obj.name, "vertices": len(vertices) - before, "thin_panel_backfaces": thin})
        finally:
            evaluated.to_mesh_clear()
    if not vertices or len(vertices) >= 65535 or not all(groups):
        raise ValueError("Unsupported vertex/submesh count")
    if not all(math.isfinite(x) for row in vertices for x in row):
        raise ValueError("Nonfinite runtime data")
    lower = [min(v[i] for v in vertices) for i in range(3)]
    upper = [max(v[i] for v in vertices) for i in range(3)]
    if not all(-2 < x < 3.5 for x in lower + upper):
        raise ValueError("Boat5 coordinate envelope exceeded")
    report = {"state": STATES[index], "vertices": len(vertices), "triangles": len(vertices) // 3,
              "triangles_per_slot": [len(g) // 3 for g in groups], "bounds_min": lower,
              "bounds_max": upper, "objects": objects, "skipped_degenerate_evaluated_triangles": skipped,
              "minimum_normal_winding_alignment": min_alignment,
              "nonplanar_polygon_winding_repairs": repaired,
              "thin_panel_added_backface_triangles": backfaces}
    return vertices, groups, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--style", choices=SOURCE_HASHES, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    source_hash = digest(args.source)
    if source_hash not in (SOURCE_HASHES[args.style], FORELAMP_SOURCE_HASHES[args.style]):
        raise ValueError("Approved four-state source fingerprint mismatch")
    if args.source.is_symlink() or not args.output.is_absolute() or args.output.exists() or args.output.is_symlink():
        raise ValueError("Expected unlinked source and a new absolute output directory")
    args.output.mkdir(mode=0o700)
    script_hash = digest(Path(__file__))
    bpy.ops.wm.open_mainfile(filepath=str(args.source), use_scripts=False)
    guides = bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"]
    baseline = snapshot(guides)
    references = bpy.data.collections.get("NATIVE_ForeLamp_REFERENCE_NOT_EXPORT")
    reference_meshes = {obj.data for obj in references.objects if obj.type == "MESH"} if references else set()
    used = {}
    for i, state in enumerate(STATES):
        collection = bpy.data.collections["%02d_%s_BODY_AND_GLASS" % (i + 1, state.upper())]
        for obj in collection.objects:
            if obj.get("exclude_from_runtime_export") or obj.data in reference_meshes:
                raise ValueError("Private reference must never enter an exported body: " + obj.name)
            for mat in obj.data.materials:
                if mat is not None and paint_uv(obj, mat, args.style) is None:
                    used[mat.name] = mat
    images, cells = atlas([used[key] for key in sorted(used)])
    path = args.output / (args.style + "-v1.bin")
    reports = []
    with path.open("xb") as stream:
        stream.write(b"JBSKIN01")
        stream.write(struct.pack("<2i", SIZE, SIZE))
        for image in images:
            stream.write(image)
        stream.write(struct.pack("<i", 4))
        for i in range(4):
            vertices, groups, report = export_state(i, args.style, cells)
            reports.append(report)
            stream.write(struct.pack("<i", len(vertices)))
            for vertex in vertices:
                stream.write(struct.pack("<8f", *vertex))
            for group in groups:
                stream.write(struct.pack("<i", len(group)))
                stream.write(struct.pack("<%di" % len(group), *group))
    if snapshot(guides) != baseline or digest(args.source) != source_hash or digest(Path(__file__)) != script_hash:
        raise ValueError("Source/guide/exporter changed during export")
    report = {"schema": 1, "style": args.style, "resource": path.name, "resource_sha256": digest(path),
              "resource_bytes": path.stat().st_size, "source_sha256": source_hash,
              "exporter_sha256": script_hash, "blender": bpy.app.version_string,
              "atlas": {"width": SIZE, "height": SIZE, "format": "RGBA32 sRGB bottom-up, point clamp no mipmaps",
                        "cells": cells}, "states": reports, "guides_excluded_and_unchanged": len(baseline),
              "game_launched": False, "game_save_access": False}
    report["private_native_reference_meshes_excluded"] = len(reference_meshes)
    (args.output / "export-manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print("EXPORT_OK " + json.dumps({k: report[k] for k in ("style", "resource_sha256", "resource_bytes")}))


if __name__ == "__main__":
    main()
