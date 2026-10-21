"""Add owned forelamp supports to both four-state ships, using native fit refs.

Native reference meshes are PRIVATE studio objects, never body/export objects.
No native mesh, transform, light, collider, save or running game is changed.
"""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


STATES = ("Sturdy", "Ok", "Poor", "Critical")
PARENTS = {
    "cthulhu": "5e13bf19bbf7399da954b976442cdaab3f34c7cfab6543413fee9ed443b4891a",
    "cyberpunk": "bf52a0ec63eef910f053a44b922eaa4677432c96cd0a54064761985eeafaae93",
}
BUNDLE_SHA = "1fd2581e26ada52344d6ed507a16b4162bf9a5110db4f1915260041f27cf3d78"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_helpers():
    path = Path(__file__).with_name("boat5_ocean_trawler_refine.py")
    spec = importlib.util.spec_from_file_location("mount_refine", path)
    ref = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ref)
    return ref


def snapshot(collection):
    return {obj.name: {"vertices": [list(v.co) for v in obj.data.vertices],
                       "matrix": [list(r) for r in obj.matrix_world],
                       "materials": [m.name for m in obj.data.materials]}
            for obj in collection.objects}


def native_reference(ref, data, scenes):
    collection = bpy.data.collections.new("NATIVE_ForeLamp_REFERENCE_NOT_EXPORT")
    collection["exclude_from_runtime_export"] = True
    for scene in scenes:
        scene.collection.children.link(collection)
    metal = ref.finish_material("NATIVE_REFERENCE_Gray", (.12, .14, .13))
    glass = ref.finish_material("NATIVE_REFERENCE_Lens", (.32, .25, .12), glow=.3)
    for row in data["reference_objects"]:
        vertices = [ref.geo.vec(p) for p in row["positions_boat5_local"]]
        faces = []
        for indices in row["indices_by_submesh"]:
            faces.extend((indices[i], indices[i + 2], indices[i + 1]) for i in range(0, len(indices), 3))
        mesh = bpy.data.meshes.new("PRIVATE_" + row["role"])
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        mesh.materials.append(metal)
        mesh.materials.append(glass)
        normals = [ref.geo.vec(n) for n in row["normals_boat5_local"]]
        for face in mesh.polygons:
            face.use_smooth = True
            if "forelamp" in row["role"]:
                center = sum((vertices[i] for i in face.vertices), Vector()) / len(face.vertices)
                if center.y > row["bounds_boat5_local"]["max"][2] - .025:
                    face.material_index = 1
        mesh.normals_split_custom_set([normals[loop.vertex_index] for loop in mesh.loops])
        obj = bpy.data.objects.new("PRIVATE_NATIVE_" + row["role"], mesh)
        obj["exclude_from_runtime_export"] = True
        # Native advanced models are additive, not replacements for Light1.
        obj["native_lamp_mode"] = "enhanced" if "enhanced" in row["role"] else "both"
        collection.objects.link(obj)
    # Replace only approximate forelamp display dummies; geometry stays intact.
    for name in ("GUIDE_ForeLamp_NotExport", "GUIDE_ForeLamp_Bracket_NotExport"):
        bpy.data.objects[name].hide_render = True
    return collection


def reference_mode(collection, mode):
    for obj in collection.objects:
        obj.hide_render = obj["native_lamp_mode"] not in ("both", mode)


def create_mount(ref, collection, style, state):
    ref.geo.SHIP = collection
    metal = "cult_bronze" if style == "cthulhu" else "CYBER_PREVIEW_cy_graphite"
    trim = "cult_bone" if style == "cthulhu" else "CYBER_PREVIEW_cy_edge"
    ref.PALETTE["mount_metal"] = bpy.data.materials[metal]
    ref.PALETTE["mount_trim"] = bpy.data.materials[trim]
    existing = set(collection.objects)
    volumes = []

    def box(name, lo, hi, color="mount_metal", bevel=.003):
        obj = ref.detail_box(state + "__ForeLamp_" + name, lo, hi, color, bevel)
        volumes.append({"name": name, "min": lo, "max": hi})
        return obj

    def rod(name, a, b, radius, color="mount_metal"):
        return ref.detail_rod(state + "__ForeLamp_" + name, a, b, radius, color, sides=6)

    # A real exported beam overlaps the original authored cabin front, above
    # the cult eye and below the windscreen, then reaches the actual lamp feet.
    box("Cabin_Socket", (-.115, 1.087, 1.388), (.115, 1.157, 1.468))
    box("Cantilever", (-.052, 1.100, 1.30), (.052, 1.145, 1.846))
    box("Rear_Crosshead", (-.177, 1.080, 1.800), (.177, 1.114, 1.848))
    for sign, side in ((-1, "Port"), (1, "Starboard")):
        x = sign * .153
        box("Basic_Foot_" + side, (min(sign * .060, sign * .148), 1.078, 1.826),
            (max(sign * .060, sign * .148), 1.109, 1.925), "mount_trim")
        rod("Upright_" + side, (x, .875, 1.820), (x, 1.130, 1.820), .024, "mount_trim")
        rod("Bow_Brace_" + side, (sign * .13, .747, 1.730), (x, .883, 1.825), .024)
        box("Deck_Heel_" + side, (sign * .13 - .045, .727, 1.690),
            (sign * .13 + .045, .780, 1.775), "mount_trim")
        rod("Cabin_Diagonal_" + side, (sign * .10, 1.091, 1.35), (x, .885, 1.82), .018)
    # Enhanced front lights have two heights. Support the lower body and the
    # rear edge of the upper body too; don't put a plate across either lens.
    box("Enhanced_Lower_Seat", (-.147, .865, 1.822), (.147, .896, 1.948))
    box("Enhanced_Upper_Seat", (-.045, 1.145, 1.817), (.045, 1.182, 1.859), "mount_trim")
    if style == "cthulhu":
        for sign, side in ((-1, "Port"), (1, "Starboard")):
            rod("Bone_Sweep_" + side, (sign * .108, 1.12, 1.39),
                (sign * .153, 1.12, 1.795), .014, "mount_trim")
    else:
        box("Armour_Cap", (-.070, 1.145, 1.370), (.070, 1.160, 1.765), "mount_trim")
    added = sorted(set(collection.objects) - existing, key=lambda obj: obj.name)
    for obj in added:
        obj["source_object"] = obj.name.removeprefix(state + "__")
        obj["damage_state"] = state
        obj["forelamp_mount_owned_geometry"] = True
    return added, volumes


def contact_check(data, volumes):
    result = []
    for row in data["reference_objects"]:
        if not row["role"].startswith("forelamp"):
            continue
        floor = row["bounds_boat5_local"]["min"][1]
        points = [p for p in row["positions_boat5_local"] if p[1] <= floor + 1e-5]
        contacts = []
        for point in points:
            for volume in volumes:
                if all(lo <= value <= hi for value, lo, hi in zip(point, volume["min"], volume["max"])):
                    contacts.append({"point": point, "support": volume["name"]})
        if not contacts:
            raise ValueError("No actual native foot vertex contacts owned support: " + row["role"])
        result.append({"role": row["role"], "bottom_y": floor, "contacts": contacts})
    if len(result) != 3:
        raise ValueError("Expected basic and both enhanced front lamps")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--style", choices=PARENTS, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    if sha(args.source) != PARENTS[args.style] or args.source.is_symlink() or args.reference.is_symlink():
        raise ValueError("Expected the accepted original four-state art source and native reference")
    if not args.output.is_absolute() or args.output.exists() or args.output.is_symlink():
        raise ValueError("Use a new absolute output directory")
    native = json.loads(args.reference.read_text())
    if native["source_bundle_sha256"] != BUNDLE_SHA or not native["original_resources_unchanged"]:
        raise ValueError("Native reference version differs")
    reference_hash, script_hash = sha(args.reference), sha(Path(__file__))
    args.output.mkdir(mode=0o700)
    bpy.ops.wm.open_mainfile(filepath=str(args.source), use_scripts=False)
    ref = load_helpers()
    ref.geo.BODY = bpy.data.materials["CULT_cabin" if args.style == "cthulhu" else "CYBER_PREVIEW_cy_white"]
    ref.geo.GLASS = bpy.data.materials["CULT_glass" if args.style == "cthulhu" else "CYBER_PREVIEW_cy_glass"]
    scenes = [bpy.data.scenes["%02d_%s" % (i + 1, name)] for i, name in enumerate(STATES)]
    collections = [bpy.data.collections["%02d_%s_BODY_AND_GLASS" % (i + 1, name.upper())] for i, name in enumerate(STATES)]
    originals = [snapshot(collection) for collection in collections]
    guides = snapshot(bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"])
    references = native_reference(ref, native, scenes)
    checks = []
    support_snapshots = []
    for state, scene, collection, baseline in zip(STATES, scenes, collections, originals):
        bpy.context.window.scene = scene
        added, volumes = create_mount(ref, collection, args.style, state)
        bpy.context.view_layer.update()
        contacts = contact_check(native, volumes)
        # Prove the back of the new socket actually intersects the cabin face.
        cabin = next(o for o in collection.objects if o.get("source_object") == "Wheelhouse_Lower_Skirt")
        tree = BVHTree.FromObject(cabin, bpy.context.evaluated_depsgraph_get())
        point, normal, face, distance = tree.ray_cast(ref.geo.vec((0, 1.12, 1.27)), ref.geo.vec((0, 0, 1)), .2)
        socket = next(v for v in volumes if v["name"] == "Cabin_Socket")
        if point is None or not all(lo <= v <= hi for v, lo, hi in
                                    zip((point.x, point.z, point.y), socket["min"], socket["max"])):
            raise ValueError("New socket misses authored cabin face: " + str(point))
        final = snapshot(collection)
        assert all(final[name] == record for name, record in baseline.items())
        support_snapshots.append({o["source_object"]: {"vertices": [list(v.co) for v in o.data.vertices],
                                                     "matrix": [list(r) for r in o.matrix_world]} for o in added})
        checks.append({"state": state, "new_owned_objects": len(added), "lamp_contacts": contacts,
                       "cabin_surface_intersection_unity": [point.x, point.z, point.y],
                       "old_body_unchanged": True})
        scene.render.engine = "BLENDER_EEVEE"
    assert all(item == support_snapshots[0] for item in support_snapshots)
    assert snapshot(bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"]) == guides
    camera = bpy.data.objects.new("03_ForeLamp_Closeup", bpy.data.cameras.new("ForeLamp_Closeup"))
    camera.location = ref.geo.vec((3.2, 1.65, 4.8))
    camera.rotation_euler = (ref.geo.vec((0, 1.0, 1.66)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.7
    for scene in scenes:
        scene.collection.objects.link(camera)
    reference_mode(references, "basic")
    bpy.context.window.scene = scenes[0]
    blend = args.output / ("boat5_%s_forelamp_v01.blend" % args.style)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    renders = []
    for mode in ("basic", "enhanced"):
        reference_mode(references, mode)
        scene = scenes[0]
        scene.camera = camera
        scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
        scene.render.resolution_percentage = 100
        scene.render.filepath = str(args.output / ("ForeLamp_" + mode + ".png"))
        bpy.ops.render.render(write_still=True)
        renders.append(Path(scene.render.filepath).name)
    assert sha(args.source) == PARENTS[args.style] and sha(args.reference) == reference_hash and sha(Path(__file__)) == script_hash
    manifest = {"style": args.style, "blend": blend.name, "blend_sha256": sha(blend),
                "source_sha256": PARENTS[args.style], "native_reference_sha256": reference_hash,
                "script_sha256": script_hash, "states": checks,
                "same_owned_support_in_all_states": True, "old_guides_geometry_unchanged": True,
                "native_reference_collection": references.name, "native_reference_runtime_export": False,
                "renders": renders, "game_launched": False, "save_access": False}
    (args.output / "forelamp-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("FORELAMP_OK " + json.dumps({key: manifest[key] for key in ("style", "blend_sha256")}))


if __name__ == "__main__":
    main()
