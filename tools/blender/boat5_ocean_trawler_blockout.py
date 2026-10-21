"""Author/render an original Boat5-sized gray blockout, without game integration.

Use Blender --background --factory-startup --disable-autoexec --python-exit-code 1
--python this_file.py -- --output /absolute/nonexistent/directory
The editable .blend and clay renders are written only to the new output folder.
Coordinates in this script are Boat5-local Unity (X right, Y up, Z bow).
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view


TITLE = "Boat5 Ocean Trawler / Gray Blockout v02"
SHIP = None
GUIDES = None
STUDIO = None
BODY = None
GLASS = None
GUIDE_MAT = None

# Longitudinal stations: bow +Z, then half-beam and deck height. A slightly
# raised forefoot and flared shoulder keep a trawler silhouette at Boat5 scale.
STATIONS = [
    (-1.43, .48, .47), (-1.19, .61, .47), (-.70, .685, .47),
    (-.12, .705, .49), (.45, .69, .51), (.94, .61, .56),
    (1.34, .47, .64), (1.68, .275, .73), (1.94, .018, .80),
]


def vec(point):
    return Vector((point[0], point[2], point[1]))


def new_collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def move_to(obj, collection):
    for parent in list(obj.users_collection):
        parent.objects.unlink(obj)
    collection.objects.link(obj)


def material(name, color, roughness=.72, object_color=False):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color, 1)
    if not result.use_nodes:
        result.use_nodes = True
    tree = result.node_tree
    if tree is None:
        result.use_nodes = True
        tree = result.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    if object_color:
        info = tree.nodes.new("ShaderNodeObjectInfo")
        tree.links.new(info.outputs["Color"], bsdf.inputs["Base Color"])
    return result


def finish(obj, collection, mat, shade=.57, bevel=0):
    move_to(obj, collection)
    obj.data.materials.append(mat)
    obj.color = (shade, shade, shade, 1)
    if collection == SHIP:
        # The blockout has only two semantic material groups. Body preview tones
        # come from object colors; these are not new runtime material slots.
        obj["runtime_semantic_slot"] = 1 if mat == GLASS else 0
        obj["blockout_only"] = True
    if bevel:
        modifier = obj.modifiers.new("Small gray-model edge break", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
    return obj


def mesh(name, points, faces, collection=None, mat=None, shade=.57, bevel=0):
    data = bpy.data.meshes.new(name + "_Mesh")
    data.from_pydata([vec(p) for p in points], [], faces)
    data.update()
    work = bmesh.new()
    work.from_mesh(data)
    bmesh.ops.recalc_face_normals(work, faces=list(work.faces))
    work.to_mesh(data)
    work.free()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    return finish(obj, collection or SHIP, mat or BODY, shade, bevel)


def box(name, minimum, maximum, shade=.57, collection=None, mat=None, bevel=.008):
    x0, y0, z0 = minimum
    x1, y1, z1 = maximum
    return mesh(name, [(x0,y0,z0),(x1,y0,z0),(x1,y0,z1),(x0,y0,z1),
                       (x0,y1,z0),(x1,y1,z0),(x1,y1,z1),(x0,y1,z1)],
                [(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],
                collection, mat, shade, bevel)


def rod(name, a, b, radius, collection=None, mat=None, shade=.45, sides=10):
    start, end = vec(a), vec(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius,
                                        depth=(end-start).length, location=(start+end)*.5)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = (end-start).to_track_quat("Z", "Y")
    return finish(obj, collection or SHIP, mat or BODY, shade)


def hull():
    points = []
    for z, width, deck in STATIONS:
        bottom = -.375 + max(0, z-.85)*.28
        points.extend([(-width,deck,z),(-width*.97,.17,z),(-width*.72,-.13,z),
                       (-width*.27,bottom+.035,z),(0,bottom,z),
                       (width*.27,bottom+.035,z),(width*.72,-.13,z),
                       (width*.97,.17,z),(width,deck,z)])
    faces = []
    for station in range(len(STATIONS)-1):
        for corner in range(8):
            a = station*9+corner
            faces.append((a,a+1,a+10,a+9))
    faces += [tuple(range(9)), tuple(range((len(STATIONS)-1)*9,len(STATIONS)*9))]
    mesh("Hull_Flared_Chine", points, faces, shade=.49)
    deck_points = [(sign*w*.955,h+.006,z) for z,w,h in STATIONS for sign in (-1,1)]
    mesh("Working_Deck", deck_points, [(i*2,i*2+1,i*2+3,i*2+2)
                                       for i in range(len(STATIONS)-1)], shade=.58)
    rim = [(w,h,z) for z,w,h in STATIONS] + [(-w,h,z) for z,w,h in reversed(STATIONS)]
    wall_points = []
    for x,y,z in rim:
        inward_x = x * max(.30, (abs(x)-.045)/max(abs(x),.001))
        inward_z = z * .976
        wall_points += [(x,y,z),(x,y+.145,z),(inward_x,y+.145,inward_z),(inward_x,y,inward_z)]
    wall_faces = []
    for i in range(len(rim)):
        j = (i+1)%len(rim)
        for k in range(4):
            wall_faces.append((i*4+k,j*4+k,j*4+(k+1)%4,i*4+(k+1)%4))
    mesh("Sheer_Bulwark_and_Cap", wall_points, wall_faces, shade=.62)
    # One simple rub rail emphasizes the long hull; no plated detail or textures.
    for sign, label in [(-1,"Port"),(1,"Starboard")]:
        for i in range(len(STATIONS)-1):
            z0,w0,_ = STATIONS[i]
            z1,w1,_ = STATIONS[i+1]
            rod("Rub_Rail_"+label+"_%02d"%i, (sign*w0*.985,.255,z0),
                (sign*w1*.985,.255,z1), .025, shade=.35, sides=6)


def frustum(name, lower, upper, y0, y1, shade=.58, cap=True):
    points = [(x,y0,z) for x,z in lower] + [(x,y1,z) for x,z in upper]
    n = len(lower)
    faces = [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    if cap:
        faces += [tuple(range(n)),tuple(range(n,2*n))]
    return mesh(name, points, faces, shade=shade)


def interpolate(a,b,t):
    return tuple(x+(y-x)*t for x,y in zip(a,b))


def window_patch(name, bottom_a, bottom_b, top_a, top_b, u0, u1, v0=.14, v1=.86):
    def point(u,v):
        p = list(interpolate(interpolate(bottom_a,bottom_b,u),
                             interpolate(top_a,top_b,u),v))
        # Offset from the wheelhouse center for an honest separate glass plane.
        dx,dz = p[0],p[2]-.75
        length = math.hypot(dx,dz)
        p[0] += .004*dx/length
        p[2] += .004*dz/length
        return tuple(p)
    mesh(name, [point(u0,v0),point(u1,v0),point(u1,v1),point(u0,v1)],
         [(0,1,2,3)],mat=GLASS)


def wheelhouse():
    # Eight-sided pilothouse gives angled forward corners and sloped glazing,
    # while retaining the roof landing at native flagpole height ~1.897.
    skirt = [(-.34,1.46),(.34,1.46),(.47,1.23),(.53,.30),(.39,.135),
             (-.39,.135),(-.53,.30),(-.47,1.23)]
    waist = [(-.32,1.41),(.32,1.41),(.49,1.20),(.49,.31),(.37,.155),
             (-.37,.155),(-.49,.31),(-.49,1.20)]
    crown = [(-.30,1.29),(.30,1.29),(.455,1.12),(.455,.33),(.35,.18),
             (-.35,.18),(-.455,.33),(-.455,1.12)]
    frustum("Wheelhouse_Lower_Skirt", skirt, waist, .52,1.17,shade=.64)
    frustum("Wheelhouse_Sloped_Lantern", waist,crown,1.17,1.80,shade=.65)
    for i in range(8):
        j=(i+1)%8
        ba,bb=(waist[i][0],1.17,waist[i][1]),(waist[j][0],1.17,waist[j][1])
        ta,tb=(crown[i][0],1.80,crown[i][1]),(crown[j][0],1.80,crown[j][1])
        divisions = [(0.07,.47),(.53,.93)] if i in (0,2,6) else [(.13,.87)]
        for pane,(u0,u1) in enumerate(divisions):
            window_patch("Glass_%02d_%02d"%(i,pane),ba,bb,ta,tb,u0,u1)
    roof = [(x*1.095,(z-.73)*1.07+.73) for x,z in crown]
    frustum("Roof_Visor",roof,roof,1.80,1.897,shade=.43)
    box("Aft_Access_Door",(-.155,.56,.124),(.155,1.10,.139),shade=.48,bevel=.004)
    box("Rod_Clamp_Support",(.26,.535,.135),(.34,.585,.23),shade=.52,bevel=.002)
    # Flush hatch leaves the native net/boxes working volume open.
    box("Aft_Flush_Working_Hatch",(-.29,.475,-.91),(.29,.51,-.38),shade=.44,bevel=.01)
    for index,(x,z,top) in enumerate([(.006,-.40,1.80),(.004,-.013,1.664)]):
        rod("Retained_Smoke_Outlet_Support_%d"%index,(x,.49,z),(x,top,z),.057,shade=.37,sides=8)
    rod("Upper_Lamp_Pedestal",(0,1.897,.346),(0,2.142,.346),.022,shade=.40)
    # A few large massing parts only: no rivets, cables, painted texture or wear.
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        box("Aft_Bollard_Base_"+label,(sign*.41-.07,.48,-1.25),
            (sign*.41+.07,.53,-1.09),shade=.43,bevel=.005)
        rod("Aft_Bollard_"+label,(sign*.41,.52,-1.17),(sign*.41,.67,-1.17),.035,shade=.40)


def native_gear_guides():
    # These are purpose-made simple occupancy dummies, never original assets.
    # Native baked rod meshes and child light positions have a wider envelope
    # than their object origins; no claim of full fitted accessory geometry.
    rod("GUIDE_FlagPole_NotExport",(.026,1.897,.791),(.026,2.86,.791),.014,GUIDES,GUIDE_MAT)
    mesh("GUIDE_Flag_NotExport",[(.026,2.80,.791),(-.43,2.80,.791),
                                (-.39,2.50,.791),(.026,2.53,.791)],[(0,1,2,3)],GUIDES,GUIDE_MAT)
    rod("GUIDE_UpperLamp_NotExport",(0,2.15,.346),(0,2.25,.346),.065,GUIDES,GUIDE_MAT)
    rod("GUIDE_ForeLamp_Bracket_NotExport",(0,1.23,1.31),(0,1.23,1.57),.025,GUIDES,GUIDE_MAT)
    rod("GUIDE_ForeLamp_NotExport",(0,1.23,1.47),(0,1.23,1.68),.105,GUIDES,GUIDE_MAT)
    rod("GUIDE_Rod1_NotExport",(.302,.585,.174),(.45,1.17,-.10),.008,GUIDES,GUIDE_MAT,sides=6)
    rod("GUIDE_Rod1_Reel_NotExport",(.273,.64,.174),(.33,.64,.174),.045,GUIDES,GUIDE_MAT)
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        rod("GUIDE_Stowed_Net_"+label,(sign*.40,.55,-1.28),
            (sign*.38,1.34,-.91),.026,GUIDES,GUIDE_MAT)
        rod("GUIDE_Net_Brace_"+label,(sign*.50,.55,-.62),
            (sign*.38,1.34,-.91),.022,GUIDES,GUIDE_MAT)
    rod("GUIDE_Net_Crossbar",(-.38,1.34,-.91),(.38,1.34,-.91),.023,GUIDES,GUIDE_MAT)
    GUIDES["purpose"] = "Original gear occupancy dummies; NOT new ship mesh or an exact asset fit."
    GUIDES["exclude_from_runtime_export"] = True


def add_camera(name, position, target, scale, resolution):
    data=bpy.data.cameras.new(name)
    data.type="ORTHO"
    data.ortho_scale=scale
    obj=bpy.data.objects.new(name,data)
    STUDIO.objects.link(obj)
    obj.location=vec(position)
    obj.rotation_euler=(vec(target)-obj.location).to_track_quat("-Z","Y").to_euler()
    obj["render_width"],obj["render_height"]=resolution
    return obj


def studio():
    ground=material("Studio_Matte_Background",(.72,.74,.75),.9)
    box("STUDIO_Ground",(-200,-.43,-200),(200,-.425,200),collection=STUDIO,mat=ground,bevel=0)
    for name,position,power,size in [("Key",(3.7,6.5,4.5),950,5),
                                     ("Fill",(-4.2,4.2,1.4),650,5),
                                     ("Rim",(-.6,5.1,-4.5),1150,4)]:
        data=bpy.data.lights.new(name,"AREA")
        data.energy=power
        data.shape="DISK"
        data.size=size
        obj=bpy.data.objects.new(name,data)
        STUDIO.objects.link(obj)
        obj.location=vec(position)
        obj.rotation_euler=(vec((0,.75,.1))-obj.location).to_track_quat("-Z","Y").to_euler()
    return [
        add_camera("01_Hero_Front_Quarter",(4.6,3.7,5.6),(0,1.03,.20),4.55,(1600,1200)),
        add_camera("02_Starboard_Profile",(7,1.2,.20),(0,1.2,.20),4.30,(1600,1200)),
        add_camera("03_Bow",(0,1.2,7),(0,1.2,.20),3.65,(1200,1200)),
        add_camera("04_Aft_Quarter",(4.6,3.8,-5.2),(0,1.02,.10),4.55,(1600,1200)),
        add_camera("05_Top",(0,7,.20),(0,.7,.20),4.30,(1200,1500)),
        add_camera("06_Body_Only",(4.6,3.7,5.6),(0,.87,.20),4.20,(1600,1200)),
    ]


def main():
    global SHIP,GUIDES,STUDIO,BODY,GLASS,GUIDE_MAT
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",required=True,type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not bpy.app.background or "--factory-startup" not in sys.argv:
        raise RuntimeError("Use an independent --background --factory-startup process.")
    output=args.output
    if not output.is_absolute() or output.exists() or output.is_symlink() or not output.parent.is_dir():
        raise RuntimeError("An absolute nonexistent output directory with an existing parent is required.")
    output.mkdir(mode=0o700)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    SHIP=new_collection("01_SHIP_BLOCKOUT_BODY_AND_GLASS")
    GUIDES=new_collection("02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT")
    STUDIO=new_collection("03_PRESENTATION_NOT_EXPORT")
    BODY=material("Body_Clay_Preview_Slot0",(.57,.57,.57),object_color=True)
    GLASS=material("Glazing_Clay_Preview_Slot1",(.105,.12,.13),.42)
    GUIDE_MAT=material("Original_Gear_Guides_Clay",(.245,.26,.27),.80)
    hull()
    wheelhouse()
    native_gear_guides()
    cameras=studio()
    scene=bpy.context.scene
    scene.unit_settings.system="METRIC"
    scene.unit_settings.scale_length=1
    scene.world.color=(.36,.36,.36)
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.get("Background").inputs["Color"].default_value=(.52,.54,.56,1)
    scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value=.5
    scene.render.engine="CYCLES"
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.view_settings.view_transform="AgX"
    scene.render.film_transparent=False
    scene.camera=cameras[0]
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    scene["title"]=TITLE
    scene["scope"]="Gray silhouette/proportion proposal only; no final UV, damage states, gameplay or runtime installation."
    scene["unity_axis_mapping"]="Blender = (Unity X, Unity Z, Unity Y)"
    scene["guide_warning"]="Dark-gray gear is placeholder occupancy; not original meshes and not part of the new hull."
    # Verify framing for every view, including the full guide flagpole. Studio
    # cameras/lights/ground must not drive bounds or clutter the editable viewport.
    bpy.context.view_layer.update()
    framing=[]
    for camera in cameras:
        scene.render.resolution_x=camera["render_width"]
        scene.render.resolution_y=camera["render_height"]
        objects=list(SHIP.objects) + ([] if camera.name=="06_Body_Only" else list(GUIDES.objects))
        corners=[obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
        for attempt in range(32):
            projected=[world_to_camera_view(scene,camera,point) for point in corners]
            if all(.055<=p.x<=.945 and .055<=p.y<=.945 and p.z>0 for p in projected):
                break
            camera.data.ortho_scale*=1.035
        else:
            raise RuntimeError("Could not frame full blockout: "+camera.name)
        framing.append({"camera":camera.name,"all_bounds_inside_margin":True,
                        "orthographic_scale":camera.data.ortho_scale})
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    STUDIO.hide_viewport=True
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=="VIEW_3D":
                area.spaces.active.shading.color_type="MATERIAL"
                area.spaces.active.region_3d.view_distance=5.5
                area.spaces.active.region_3d.view_location=vec((0,1,.2))
    bpy.ops.object.select_all(action="DESELECT")
    for obj in SHIP.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=bpy.data.objects["Hull_Flared_Chine"]
    blend=output/"boat5_ocean_trawler_gray_v02.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=True)
    renders=[]
    for camera in cameras:
        GUIDES.hide_render=camera.name=="06_Body_Only"
        scene.camera=camera
        scene.render.resolution_x=camera["render_width"]
        scene.render.resolution_y=camera["render_height"]
        path=output/(camera.name+".png")
        scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)
        renders.append({"camera":camera.name,"file":path.name,"guide_proxies_visible":not GUIDES.hide_render})
        print("BLOCKOUT_RENDER "+json.dumps(renders[-1]),flush=True)
    bounds=[]
    for obj in SHIP.objects:
        bounds.extend([obj.matrix_world @ Vector(corner) for corner in obj.bound_box])
    summary={
        "title":TITLE,"blender":bpy.app.version_string,
        "scope":"Actual Blender gray blockout; original model, not an in-game screenshot.",
        "ship_objects":len(SHIP.objects),"guide_objects":len(GUIDES.objects),
        "blender_bounds_min":[min(p[i] for p in bounds) for i in range(3)],
        "blender_bounds_max":[max(p[i] for p in bounds) for i in range(3)],
        "body_semantic_slots":[0,1],"roof_unity_y":1.897,
        "final_uvs":False,"four_damage_states":False,"game_integration":False,
        "original_game_meshes_loaded":False,"game_or_save_access":False,
        "renders":renders,"blend":blend.name,
        "framing":framing,
        "blend_sha256":hashlib.sha256(blend.read_bytes()).hexdigest(),
        "awaiting_user_confirmation":"Silhouette, wheelhouse proportion, bow and aft working-deck layout",
    }
    with (output/"blockout-manifest.json").open("x",encoding="utf-8") as stream:
        json.dump(summary,stream,ensure_ascii=False,indent=2)
    print("BLOCKOUT_COMPLETE "+json.dumps(summary,ensure_ascii=False),flush=True)


if __name__=="__main__":
    main()
