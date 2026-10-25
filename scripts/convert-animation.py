"""Convert a Mixamo animation (skeleton-only FBX: the crawl, the walks) into one of the mod's clips.

Build-time only: Python 3.12 and `pip install ufbx==0.0.5`.
The clip stores, per frame, where each limb points and how the hips, chest, head, hands and feet are turned,
in a character frame (facing +Z, up +Y, right +X; Unity's axes). The runtime fits those directions onto each
character's own skeleton, so the different rest poses (the FBX's T-pose, the characters' A-poses) do not matter.
Root motion is taken out (the crawl plays in place) and kept as the distance per cycle, for the pace.

Usage: python scripts/convert-animation.py Crawling.fbx src/NunMassacre.Multiplayer/Players/Assets/crawl.anim.json 0.5
       python scripts/convert-animation.py "Walking.fbx" src/NunMassacre.Multiplayer/Players/Assets/walk_male.anim.json
       python scripts/convert-animation.py "Female Walk.fbx" src/NunMassacre.Multiplayer/Players/Assets/walk_female_raw.anim.json
       (then scripts/blend-animation.py makes walk_female.anim.json from the raw one)
The optional last number damps the hips' side-to-side sway (the crawl: 0.5).
"""
from __future__ import annotations
import json
import math
import os
import sys
import ufbx


# ---------- quaternion helpers (x, y, z, w) ----------
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx, aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)

def qinv(q):
    x, y, z, w = q
    return (-x, -y, -z, w)

def qrot(q, v):
    p = (v[0], v[1], v[2], 0.0)
    r = qmul(qmul(q, p), qinv(q))
    return (r[0], r[1], r[2])

def add(a, b): return tuple(a[i] + b[i] for i in range(3))
def sub(a, b): return tuple(a[i] - b[i] for i in range(3))
def scl(a, s): return tuple(a[i] * s for i in range(3))
def mul(a, b): return tuple(a[i] * b[i] for i in range(3))
def length(a): return math.sqrt(sum(c*c for c in a))
def norm(a):
    n = length(a)
    return tuple(c / n for c in a) if n > 1e-9 else (0.0, 0.0, 0.0)
def unity(v):  # FBX is right-handed; Unity is left-handed: mirror X.
    return (-v[0], v[1], v[2])


MIXAMO_PARENTS = {"Spine": "Hips", "Spine1": "Spine", "Spine2": "Spine1", "Neck": "Spine2", "Head": "Neck", "HeadTop_End": "Head"}
for _side in ("Left", "Right"):
    MIXAMO_PARENTS.update({_side + "UpLeg": "Hips", _side + "Leg": _side + "UpLeg", _side + "Foot": _side + "Leg",
                           _side + "ToeBase": _side + "Foot", _side + "Toe_End": _side + "ToeBase",
                           _side + "Shoulder": "Spine2", _side + "Arm": _side + "Shoulder", _side + "ForeArm": _side + "Arm",
                           _side + "Hand": _side + "ForeArm"})
    for _finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
        for _k in range(1, 5):
            MIXAMO_PARENTS[_side + "Hand" + _finger + str(_k)] = _side + "Hand" if _k == 1 else _side + "Hand" + _finger + str(_k - 1)


def read_bones(scene, anim, times):
    """Every Mixamo bone's local rest and per-frame transforms, read into plain values in one pass: the binding's node
    objects crash when revisited or when their (root) parent is touched, so each is read once, and the standard Mixamo
    hierarchy supplies the parents."""
    bones = {}
    nodes = scene.nodes
    for i in range(len(nodes)):
        node = nodes[i]
        name = node.name
        if not name.startswith("mixamorig:"): continue
        rest = trs(node.local_transform)
        frames = [trs(node.evaluate_transform(anim, t)) for t in times]
        bone = name[len("mixamorig:"):]
        parent = MIXAMO_PARENTS.get(bone)
        bones[name] = ("mixamorig:" + parent if parent else None, rest, frames)
    return bones


def trs(t):
    return ((t.translation.x, t.translation.y, t.translation.z), (t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w), (t.scale.x, t.scale.y, t.scale.z))


def world_transforms(bones, pick):
    """Bone name -> (position, rotation) in the FBX's world, composing local TRS down the hierarchy."""
    world = {}
    def solve(name):
        if name in world: return world[name]
        parent, rest, frames = bones[name]
        t, r, sc = pick(rest, frames)
        if parent is None or parent not in bones: pp, pr, ps = (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0)
        else: pp, pr, ps = solve(parent)
        world[name] = (add(pp, qrot(pr, mul(ps, t))), qmul(pr, r), mul(ps, sc))
        return world[name]
    for name in bones: solve(name)
    return {name: (v[0], v[1]) for name, v in world.items()}


def main(source, target, sway=1.0):
    scene = ufbx.load_file(source)
    metres = scene.settings.unit_meters or 0.01
    stack = scene.anim_stacks[0]
    anim = stack.anim
    fps = 30.0
    count = int(round((stack.time_end - stack.time_begin) * fps)) + 1
    bones = read_bones(scene, anim, [stack.time_begin + i / fps for i in range(count)])

    rest = world_transforms(bones, lambda r, f: r)
    def rest_rot(name): return rest["mixamorig:" + name][1]

    frames_raw = [world_transforms(bones, lambda r, f, i=i: f[i]) for i in range(count)]

    def P(w, name): return scl(w["mixamorig:" + name][0], metres)
    def delta(w, name):  # World rotation since the T-pose, for axes that start the same on every skeleton.
        return qmul(w["mixamorig:" + name][1], qinv(rest_rot(name)))

    # In the T-pose the FBX character faces +Z with +X on its left and +Y up; palms and soles face down.
    LEFT, UP, FORWARD, DOWN = (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (0.0, -1.0, 0.0)

    r = rest
    leg = (length(sub(scl(r["mixamorig:LeftLeg"][0], metres), scl(r["mixamorig:LeftUpLeg"][0], metres)))
           + length(sub(scl(r["mixamorig:LeftFoot"][0], metres), scl(r["mixamorig:LeftLeg"][0], metres))))
    arm_rest = norm(sub(r["mixamorig:LeftForeArm"][0], r["mixamorig:LeftArm"][0]))

    hips = [P(w, "Hips") for w in frames_raw]
    travel = sub(hips[-1], hips[0])
    flat_travel = (travel[0], 0.0, travel[2])
    cycle = length(flat_travel)
    heading = norm(flat_travel) if cycle > 0.05 else FORWARD

    frames = []
    for i, w in enumerate(frames_raw):
        f = i / (count - 1)
        # In place: the steady drift forward removed, what is left (sway, bob) kept.
        h = sub(hips[i], scl(flat_travel, f))
        h = sub(h, (hips[0][0], 0.0, hips[0][2]))
        def seg(a, b): return unity(norm(sub(P(w, b), P(w, a))))
        def axis(name, v): return unity(norm(qrot(delta(w, name), v)))
        frame = {
            "hips": list(unity(h)),
            "pelvisUp": axis("Hips", UP), "pelvisLeft": axis("Hips", LEFT),
            "spine": seg("Spine", "Spine2"),
            "chestUp": axis("Spine2", UP), "chestLeft": axis("Spine2", LEFT),
            "neck": seg("Neck", "Head"),
            "headUp": axis("Head", UP), "headFace": axis("Head", FORWARD),
        }
        for side, name in (("L", "Left"), ("R", "Right")):
            frame["shoulder" + side] = seg(name + "Shoulder", name + "Arm")
            frame["upperArm" + side] = seg(name + "Arm", name + "ForeArm")
            frame["forearm" + side] = seg(name + "ForeArm", name + "Hand")
            frame["fingers" + side] = seg(name + "Hand", name + "HandMiddle1")
            frame["palm" + side] = axis(name + "Hand", DOWN)
            frame["thigh" + side] = seg(name + "UpLeg", name + "Leg")
            frame["shin" + side] = seg(name + "Leg", name + "Foot")
            frame["toes" + side] = seg(name + "Foot", name + "ToeBase")
            frame["sole" + side] = axis(name + "Foot", DOWN)
        frames.append({k: [round(c, 5) for c in v] for k, v in frame.items()})

    # Centred on the player's spot: the middle is where the hips are on average over the cycle (not the first frame's
    # place), and a crawl's weaving from side to side is damped (sway < 1) so the body keeps to a duct's width.
    mid_x = sum(fr["hips"][0] for fr in frames) / len(frames)
    mid_z = sum(fr["hips"][2] for fr in frames) / len(frames)
    for fr in frames:
        fr["hips"][0] = round((fr["hips"][0] - mid_x) * sway, 5)
        fr["hips"][2] = round(fr["hips"][2] - mid_z, 5)

    if cycle < 0.05:
        # Recorded in place: the distance per cycle is how far a planted foot travels back relative to the hips.
        zs = [P(w, "LeftFoot")[2] - P(w, "Hips")[2] for w in frames_raw]
        cycle = 2.0 * (max(zs) - min(zs))
    clip = {"name": os.path.splitext(os.path.basename(source))[0], "fps": fps, "legLength": round(leg, 4), "cycleDistance": round(cycle, 4), "frames": frames}
    with open(target, "w", encoding="utf-8") as out:
        json.dump(clip, out, separators=(",", ":"))
    print(f"{count} frames at {fps:g} fps, leg {leg:.3f} m, travel per cycle {cycle:.3f} m heading {tuple(round(c, 2) for c in unity(heading))}")
    print(f"rest upper arm direction {tuple(round(c, 2) for c in unity(arm_rest))} (T-pose: sideways)")
    ys = [h[1] for h in hips]
    print(f"hips height {min(ys):.3f}..{max(ys):.3f} m; first frame pelvis up {frames[0]['pelvisUp']} left {frames[0]['pelvisLeft']}")
    print(f"first frame thigh L {frames[0]['thighL']} shin L {frames[0]['shinL']} upper arm L {frames[0]['upperArmL']} fingers L {frames[0]['fingersL']} palm L {frames[0]['palmL']}")
    sys.stdout.flush()
    os._exit(0)  # ufbx's teardown can crash on exit; the file is already written.


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 1.0)
