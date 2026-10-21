"""Refine the approved Boat5 blockout into a standalone Blender art preview.

Run with Blender --background --factory-startup --disable-autoexec
--python-exit-code 1 --python this_file.py -- --source /approved/file.blend
--output /absolute/new/directory. No game files, saves or runtime exports.
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
from bpy_extras.object_utils import world_to_camera_view


TITLE = "Boat5 Ocean Trawler / Refined Art Preview v02"
HELPER_PATH = Path(__file__).with_name("boat5_ocean_trawler_blockout.py")
spec = importlib.util.spec_from_file_location("boat5_blockout", HELPER_PATH)
geo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(geo)
PALETTE = {}


def finish_material(name, dark, light=None, roughness=.85, glow=0):
    mat = geo.material(name, dark, roughness)
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Specular IOR Level"].default_value = .22
    if light:
        # Broad, quiet paint variation. No high-frequency bump or photographic
        # texture: large flat planes should remain readable at game scale.
        noise = mat.node_tree.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 3.2
        noise.inputs["Detail"].default_value = 0
        ramp = mat.node_tree.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = .25
        ramp.color_ramp.elements[0].color = (*dark, 1)
        ramp.color_ramp.elements[1].position = .75
        ramp.color_ramp.elements[1].color = (*light, 1)
        mat.node_tree.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        mat.node_tree.links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
    if glow:
        shader.inputs["Emission Color"].default_value = (*dark, 1)
        shader.inputs["Emission Strength"].default_value = glow
    return mat


def paint(obj, region, slot=0):
    obj.data.materials.clear()
    obj.data.materials.append(PALETTE[region])
    obj["runtime_semantic_slot"] = slot
    obj["finish_region"] = region
    obj["preview_only"] = True
    obj["blockout_only"] = False
    return obj


def detail_box(name, low, high, region, bevel=.004):
    return paint(geo.box(name, low, high, bevel=bevel), region)


def detail_rod(name, a, b, radius, region, sides=8):
    return paint(geo.rod(name, a, b, radius, sides=sides), region)


def panel(name, points, region, slot=0):
    return paint(geo.mesh(name, points, [tuple(range(len(points)))]), region, slot)


def make_palette():
    definitions = {
        "hull": ((.115,.071,.044), (.185,.122,.071)),
        "hull_shadow": ((.061,.047,.037), None),
        "keel": ((.086,.045,.034), (.118,.060,.043)),
        "cabin": ((.51,.49,.40), (.59,.565,.47)),
        "roof": ((.19,.265,.225), (.255,.325,.272)),
        "roof_edge": ((.115,.168,.142), None),
        "trim": ((.335,.283,.190), (.40,.345,.24)),
        "deck": ((.205,.156,.100), (.255,.195,.125)),
        "wood_seam": ((.084,.064,.045), None),
        "iron": ((.045,.058,.052), (.068,.078,.069)),
        "iron_edge": ((.125,.145,.124), None),
        "brass": ((.26,.205,.105), None),
        "glass": ((.035,.075,.077), None),
        "glass_glint": ((.055,.105,.104), None),
        "wear": ((.31,.238,.145), None),
        "old_paint": ((.405,.405,.328), None),
    }
    for name, (dark, light) in definitions.items():
        PALETTE[name] = finish_material("PREVIEW_" + name, dark, light)


def tint_approved_geometry():
    for obj in list(geo.SHIP.objects):
        name = obj.name
        if name.startswith("Glass_"):
            paint(obj, "glass", 1)
        elif name == "Hull_Flared_Chine":
            paint(obj, "hull")
            obj.data.materials.append(PALETTE["hull_shadow"])
            obj.data.materials.append(PALETTE["keel"])
            for face in obj.data.polygons:
                y = sum(obj.data.vertices[i].co.z for i in face.vertices) / len(face.vertices)
                face.material_index = 2 if y < -.10 else (1 if y < .19 else 0)
        elif name == "Working_Deck":
            paint(obj, "deck")
        elif name == "Sheer_Bulwark_and_Cap":
            paint(obj, "hull")
            obj.data.materials.append(PALETTE["trim"])
            for i, face in enumerate(obj.data.polygons):
                # Four faces per rim segment; the top face gets a broad cap.
                face.material_index = 1 if i % 4 == 1 else 0
        elif name.startswith("Rub_Rail_"):
            paint(obj, "iron")
        elif name == "Roof_Visor":
            paint(obj, "roof")
            obj.data.materials.append(PALETTE["roof_edge"])
            for face in obj.data.polygons:
                face.material_index = 0 if face.normal.z > .5 else 1
        elif name.startswith("Wheelhouse_"):
            paint(obj, "cabin")
        elif name == "Aft_Flush_Working_Hatch":
            paint(obj, "roof_edge")
        elif name == "Aft_Access_Door":
            paint(obj, "iron")
        else:
            paint(obj, "iron_edge")


def window_details():
    for obj in [o for o in geo.SHIP.objects if o.name.startswith("Glass_")]:
        points = [obj.matrix_world @ v.co for v in obj.data.vertices]
        normal = obj.data.polygons[0].normal.copy()
        # The authored panes are upright; choose the outward direction explicitly.
        center = sum(points, Vector()) / len(points)
        outward = Vector((center.x, center.y-.75, 0))
        if normal.dot(outward) < 0:
            normal.negate()
        def unity(p):
            return (p.x, p.z, p.y)
        for i in range(4):
            a = points[i] + normal*.003
            b = points[(i+1)%4] + normal*.003
            detail_rod(obj.name+"_Window_Gasket_%d"%i, unity(a), unity(b), .010, "roof_edge", 4)
        # Only three panes carry a quiet graphic streak; repeating a bright
        # polygon in every pane overwhelms the original game's flat window band.
        if obj.name in {"Glass_00_00","Glass_02_01","Glass_06_00"}:
            a,b,c,d = points
            glint = [a.lerp(b,u).lerp(d.lerp(c,u),v)
                     for u,v in [(.08,.68),(.13,.86),(.88,.54),(.85,.42)]]
            panel(obj.name+"_Quiet_Reflection",[unity(p+normal*.0015) for p in glint],"glass_glint",1)
    # Simple large wipers on the two front panes. Geometry follows the original
    # inclined window plane rather than floating in front of the cabin.
    for index in (0,1):
        pane = bpy.data.objects["Glass_00_%02d"%index]
        a,b,c,d = [pane.matrix_world @ v.co for v in pane.data.vertices]
        normal = Vector((0,1,.19)).normalized()
        def point(u,v):
            p = a.lerp(b,u).lerp(d.lerp(c,u),v) + normal*.012
            return (p.x,p.z,p.y)
        detail_rod("Wiper_Arm_%d"%index, point(.5,.05),point(.3,.59),.006,"iron",6)
        detail_rod("Wiper_Blade_%d"%index,point(.22,.34),point(.40,.80),.008,"iron",4)


def cabin_details():
    # Window sill uses the already approved eight-sided cabin footprint.
    waist = [(-.32,1.41),(.32,1.41),(.49,1.20),(.49,.31),(.37,.155),
             (-.37,.155),(-.49,.31),(-.49,1.20)]
    for i,(x,z) in enumerate(waist):
        x2,z2 = waist[(i+1)%8]
        detail_rod("Cabin_Window_Sill_%d"%i,(x*1.012,1.165,z),(x2*1.012,1.165,z2),.018,"trim",4)
    # Aft door inset, hinges and a clear horizontal handle.
    detail_box("Door_Inset",(-.132,.59,.107),(.132,1.07,.121),"roof",.006)
    detail_box("Door_Lower_Kickplate",(-.112,.61,.101),(.112,.72,.108),"iron_edge",.003)
    for i,y in enumerate((.67,.98)):
        detail_box("Door_Hinge_%d"%i,(-.15,y-.025,.098),(-.112,y+.025,.112),"brass",.002)
    detail_rod("Door_Handle",(.045,.84,.089),(.115,.84,.089),.010,"brass")
    for x in (.045,.115):
        detail_rod("Door_Handle_Mount_%.3f"%x,(x,.84,.089),(x,.84,.110),.008,"iron_edge")
    detail_box("Door_Step",(-.20,.51,.035),(.20,.56,.15),"iron",.007)
    # Restrained side vents, each at the actual lower-cabin side surface.
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        detail_box("Side_Vent_Frame_"+label,(sign*.514-.009,.77,.50),
                   (sign*.514+.009,.96,.77),"roof_edge",.003)
        for i in range(4):
            y=.80+i*.042
            detail_box("Side_Vent_Louver_%s_%d"%(label,i),
                       (sign*.527-.006,y,.52),(sign*.527+.006,y+.014,.75),"iron_edge",.002)
        # Door-sized graphic wear, not tiny noise: only three chips per side.
        for i,(y,z,width) in enumerate([(.71,1.13,.035),(.60,.92,.048),(.97,.47,.024)]):
            x=sign*(.53+(.49-.53)*(y-.52)/.65)
            panel("Cabin_Paint_Chip_%s_%d"%(label,i),
                  [(x+sign*.003,y,z),(x+sign*.003,y+.012,z+width),
                   (x+sign*.003,y+.023,z+.8*width),(x+sign*.003,y+.018,z+.006)],"old_paint")
    # Existing smoke supports keep their endpoints. Collars give their base and
    # top a readable silhouette without inventing new functional equipment.
    for i,(x,z,top) in enumerate([(.006,-.40,1.80),(.004,-.013,1.664)]):
        detail_rod("Exhaust_Base_Collar_%d"%i,(x,.49,z),(x,.56,z),.077,"iron",8)
        detail_rod("Exhaust_Top_Band_%d"%i,(x,top-.09,z),(x,top-.055,z),.064,"iron",8)
        detail_rod("Exhaust_Dark_Mouth_%d"%i,(x,top+.001,z),(x,top+.003,z),.045,"iron",8)


def deck_and_hull_details():
    # Aft deck is intentionally clear: no crates, winch or new net equipment.
    for i,x in enumerate([-.50,-.375,-.25,-.125,0,.125,.25,.375,.50]):
        pieces=[(-1.35,.12)] if abs(x)>.30 else [(-1.35,-.93),(-.36,.12)]
        for j,(z0,z1) in enumerate(pieces):
            if abs(x)>.45:
                z0=-1.17
            panel("Deck_Plank_Seam_%d_%d"%(i,j),[(x-.0018,.479,z0),(x+.0018,.479,z0),
                   (x+.0018,.479,z1),(x-.0018,.479,z1)],"wood_seam")
    detail_box("Hatch_Top_Inset",(-.262,.5105,-.882),(.262,.521,-.408),"deck",.003)
    for i,x in enumerate((-.18,.18)):
        detail_box("Hatch_Hinge_%d"%i,(x-.037,.52,-.46),(x+.037,.535,-.40),"brass",.003)
    detail_rod("Hatch_Recess_Handle",(-.065,.530,-.77),(.065,.530,-.77),.009,"iron",6)
    # Narrow shaped rub-rail highlights and scupper insets follow the authored
    # station widths, not a rectangular approximation outside the hull.
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        for index in (1,3,5):
            z0,w0,h0=geo.STATIONS[index]
            z1,w1,h1=geo.STATIONS[index+1]
            t0,t1=.32,.50
            panel("Hull_Rail_Wear_%s_%d"%(label,index),
                  [(sign*(w0+(w1-w0)*t),.265+dy,z0+(z1-z0)*t)
                   for t,dy in [(t0,-.006),(t1,-.002),(t1,.008),(t0,.006)]],"wear")
        for index in (1,2):
            z0,w0,h0=geo.STATIONS[index]
            z1,w1,h1=geo.STATIONS[index+1]
            coords=[]
            for t,dy in [(.35,.025),(.63,.025),(.63,.063),(.35,.063)]:
                coords.append((sign*(w0+(w1-w0)*t+.002),h0+(h1-h0)*t+dy,z0+(z1-z0)*t))
            panel("Bulwark_Scupper_%s_%d"%(label,index),coords,"iron")
        # A single broad worn streak below the sheer; not repetitive rivet rows.
        z0,w0,h0=geo.STATIONS[3]
        z1,w1,h1=geo.STATIONS[4]
        panel("Hull_Worn_Stroke_"+label,
              [(sign*(w0+(w1-w0)*t+.001),h0+(h1-h0)*t-dy,z0+(z1-z0)*t)
               for t,dy in [(.15,.015),(.85,.019),(.73,.037),(.25,.030)]],"wear")


def setup_guides():
    mat=finish_material("GUIDES_Original_Gear_Dummies",(.12,.145,.128))
    for obj in geo.GUIDES.objects:
        obj.data.materials.clear()
        obj.data.materials.append(mat)
        obj["exclude_from_runtime_export"]=True
    flag=bpy.data.objects["GUIDE_Flag_NotExport"]
    flag.data.materials[0]=finish_material("GUIDES_Plain_Flag",(.335,.31,.24))
    geo.GUIDES["purpose"]="Illustrative original-gear occupancy only; not original assets or exact fit validation."


def frame_cameras(scene,cameras):
    bpy.context.view_layer.update()
    result=[]
    for camera in cameras:
        scene.render.resolution_x=camera["render_width"]
        scene.render.resolution_y=camera["render_height"]
        objects=list(geo.SHIP.objects)+([] if camera["body_only"] else list(geo.GUIDES.objects))
        corners=[obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
        for _ in range(40):
            projected=[world_to_camera_view(scene,camera,p) for p in corners]
            if all(.055<=p.x<=.945 and .055<=p.y<=.945 and p.z>0 for p in projected):
                break
            camera.data.ortho_scale*=1.025
        else:
            raise RuntimeError("Could not frame "+camera.name+": "+json.dumps({
                "camera_matrix":[list(row) for row in camera.matrix_world],
                "projected_min":[min(p[i] for p in projected) for i in range(3)],
                "projected_max":[max(p[i] for p in projected) for i in range(3)],
                "world_min":[min(p[i] for p in corners) for i in range(3)],
                "world_max":[max(p[i] for p in corners) for i in range(3)],
            }))
        result.append({"camera":camera.name,"full_bounds_inside_margin":True})
    return result


def validate(scene,source_vertices):
    # Geometry from the approved source remains intact: details are additive.
    for name,vertices in source_vertices.items():
        assert vertices==[tuple(v.co) for v in bpy.data.objects[name].data.vertices], name
    slots={o.get("runtime_semantic_slot") for o in geo.SHIP.objects}
    assert slots=={0,1}, slots
    for obj in geo.SHIP.objects:
        for vertex in obj.data.vertices:
            assert all(math.isfinite(v) for v in vertex.co), obj.name
    assert geo.GUIDES.get("exclude_from_runtime_export")
    assert bpy.data.objects["Roof_Visor"].bound_box[1][2] <= 1.898
    scene["approved_source_vertices_unchanged"]=True


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if not bpy.app.background or "--factory-startup" not in sys.argv:
        raise RuntimeError("Use an independent background factory-startup process.")
    source=args.source
    if not source.is_absolute() or source.is_symlink() or source.name!="boat5_ocean_trawler_gray_v02.blend":
        raise RuntimeError("Supply the approved local gray-v02 .blend explicitly.")
    source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
    approved=json.loads(source.with_name("blockout-manifest.json").read_text())
    if source_hash!=approved["blend_sha256"]:
        raise RuntimeError("Approved blockout hash mismatch.")
    output=args.output
    if not output.is_absolute() or output.exists() or output.is_symlink() or not output.parent.is_dir():
        raise RuntimeError("An absolute new output folder with an existing parent is required.")
    output.mkdir(mode=0o700)
    bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
    geo.SHIP=bpy.data.collections["01_SHIP_BLOCKOUT_BODY_AND_GLASS"]
    geo.SHIP.name="01_SHIP_REFINED_BODY_AND_GLASS"
    geo.GUIDES=bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"]
    geo.STUDIO=bpy.data.collections["03_PRESENTATION_NOT_EXPORT"]
    # The approved file hides studio helpers in the editable viewport. Enable
    # their evaluation while framing: otherwise inactive cameras can retain an
    # identity matrix after open_mainfile despite having valid transform fields.
    geo.STUDIO.hide_viewport=False
    source_vertices={o.name:[tuple(v.co) for v in o.data.vertices] for o in geo.SHIP.objects}
    make_palette()
    geo.BODY=PALETTE["cabin"]
    geo.GLASS=PALETTE["glass"]
    geo.GUIDE_MAT=PALETTE["iron"]
    tint_approved_geometry()
    window_details()
    cabin_details()
    deck_and_hull_details()
    setup_guides()
    scene=bpy.context.scene
    scene["title"]=TITLE
    scene["scope"]="Refined Blender art preview only. Not a game-ready asset or in-game capture."
    scene["material_warning"]="Preview palette/procedural variation; native shader/paint UV mapping is still pending."
    scene["source_blend_sha256"]=source_hash
    scene.cycles.samples=40
    scene.view_settings.look="AgX - Medium High Contrast"
    scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value=.35
    cameras=sorted([o for o in geo.STUDIO.objects if o.type=="CAMERA"],key=lambda o:o.name)
    for camera in cameras:
        camera["body_only"]=camera.name=="06_Body_Only"
    # A normal gameplay-sized view makes excess detail obvious, but remains a
    # Blender render: it does not claim the game's camera, sea or lighting.
    game_camera=geo.add_camera("07_Small_Scale_Readability",(3.6,5.2,-5.6),(0,.82,.2),5.2,(640,480))
    game_camera["body_only"]=False
    cameras.append(game_camera)
    framing=frame_cameras(scene,cameras)
    validate(scene,source_vertices)
    geo.STUDIO.hide_viewport=True
    scene.camera=cameras[0]
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    geo.GUIDES.hide_render=False
    blend=output/"boat5_ocean_trawler_refined_v02.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=True)
    renders=[]
    for camera in cameras:
        scene.camera=camera
        geo.GUIDES.hide_render=bool(camera["body_only"])
        scene.render.resolution_x=camera["render_width"]
        scene.render.resolution_y=camera["render_height"]
        path=output/(camera.name+".png")
        scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)
        renders.append({"camera":camera.name,"file":path.name,"gear_guides_visible":not geo.GUIDES.hide_render})
        print("REFINED_RENDER "+json.dumps(renders[-1]),flush=True)
    corners=[o.matrix_world @ Vector(c) for o in geo.SHIP.objects for c in o.bound_box]
    depsgraph=bpy.context.evaluated_depsgraph_get()
    triangles=0
    for obj in geo.SHIP.objects:
        evaluated=obj.evaluated_get(depsgraph)
        data=evaluated.to_mesh()
        data.calc_loop_triangles()
        triangles+=len(data.loop_triangles)
        evaluated.to_mesh_clear()
    manifest={
        "title":TITLE,"blender":bpy.app.version_string,"source_blend_sha256":source_hash,
        "approved_source_vertices_unchanged":True,"ship_objects":len(geo.SHIP.objects),
        "guide_objects":len(geo.GUIDES.objects),"evaluated_body_triangles":triangles,
        "blender_bounds_min":[min(p[i] for p in corners) for i in range(3)],
        "blender_bounds_max":[max(p[i] for p in corners) for i in range(3)],
        "semantic_groups":[0,1],"preview_materials_are_runtime_materials":False,
        "native_shader_uv_mapping":False,"four_damage_states":False,"game_integration":False,
        "game_or_save_access":False,"original_game_meshes_loaded":False,
        "renders":renders,"framing":framing,"blend":blend.name,
        "blend_sha256":hashlib.sha256(blend.read_bytes()).hexdigest(),
        "source_script_sha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "awaiting_user_confirmation":"Refined art and palette before further production or game integration",
    }
    with (output/"refinement-manifest.json").open("x",encoding="utf-8") as file:
        json.dump(manifest,file,ensure_ascii=False,indent=2)
    print("REFINED_COMPLETE "+json.dumps(manifest,ensure_ascii=False),flush=True)


if __name__=="__main__":
    main()
