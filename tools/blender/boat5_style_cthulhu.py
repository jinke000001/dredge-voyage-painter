"""Original deep-sea cult ornamentation for a Blender-only ship proposal.

The supplied ref module owns geometry helpers and the loaded source collection.
New sculptures are static authored mesh, not new gameplay equipment.
"""

import math

import bpy
from mathutils import Vector


def tube(ref, name, centers, radii, region, sides=7, slot=0):
    """A tapered polygonal sculpture in Unity-local coordinates (Y up)."""
    centers=[Vector(p) for p in centers]
    points=[]
    for i,center in enumerate(centers):
        tangent=(centers[min(i+1,len(centers)-1)]-centers[max(i-1,0)]).normalized()
        reference=Vector((0,1,0)) if abs(tangent.y)<.92 else Vector((1,0,0))
        u=tangent.cross(reference).normalized()
        v=tangent.cross(u).normalized()
        for j in range(sides):
            angle=j*math.tau/sides
            points.append(tuple(center+radii[i]*(u*math.cos(angle)+v*math.sin(angle))))
    faces=[]
    for i in range(len(centers)-1):
        for j in range(sides):
            k=i*sides+j
            faces.append((k,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,k+sides))
    faces.extend([tuple(reversed(range(sides))),tuple(range((len(centers)-1)*sides,len(centers)*sides))])
    return ref.paint(ref.geo.mesh(name,points,faces),region,slot)


def recolor(ref):
    colors={
        "hull":((.023,.060,.049),(.044,.094,.076)),
        "hull_shadow":((.017,.030,.027),None),
        "keel":((.035,.024,.028),(.055,.036,.040)),
        "cabin":((.105,.155,.125),(.15,.205,.158)),
        "roof":((.040,.072,.063),(.068,.109,.088)),
        "roof_edge":((.12,.095,.050),None),
        "trim":((.28,.245,.145),(.37,.32,.19)),
        "deck":((.105,.083,.052),(.155,.12,.074)),
        "wood_seam":((.025,.032,.022),None),
        "iron":((.038,.057,.044),(.058,.077,.051)),
        "iron_edge":((.17,.158,.092),None),
        "brass":((.24,.19,.078),None),
        "glass":((.025,.095,.062),None),
        "glass_glint":((.055,.14,.082),None),
        "wear":((.145,.165,.082),None),
        "old_paint":((.19,.23,.13),None),
    }
    replacements={}
    for key,(dark,light) in colors.items():
        glow=.22 if key=="glass" else 0
        replacements["PREVIEW_"+key]=ref.finish_material("CULT_"+key,dark,light,glow=glow)
    for obj in ref.geo.SHIP.objects:
        for slot in obj.material_slots:
            if slot.material and slot.material.name in replacements:
                slot.material=replacements[slot.material.name]
    for key,color,glow in [
        ("cult_bone",(.39,.37,.235),0),
        ("cult_bronze",(.19,.165,.072),0),
        ("cult_dark",(.025,.047,.036),0),
        ("cult_amber",(.17,.38,.13),1.5),
        ("cult_ink",(.008,.021,.014),0),
    ]:
        ref.PALETTE[key]=ref.finish_material(key,color,roughness=.86,glow=glow)


def build(ref):
    recolor(ref)
    # Six swept finials form a recognisable shrine-like roof crown. The center
    # is left clear for the original independent flagpole and upper lamp.
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        for index,z in enumerate((1.10,.76,.39)):
            tube(ref,"CULT_Roof_Horn_%s_%d"%(label,index),
                 [(sign*.43,1.86,z),(sign*.535,1.99,z-.02),
                  (sign*.58,2.14,z-.055),(sign*.55,2.25,z-.085),
                  (sign*.49,2.29,z-.105)],
                 [.056,.050,.035,.018,.004],"cult_bone")
            ref.detail_box("CULT_Finial_Mount_%s_%d"%(label,index),
                           (sign*.43-.065,1.85,z-.065),(sign*.43+.065,1.90,z+.065),"cult_bronze")
        # Bone ribs wrap the broad hull facets; additions remain attached rather
        # than becoming large tentacles that would imply a different collider.
        for index,(z,width,deck) in enumerate([(-.98,.65,.47),(-.30,.70,.485),(.69,.65,.54)]):
            tube(ref,"CULT_Hull_Rib_%s_%d"%(label,index),
                 [(sign*width*.88,-.075,z),(sign*width*.995,.18,z),
                  (sign*(width+.018),deck+.02,z),(sign*(width+.005),deck+.17,z),
                  (sign*(width-.070),deck+.215,z+.025)],
                 [.010,.025,.030,.024,.006],"cult_bone",6)
        # Two sculpted curling tendrils on the bow rail, below the retained lamp.
        tube(ref,"CULT_Bow_Curl_"+label,
             [(sign*.12,.82,1.83),(sign*.30,.95,1.77),(sign*.37,1.10,1.72),
              (sign*.33,1.23,1.76),(sign*.23,1.27,1.81),(sign*.19,1.20,1.84),
              (sign*.24,1.17,1.85)],
             [.059,.058,.046,.035,.026,.016,.004],"cult_bronze",8)

    # A low relief eye and six scrolls make a unique front cabin crest. The
    # original forelamp center is higher, at Y1.23, and remains unobscured.
    ref.panel("CULT_Eye_Backing",[(-.19,.96,1.445),(-.10,1.055,1.445),
              (0,1.085,1.445),(.10,1.055,1.445),(.19,.96,1.445),
              (.10,.895,1.445),(0,.865,1.445),(-.10,.895,1.445)],"cult_bronze")
    ref.panel("CULT_Green_Eye",[(-.145,.96,1.451),(-.06,1.011,1.451),
              (.07,1.011,1.451),(.145,.96,1.451),(.07,.915,1.451),(-.06,.915,1.451)],"cult_amber",1)
    ref.panel("CULT_Vertical_Pupil",[(-.013,.96,1.455),(0,1.009,1.455),
              (.013,.96,1.455),(0,.916,1.455)],"cult_ink")
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        for i in range(3):
            x=sign*(.050+i*.05)
            z=1.455
            tube(ref,"CULT_Crest_Tendril_%s_%d"%(label,i),
                 [(x,.90,z),(x+sign*.025,.82,z+.008),
                  (x+sign*.10,.77,z+.014),(x+sign*.145,.80,z+.017),
                  (x+sign*.14,.85,z+.020)],
                 [.018,.017,.013,.009,.003],"cult_bronze",6)
    # Small inset ritual gems are preview emission, not a promise that the old
    # game's two original material slots can reproduce this coloured light.
    for sign,label in [(-1,"Port"),(1,"Starboard")]:
        x=sign*.708
        front=[(x,.325,-.12),(x,.39,.035),(x,.325,.19),(x,.26,.035)]
        back=[(sign*.68,y,z) for _,y,z in front]
        ref.paint(ref.geo.mesh("CULT_Side_Rune_"+label,back+front,
                  [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),
                   (2,3,7,6),(3,0,4,7)]),"cult_bronze")
        ref.panel("CULT_Side_Rune_Gem_"+label,[(x+sign*.003,.325,-.055),
                  (x+sign*.003,.355,.035),(x+sign*.003,.325,.125),
                  (x+sign*.003,.295,.035)],"cult_amber",1)
    return {
        "display_name":"深海异教渔船",
        "design":"Swept bone crown, bronze tentacle figurehead, cult eye crest, patinated hull ribs.",
        "preview_emission":True,
        "runtime_emissive_material_compatibility":"Not implemented; original material UV/slot contract needs review.",
        "accessory_clearance":"Original guide transforms preserved; actual equipment and animations unverified.",
    }
