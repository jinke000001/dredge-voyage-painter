"""Add a cyber-industrial Boat5 art proposal to an already loaded refined hull.

The parent preview driver owns input, validation, saving and rendering. This
module only changes the ship's preview materials and adds its own ship geometry.
It never edits source vertices, object transforms, guide objects or game data.
All positions passed to the common helpers use Unity X/right, Y/up, Z/bow.
"""


def _palette(ref):
    definitions = {
        "cy_graphite": ((.027, .037, .049), .78, 0),
        "cy_lower": ((.012, .019, .027), .85, 0),
        "cy_white": ((.51, .57, .59), .78, 0),
        "cy_armour": ((.092, .126, .145), .73, 0),
        "cy_edge": ((.20, .29, .32), .76, 0),
        "cy_deck": ((.063, .080, .087), .9, 0),
        "cy_black": ((.009, .016, .022), .9, 0),
        "cy_glass": ((.014, .082, .11), .66, .10),
        "cy_glint": ((.035, .13, .17), .8, 0),
        "cy_cyan": ((.015, .48, .66), .68, 1.8),
        "cy_magenta": ((.46, .025, .19), .78, .8),
    }
    for region, (color, roughness, glow) in definitions.items():
        ref.PALETTE[region] = ref.finish_material(
            "CYBER_PREVIEW_" + region, color, roughness=roughness, glow=glow)
    # Preserve the original mesh/material-index layout, including the hull's
    # three preview regions. Guide materials belong to their separate collection.
    remap = {
        "hull": "cy_graphite", "hull_shadow": "cy_armour", "keel": "cy_lower",
        "cabin": "cy_white", "roof": "cy_graphite", "roof_edge": "cy_armour",
        "trim": "cy_white", "deck": "cy_deck", "wood_seam": "cy_black",
        "iron": "cy_black", "iron_edge": "cy_edge", "brass": "cy_edge",
        "glass": "cy_glass", "glass_glint": "cy_glint",
        "wear": "cy_edge", "old_paint": "cy_white",
    }
    for obj in ref.geo.SHIP.objects:
        for index, old in enumerate(obj.data.materials):
            region = old.name.removeprefix("PREVIEW_")
            if region in remap:
                obj.data.materials[index] = ref.PALETTE[remap[region]]
        obj["cyber_material_preview_only"] = True
    ref.geo.BODY = ref.PALETTE["cy_graphite"]
    ref.geo.GLASS = ref.PALETTE["cy_glass"]


def _width(ref, z):
    stations = ref.geo.STATIONS
    for (z0, w0, _), (z1, w1, _) in zip(stations, stations[1:]):
        if z0 <= z <= z1:
            return w0 + (w1 - w0) * (z - z0) / (z1 - z0)
    raise ValueError("Cyber panel lies outside the approved hull stations")


def _solid(ref, name, inside, outside, region):
    """Closed low-poly plate; inside overlaps the base, outside defines a facet."""
    count = len(inside)
    faces = [tuple(reversed(range(count))), tuple(range(count, 2 * count))]
    faces.extend((i, (i+1) % count, (i+1) % count+count, i+count)
                 for i in range(count))
    return ref.paint(ref.geo.mesh(name, inside + outside, faces), region)


def _clip_z(profile, bound, above):
    """Clip a convex Y/Z outline without leaving duplicate boundary points."""
    clipped=[]
    previous=profile[-1]
    previous_inside=previous[1]>=bound if above else previous[1]<=bound
    for point in profile:
        inside=point[1]>=bound if above else point[1]<=bound
        if inside != previous_inside:
            t=(bound-previous[1])/(point[1]-previous[1])
            clipped.append((previous[0]+(point[0]-previous[0])*t,bound))
        if inside:
            clipped.append(point)
        previous,previous_inside=point,inside
    unique=[]
    for point in clipped:
        if not unique or any(abs(a-b)>1e-9 for a,b in zip(point,unique[-1])):
            unique.append(point)
    if len(unique)>1 and all(abs(a-b)<1e-9 for a,b in zip(unique[0],unique[-1])):
        unique.pop()
    return unique


def _hull_slices(ref, profile):
    # Each slice stays within one piecewise-linear hull station interval, so
    # X=w(Z)+offset is a plane. A single long polygon would cut a chord through
    # the curved original hull even when all of its vertices were outside it.
    low=min(p[1] for p in profile)
    high=max(p[1] for p in profile)
    cuts=sorted({p[1] for p in profile} |
                {z for z,_,_ in ref.geo.STATIONS if low<z<high})
    for z0,z1 in zip(cuts,cuts[1:]):
        points=_clip_z(_clip_z(profile,z0,True),z1,False)
        if len(points)>=3:
            yield points


def _side_sheet(ref, name, profile, sign, offset, region, slot=0):
    for number,points in enumerate(_hull_slices(ref,profile)):
        ref.panel(name+"_%02d"%number,
            [(sign*(_width(ref,z)+offset),y,z) for y,z in points],region,slot)


def _side_armour(ref):
    # Two separated, chamfered plates per side. Their inner shell is embedded
    # in the existing bulwark; the width follows the hull's changing beam.
    profiles = [
        [( .34,-1.22),(.29,-1.13),(.29,-.33),(.36,-.23),(.59,-.31),(.59,-1.15)],
        [( .34,-.10),(.29, .01),(.34, .93),(.46,1.25),(.69,1.20),(.62,-.04)],
    ]
    for sign, label in ((-1, "Port"), (1, "Starboard")):
        for number, profile in enumerate(profiles):
            for section,points in enumerate(_hull_slices(ref,profile)):
                inner = [(sign*(_width(ref,z)*.98),y,z) for y,z in points]
                outer = [(sign*(_width(ref,z)+.050),y,z) for y,z in points]
                _solid(ref,"CY_Side_Armour_%s_%d_%02d"%(label,number,section),
                       inner,outer,"cy_white")
            # Dark inset creates an unmistakable broad panel division, while a
            # narrow cyan line is readable without turning the whole hull neon.
            z0,z1 = ((-1.08,-.38) if number == 0 else (.10,.94))
            _side_sheet(ref,"CY_Side_Inset_%s_%d"%(label,number),
                [(.36,z0),(.36,z1),(.48,z1),(.50,z0)],sign,.052,"cy_armour")
            _side_sheet(ref,"CY_Side_Light_%s_%d"%(label,number),
                [(.489,z0),(.467,z1),(.483,z1),(.505,z0)],sign,.054,"cy_cyan",1)
        # A single small magenta status segment per side is the only accent.
        z=-1.06
        _side_sheet(ref,"CY_Status_%s"%label,
            [(.535,z),(.535,z+.075),(.554,z+.075),(.554,z)],sign,.055,"cy_magenta",1)


def _cabin_and_roof(ref):
    for sign, label in ((-1,"Port"),(1,"Starboard")):
        # Chunky protective uprights remain outside the glazing and do not
        # occupy the front lamp or original roof-center flag/lamp clearances.
        for number, (z0,z1) in enumerate(((.30,.39),(1.12,1.205))):
            x0,x1=sorted((sign*.455,sign*.555))
            ref.detail_box("CY_Cabin_Guard_%s_%d"%(label,number),
                (x0,1.12,z0),(x1,1.91,z1),"cy_graphite",.014)
            x0,x1=sorted((sign*.553,sign*.562))
            ref.detail_box("CY_Guard_Light_%s_%d"%(label,number),
                (x0,1.49,z0+.022),(x1,1.76,z1-.022),"cy_cyan",.002)
            # Glow remains a preview semantic group; no runtime shader promise.
            ref.geo.SHIP.objects["CY_Guard_Light_%s_%d"%(label,number)]["runtime_semantic_slot"]=1
        x0,x1=sorted((sign*.43,sign*.60))
        ref.detail_box("CY_Roof_Equipment_Mount_"+label,
            (x0,1.875,.42),(x1,1.925,1.09),"cy_armour",.009)
        # Eight-corner side equipment pods change the upper silhouette while
        # staying on the roof edges. Their short legs are part of the same mass.
        profile=[(1.92,.43),(2.005,.43),(2.055,.50),(2.055,.96),
                 (2.015,1.07),(1.92,1.07)]
        inner=[(sign*.455,y,z) for y,z in profile]
        outer=[(sign*.605,y,z) for y,z in profile]
        _solid(ref,"CY_Roof_Side_Pod_"+label,inner,outer,"cy_graphite")
        ref.panel("CY_Roof_Pod_Light_"+label,
            [(sign*.606,1.975,.52),(sign*.606,1.975,.96),
             (sign*.606,1.994,.96),(sign*.606,1.994,.52)],"cy_cyan",1)
        for number,z in enumerate((.56,.69,.82)):
            x0,x1=sorted((sign*.48,sign*.575))
            ref.detail_box("CY_Pod_Top_Rib_%s_%d"%(label,number),
                (x0,2.052,z),(x1,2.068,z+.025),"cy_edge",.002)
    # The short frontal identification panel is below the original forelamp.
    profile=[(-.23,.83),(-.18,.78),(.18,.78),(.23,.83),(.23,1.05),(-.23,1.05)]
    inside=[(x,y,1.424) for x,y in profile]
    outside=[(x,y,1.493) for x,y in profile]
    _solid(ref,"CY_Front_Identification_Panel",inside,outside,"cy_graphite")
    ref.panel("CY_Front_Panel_White_Edge",[(-.20,1.017,1.495),(.20,1.017,1.495),
        (.20,1.034,1.495),(-.20,1.034,1.495)],"cy_white")
    for number, (x,length) in enumerate(((-.15,.22),(-.15,.14),(.11,.045))):
        y=.952 if number==0 else .905
        ref.panel("CY_Front_Display_%d"%number,[(x,y,1.496),(x+length,y,1.496),
            (x+length,y+.019,1.496),(x,y+.019,1.496)],"cy_cyan",1)


def build(ref):
    """Apply one visual proposal; the parent validates and writes new artifacts."""
    if any(obj.name.startswith("CY_") for obj in ref.geo.SHIP.objects):
        raise RuntimeError("Cyber style must be built on a fresh refined source")
    previous_count=len(ref.geo.SHIP.objects)
    _palette(ref)
    _side_armour(ref)
    _cabin_and_roof(ref)
    return {
        "style":"cyberpunk-industrial-trawler",
        "display_name":"赛博改装渔船",
        "added_ship_objects":len(ref.geo.SHIP.objects)-previous_count,
        "visual_changes":["segmented side armour", "wide cabin guards",
                          "roof-edge equipment pods", "restrained cyan strips"],
        "base_geometry_and_guides_unchanged":True,
        "preview_only":True,
        "runtime_material_compatibility":False,
        "risks":["Emissive preview requires a later native-shader decision",
                 "Side armour and roof guards need original accessory clearance QA",
                 "Added armour is cosmetic and does not change collision or buoyancy"],
    }
