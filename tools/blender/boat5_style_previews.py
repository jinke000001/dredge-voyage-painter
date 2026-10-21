"""Render independent Cthulhu/cyberpunk ship style proposals from refined-v02.

Blender --background --factory-startup --disable-autoexec --python-exit-code 1
--python this_file.py -- --style cthulhu --source /refined_v02.blend
--output /absolute/new/folder
Only the owned Blender source and a fresh art output directory are accessed.
"""

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


ROOT=Path(__file__).parent


def module(name):
    spec=importlib.util.spec_from_file_location(name,ROOT/(name+".py"))
    loaded=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


def snapshot(objects):
    return {o.name:{"vertices":[tuple(v.co) for v in o.data.vertices],
                    "matrix":[tuple(row) for row in o.matrix_world]}
            for o in objects}


def assert_preserved(before):
    for name,record in before.items():
        obj=bpy.data.objects[name]
        assert record["vertices"]==[tuple(v.co) for v in obj.data.vertices],name
        assert record["matrix"]==[tuple(row) for row in obj.matrix_world],name


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--style",choices=("cthulhu","cyberpunk"),required=True)
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if not bpy.app.background or "--factory-startup" not in sys.argv:
        raise RuntimeError("An independent background factory-startup process is required.")
    source=args.source
    if not source.is_absolute() or source.is_symlink() or source.name!="boat5_ocean_trawler_refined_v02.blend":
        raise RuntimeError("Use the owned refined-v02 Blender source explicitly.")
    source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
    reference=json.loads(source.with_name("refinement-manifest.json").read_text())
    if source_hash!=reference["blend_sha256"]:
        raise RuntimeError("Refined source hash mismatch.")
    output=args.output
    if not output.is_absolute() or output.exists() or output.is_symlink() or not output.parent.is_dir():
        raise RuntimeError("A nonexistent absolute output directory with existing parent is required.")
    output.mkdir(mode=0o700)
    bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
    ref=module("boat5_ocean_trawler_refine")
    ref.geo.SHIP=bpy.data.collections["01_SHIP_REFINED_BODY_AND_GLASS"]
    ref.geo.GUIDES=bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"]
    ref.geo.STUDIO=bpy.data.collections["03_PRESENTATION_NOT_EXPORT"]
    ref.geo.STUDIO.hide_viewport=False
    bpy.context.view_layer.update()
    base=snapshot(ref.geo.SHIP.objects)
    guides=snapshot(ref.geo.GUIDES.objects)
    ref.geo.BODY=bpy.data.materials["PREVIEW_cabin"]
    ref.geo.GLASS=bpy.data.materials["PREVIEW_glass"]
    ref.geo.GUIDE_MAT=bpy.data.materials["GUIDES_Original_Gear_Dummies"]
    style=module("boat5_style_"+args.style)
    design=style.build(ref)
    bpy.context.view_layer.update()
    assert_preserved(base)
    assert_preserved(guides)
    for obj in ref.geo.SHIP.objects:
        assert obj.get("runtime_semantic_slot") in {0,1},obj.name
        assert all(math.isfinite(x) for v in obj.data.vertices for x in v.co),obj.name
    assert ref.geo.GUIDES.get("exclude_from_runtime_export")

    scene=bpy.context.scene
    scene["title"]=design["display_name"]+" / Blender Style Proposal v01"
    scene["scope"]="Style proposal only; not game integration, gameplay, or accessory fit proof."
    scene["material_warning"]="Custom preview materials/emission are not the native two-material runtime contract."
    scene["source_blend_sha256"]=source_hash
    scene.cycles.samples=32
    scene.render.resolution_percentage=100
    camera_specs=[
        ("01_Hero_Front_Quarter",6.35),
        ("04_Aft_Quarter",6.35),
        ("02_Starboard_Profile",5.6),
        # A base-only diagnostic also needs >=6.223 to include the full flagpole
        # at this high aft angle. Both proposals use the same 6.35 framing.
        ("07_Small_Scale_Readability",6.35),
    ]
    cameras=[]
    for name,scale in camera_specs:
        camera=bpy.data.objects[name]
        camera.data.ortho_scale=scale
        camera["body_only"]=False
        cameras.append(camera)
    framing=ref.frame_cameras(scene,cameras)
    assert all(abs(camera.data.ortho_scale-scale)<1e-5
               for camera,(_,scale) in zip(cameras,camera_specs)),json.dumps({
                   "error":"Style exceeds shared framing",
                   "actual_scales":{c.name:c.data.ortho_scale for c in cameras}})
    scene.camera=cameras[0]
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    ref.geo.STUDIO.hide_viewport=True
    ref.geo.GUIDES.hide_render=False
    scene["source_body_geometry_and_transforms_preserved"]=True
    scene["source_gear_guide_geometry_and_transforms_preserved"]=True
    blend=output/("boat5_"+args.style+"_style_v01.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=True)
    renders=[]
    for camera,name in zip(cameras,["01_Hero_Day","02_Aft_Day","03_Side_Day","05_Small_Scale"]):
        scene.camera=camera
        scene.render.resolution_x=camera["render_width"]
        scene.render.resolution_y=camera["render_height"]
        scene.render.filepath=str(output/(name+".png"))
        bpy.ops.render.render(write_still=True)
        renders.append({"file":name+".png","lighting":"day","camera":camera.name})
        print("STYLE_RENDER "+json.dumps(renders[-1]),flush=True)
    # Twilight is a Blender lighting study to expose emission. It is explicitly
    # not the game's night rendering or an installed shader/material result.
    scene.camera=cameras[0]
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    bg=scene.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value=(.16,.23,.31,1)
    bg.inputs["Strength"].default_value=.08
    for obj in ref.geo.STUDIO.objects:
        if obj.type=="LIGHT":
            obj.data.energy*=.16
            obj.data.color=(.62,.76,1.0) if obj.name=="Fill" else (.87,.91,1.0)
    scene.render.filepath=str(output/"04_Hero_Twilight.png")
    bpy.ops.render.render(write_still=True)
    renders.append({"file":"04_Hero_Twilight.png","lighting":"Blender twilight study","camera":scene.camera.name})
    corners=[o.matrix_world @ Vector(c) for o in ref.geo.SHIP.objects for c in o.bound_box]
    triangles=0
    depsgraph=bpy.context.evaluated_depsgraph_get()
    for obj in ref.geo.SHIP.objects:
        evaluated=obj.evaluated_get(depsgraph)
        mesh=evaluated.to_mesh()
        mesh.calc_loop_triangles()
        triangles+=len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    manifest={
        "style":args.style,"design":design,"blender":bpy.app.version_string,
        "source_blend_sha256":source_hash,"source_body_objects_preserved":len(base),
        "source_guides_preserved":len(guides),"body_objects":len(ref.geo.SHIP.objects),
        "evaluated_body_triangles":triangles,"blend":blend.name,
        "blend_sha256":hashlib.sha256(blend.read_bytes()).hexdigest(),
        "source_scripts_sha256":{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in
                                 [Path(__file__),ROOT/("boat5_style_"+args.style+".py"),
                                  ROOT/"boat5_ocean_trawler_refine.py",ROOT/"boat5_ocean_trawler_blockout.py"]},
        "blender_bounds_min":[min(p[i] for p in corners) for i in range(3)],
        "blender_bounds_max":[max(p[i] for p in corners) for i in range(3)],
        "renders":renders,"framing":framing,
        "semantic_groups_are_runtime_submeshes":False,
        "custom_material_runtime_support":False,"four_damage_states":False,
        "game_integration":False,"game_or_save_access":False,
        "awaiting_user_decision":"Compare styles and select a direction before production resources or game integration.",
    }
    with (output/"style-manifest.json").open("x",encoding="utf-8") as file:
        json.dump(manifest,file,ensure_ascii=False,indent=2)
    print("STYLE_COMPLETE "+json.dumps(manifest,ensure_ascii=False),flush=True)


if __name__=="__main__":
    main()
