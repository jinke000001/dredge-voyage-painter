"""Author four independent cosmetic damage states for an owned style preview.

Blender --background --factory-startup --disable-autoexec --python-exit-code 1
--python this_file.py -- --style cthulhu --source /owned/style_v01.blend
--output /absolute/nonexistent/folder
This does not export/install game resources or access any saves.
"""

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Vector


STATES=("Sturdy","Ok","Poor","Critical")
INDENT=(0,.035,.095,.18)
DROP=(0,.008,.025,.065)
ROOT=Path(__file__).parent


def load_helpers():
    spec=importlib.util.spec_from_file_location("boat5_refine",ROOT/"boat5_ocean_trawler_refine.py")
    ref=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ref)
    return ref


def unity(point):
    return Vector((point.x,point.z,point.y))


def clamp(value):
    return max(0,min(1,value))


def station_weight(z):
    # Piecewise linear at the original hull stations: armour slices and hull
    # facets receive the same displacement rather than cutting across a curve.
    stations=((.45,0),(.94,1),(1.34,.5),(1.68,0))
    for (z0,w0),(z1,w1) in zip(stations,stations[1:]):
        if z0<=z<=z1:
            return w0+(w1-w0)*(z-z0)/(z1-z0)
    return 0


def damage_point(p,state,source_name,style):
    q=p.copy()
    side=clamp((p.x-.18)/.16)
    height=min(clamp((p.y+.20)/.22),clamp((1.17-p.y)/.35))
    weight=station_weight(p.z)*side*height
    q.x-=INDENT[state]*weight
    q.y-=DROP[state]*weight
    if style=="cthulhu" and source_name=="CULT_Roof_Horn_Starboard_0":
        # Bend one decorative horn; its base and the central flag/lamp landings
        # stay in place. No detached fragment or animation is introduced.
        tip=clamp((p.y-1.96)/.33)
        q.x+=(0,0,.02,.08)[state]*tip
        q.y-=(0,0,.05,.13)[state]*tip
        q.z+=(0,0,.018,.05)[state]*tip
    return q


def snapshot(objects):
    return {obj.name:{"vertices":[list(v.co) for v in obj.data.vertices],
                      "matrix":[list(row) for row in obj.matrix_world]}
            for obj in objects}


def new_collection(name):
    return bpy.data.collections.new(name)


def copy_state(base,index,style):
    collection=new_collection("%02d_%s_BODY_AND_GLASS"%(index+1,STATES[index].upper()))
    for original in base:
        obj=original.copy()
        obj.data=original.data.copy()
        obj.name=STATES[index]+"__"+original.name
        obj.data.name=obj.name+"_Mesh"
        obj["source_object"]=original.name
        obj["damage_state"]=STATES[index]
        collection.objects.link(obj)
        if index:
            inverse=obj.matrix_world.inverted()
            for vertex in obj.data.vertices:
                point=unity(obj.matrix_world @ vertex.co)
                vertex.co=inverse @ unity(damage_point(point,index,original.name,style))
            work=bmesh.new()
            work.from_mesh(obj.data)
            bmesh.ops.recalc_face_normals(work,faces=list(work.faces))
            work.to_mesh(obj.data)
            work.free()
            obj.data.update()
    return collection


def scar_surfaces(collection,style):
    triangles=[]
    for obj in collection.objects:
        name=obj.get("source_object","")
        selected=(name=="Hull_Flared_Chine" if style=="cthulhu" else
                  name.startswith("CY_Side_Armour_Starboard_1_"))
        if not selected:
            continue
        obj.data.calc_loop_triangles()
        for triangle in obj.data.loop_triangles:
            vertices=[obj.matrix_world @ obj.data.vertices[i].co for i in triangle.vertices]
            center=unity(sum(vertices,Vector())/3)
            normal=(vertices[1]-vertices[0]).cross(vertices[2]-vertices[0])
            if normal.length<1e-8:
                continue
            if normal.normalized().x>.5 and .20<center.y<.76 and .58<center.z<1.30:
                triangles.append((normal.length,vertices,normal.normalized(),obj.name))
    triangles.sort(key=lambda row:row[0],reverse=True)
    if not triangles:
        raise RuntimeError("No exposed damage surface: "+style)
    return triangles


def cracks(ref,collection,index,style):
    if not index:
        return 0
    surfaces=scar_surfaces(collection,style)
    count=(0,1,3,5)[index]
    width=(0,.0025,.006,.011)[index]
    pattern=((.12,.18),(.25,.27),(.28,.42),(.42,.35),(.47,.19),(.64,.14))
    ref.geo.SHIP=collection
    for i in range(count):
        _,(a,b,c),normal,surface=surfaces[i%len(surfaces)]
        uv=[(v,1-u-v) if i>=len(surfaces) else (u,v) for u,v in pattern]
        line=[a+(b-a)*u+(c-a)*v for u,v in uv]
        for layer,region,multiplier,offset in [("chip","damage_chip",2.0,.0010),
                                               ("crack","damage_dark",1.0,.0017)]:
            points=[]
            faces=[]
            for j,(p,q) in enumerate(zip(line,line[1:])):
                across=(q-p).cross(normal).normalized()*width*multiplier
                k=len(points)
                points.extend([tuple(unity(v+normal*offset)) for v in (p-across,p+across,q+across,q-across)])
                faces.append((k,k+1,k+2,k+3))
            obj=ref.paint(ref.geo.mesh("%s__Damage_%s_%02d"%(STATES[index],layer,i),points,faces),region)
            obj["source_surface"]=surface
            obj["damage_overlay_geometry"]=True
            obj["damage_state"]=STATES[index]
    return count


def configure_scene(scene,source):
    scene.world=source.world
    scene.render.engine="CYCLES"
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.image_settings.file_format="PNG"
    scene.render.resolution_percentage=100
    scene.render.resolution_x=1600
    scene.render.resolution_y=1200
    scene.view_settings.view_transform="AgX"
    scene.view_settings.look="AgX - Medium High Contrast"


def comparison_scene(ref,scenes,collections,guides,title):
    scene=bpy.data.scenes.new("00_Four_State_Comparison")
    configure_scene(scene,scenes[0])
    scene.world=scenes[0].world.copy()
    camera_source=bpy.data.objects["01_Hero_Front_Quarter"]
    rotation=camera_source.rotation_euler.to_quaternion()
    right=rotation @ Vector((1,0,0))
    up=rotation @ Vector((0,1,0))
    normal=rotation @ Vector((0,0,1))
    stage=new_collection("COMPARISON_PRESENTATION_NOT_EXPORT")
    scene.collection.children.link(stage)
    camera=bpy.data.objects.new("CAMERA_Four_State_Overview",bpy.data.cameras.new("Four_State_Overview"))
    camera.data.type="ORTHO"
    camera.data.ortho_scale=11.6
    camera.rotation_euler=camera_source.rotation_euler.copy()
    camera.location=normal*30
    stage.objects.link(camera)
    scene.camera=camera
    text_mat=ref.finish_material("Comparison_Label",(.045,.055,.055),glow=.25)
    for index,collection in enumerate(collections):
        column=-2.4 if index%2==0 else 2.4
        row=2.55 if index<2 else -2.55
        assembly=new_collection("COMPARE_INSTANCE_SOURCE_"+STATES[index])
        assembly.children.link(collection)
        assembly.children.link(guides)
        obj=bpy.data.objects.new("PREVIEW_ONLY_"+STATES[index],None)
        obj.instance_type="COLLECTION"
        obj.instance_collection=assembly
        obj.location=right*column+up*row-ref.geo.vec((0,1.03,.20))
        obj["exclude_from_runtime_export"]=True
        stage.objects.link(obj)
        text=bpy.data.curves.new("Label_"+STATES[index],"FONT")
        text.body="%02d  %s"%(index+1,STATES[index].upper())
        text.size=.25
        text.align_x="CENTER"
        label=bpy.data.objects.new(text.name,text)
        label.rotation_euler=camera.rotation_euler.copy()
        label.location=right*column+up*(row+1.98)+normal*2.2
        label.data.materials.append(text_mat)
        stage.objects.link(label)
    for name,position,energy,size in [("Overview_Key",right*9+up*13+normal*18,15000,14),
                                       ("Overview_Fill",right*-14+up*4+normal*12,11000,18)]:
        data=bpy.data.lights.new(name,"AREA")
        data.energy=energy
        data.shape="DISK"
        data.size=size
        obj=bpy.data.objects.new(name,data)
        obj.location=position
        obj.rotation_euler=(-position).to_track_quat("-Z","Y").to_euler()
        stage.objects.link(obj)
    scene.render.resolution_x=2000
    scene.render.resolution_y=1800
    scene["title"]=title+" / Four Cosmetic Damage States"
    scene["scope"]="Blender comparison only; collection instances and text are not ship export objects."
    return scene


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--style",choices=("cthulhu","cyberpunk"),required=True)
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if not bpy.app.background or "--factory-startup" not in sys.argv:
        raise RuntimeError("Use an independent background factory-startup process.")
    source=args.source
    if not source.is_absolute() or source.is_symlink() or source.name!="boat5_%s_style_v01.blend"%args.style:
        raise RuntimeError("Supply the owned matching style preview explicitly.")
    parent=json.loads(source.with_name("style-manifest.json").read_text())
    source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
    if parent["style"]!=args.style or source_hash!=parent["blend_sha256"]:
        raise RuntimeError("Style source identity mismatch.")
    output=args.output
    if not output.is_absolute() or output.exists() or output.is_symlink() or not output.parent.is_dir():
        raise RuntimeError("Use an absolute nonexistent output directory with an existing parent.")
    output.mkdir(mode=0o700)
    script_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in
                   [Path(__file__),ROOT/"boat5_ocean_trawler_refine.py",ROOT/"boat5_ocean_trawler_blockout.py"]}
    bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
    ref=load_helpers()
    source_scene=bpy.context.scene
    source_body=bpy.data.collections["01_SHIP_REFINED_BODY_AND_GLASS"]
    guides=bpy.data.collections["02_ORIGINAL_GEAR_GUIDES_NOT_EXPORT"]
    studio=bpy.data.collections["03_PRESENTATION_NOT_EXPORT"]
    studio.hide_viewport=False
    bpy.context.view_layer.update()
    baseline=snapshot(source_body.objects)
    guide_snapshot=snapshot(guides.objects)
    ref.geo.BODY=bpy.data.materials.get("CULT_cabin") or bpy.data.materials["CYBER_PREVIEW_cy_white"]
    ref.geo.GLASS=bpy.data.materials.get("CULT_glass") or bpy.data.materials["CYBER_PREVIEW_cy_glass"]
    ref.PALETTE["damage_dark"]=ref.finish_material("DAMAGE_Dark_Fracture",(.008,.013,.010))
    chip=(.18,.15,.08) if args.style=="cthulhu" else (.22,.26,.27)
    ref.PALETTE["damage_chip"]=ref.finish_material("DAMAGE_Exposed_Edge",chip)
    collections=[]
    scenes=[]
    state_reports=[]
    for index,state in enumerate(STATES):
        collection=copy_state(list(source_body.objects),index,args.style)
        marks=cracks(ref,collection,index,args.style)
        scene=bpy.data.scenes.new("%02d_%s"%(index+1,state))
        configure_scene(scene,source_scene)
        scene.collection.children.link(collection)
        scene.collection.children.link(guides)
        scene.collection.children.link(studio)
        scene.camera=bpy.data.objects["01_Hero_Front_Quarter"]
        scene.camera.data.ortho_scale=6.35
        scene["damage_state"]=state
        scene["scope"]="Offline cosmetic state, not a gameplay damage event."
        collections.append(collection)
        scenes.append(scene)
        state_reports.append({"state":state,"collection":collection.name,"scene":scene.name,
                              "body_objects":len(collection.objects),"crack_paths":marks,
                              "max_designed_indent":INDENT[index],"max_designed_drop":DROP[index]})
    # Never save the source scene as the active state; preserve the original
    # source collection as an unlinked reference, not another visible ship.
    for child in list(source_scene.collection.children):
        source_scene.collection.children.unlink(child)
    overview=comparison_scene(ref,scenes,collections,guides,parent["design"]["display_name"])
    studio.hide_viewport=True
    for obj in collections[0].objects:
        record=baseline[obj["source_object"]]
        assert [list(v.co) for v in obj.data.vertices]==record["vertices"]
        assert [list(row) for row in obj.matrix_world]==record["matrix"]
    assert snapshot(guides.objects)==guide_snapshot
    assert len({obj.data.as_pointer() for col in collections for obj in col.objects})==sum(len(c.objects) for c in collections)
    for col in collections:
        for obj in col.objects:
            assert all(math.isfinite(x) for vertex in obj.data.vertices for x in vertex.co),obj.name
    bpy.context.window.scene=overview
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=="VIEW_3D":
                area.spaces.active.region_3d.view_perspective="CAMERA"
                area.spaces.active.shading.color_type="MATERIAL"
    blend=output/("boat5_%s_damage_v01.blend"%args.style)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=True)
    renders=[]
    # Overview first so visual framing can be checked while individual views run.
    jobs=[(overview,"00_Four_State_Comparison.png")]+[(scene,"%02d_%s_Hero.png"%(i+1,STATES[i])) for i,scene in enumerate(scenes)]
    for scene,name in jobs:
        bpy.context.window.scene=scene
        scene.render.filepath=str(output/name)
        bpy.ops.render.render(write_still=True)
        renders.append({"file":name,"scene":scene.name})
        print("DAMAGE_RENDER "+json.dumps(renders[-1]),flush=True)
    critical=scenes[-1]
    critical.camera=bpy.data.objects["02_Starboard_Profile"]
    critical.camera.data.ortho_scale=5.6
    critical.render.filepath=str(output/"05_Critical_Starboard.png")
    bpy.context.window.scene=critical
    bpy.ops.render.render(write_still=True)
    renders.append({"file":"05_Critical_Starboard.png","scene":critical.name})
    assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
    assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in script_hashes.items())
    manifest={"style":args.style,"blender":bpy.app.version_string,"source_blend_sha256":source_hash,
              "blend":blend.name,"blend_sha256":hashlib.sha256(blend.read_bytes()).hexdigest(),
              "source_scripts_sha256":script_hashes,"sturdy_matches_accepted_style_geometry":True,
              "independent_mesh_data_per_state":True,"guide_geometry_and_transforms_preserved":True,
              "states":state_reports,"renders":renders,"four_cosmetic_damage_states":True,
              "runtime_uvs_and_submeshes":False,"native_material_compatibility":False,
              "game_integration":False,"game_or_save_access":False,
              "awaiting_user_confirmation":"Damage appearance and future material implementation route."}
    with (output/"damage-manifest.json").open("x",encoding="utf-8") as file:
        json.dump(manifest,file,ensure_ascii=False,indent=2)
    with (output/"source-geometry-reference.json").open("x",encoding="utf-8") as file:
        json.dump({"body":baseline,"guides":guide_snapshot},file,separators=(",",":"))
    print("DAMAGE_COMPLETE "+json.dumps(manifest,ensure_ascii=False),flush=True)


if __name__=="__main__":
    main()
