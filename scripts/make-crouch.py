"""Make a crouched clip from a standing one (both in the format convert-animation.py writes).

Mixamo's unarmed crouches stand turned 30 to 45 degrees to one side and hold their hands up; turned to face ahead for
teammates, their legs read wrong. This keeps a clip that already looks right (the Standard Walk, the Breathing Idle)
and crouches it: the hips come down and back, each foot stays where the clip puts it (fore and aft brought in, so the
steps are shorter) with the knee solved to reach it, the pelvis and back tip forward while the head keeps looking
ahead, and the elbows bend in.

Usage: python scripts/make-crouch.py walk_male.anim.json crouch_walk.anim.json 0.19 0.75
       python scripts/make-crouch.py walk_female.anim.json crouch_walk_female.anim.json 0.19 0.75
       python scripts/make-crouch.py idle.anim.json crouch_idle.anim.json 0.19 1.0
(arguments: source, target, how far the hips drop in metres, how much of each step's length is kept)
"""
from __future__ import annotations
import json
import math
import sys

THIGH, SHIN, HIP_HALF = 0.46, 0.43, 0.09   # Metres, for a 0.889 m leg (the clips' own).
PELVIS_TIP, SPINE_TIP, CHEST_TIP, NECK_TIP = 26.0, 46.0, 50.0, -28.0  # Degrees forward (the neck back: the face looks ahead).


def add(a, b): return [a[i] + b[i] for i in range(3)]
def sub(a, b): return [a[i] - b[i] for i in range(3)]
def scl(a, s): return [c * s for c in a]
def dot(a, b): return sum(a[i] * b[i] for i in range(3))
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    n = length(a)
    return [c / n for c in a] if n > 1e-9 else a


def tip(v, degrees):
    """Pitched forward about the character's right (+X): up (0,1,0) goes towards ahead (+Z)."""
    t = math.radians(degrees)
    return [v[0], v[1] * math.cos(t) - v[2] * math.sin(t), v[1] * math.sin(t) + v[2] * math.cos(t)]


def knee_solve(hip, foot, ahead=(0.0, 0.0, 1.0)):
    """Thigh and shin directions reaching from the hip to the foot, the knee bending forward."""
    to = sub(foot, hip)
    c = min(length(to), (THIGH + SHIN) * 0.999)
    u = norm(to)
    cos_a = max(-1.0, min(1.0, (THIGH * THIGH + c * c - SHIN * SHIN) / (2 * THIGH * c)))
    a = math.acos(cos_a)
    pole = norm(sub(list(ahead), scl(u, dot(list(ahead), u))))
    thigh = norm(add(scl(u, math.cos(a)), scl(pole, math.sin(a))))
    knee = add(hip, scl(thigh, THIGH))
    shin = norm(sub(add(hip, scl(u, c)), knee))
    return thigh, shin


def main(source, target, drop, stride):
    clip = json.load(open(source))
    for f in clip["frames"]:
        hips = f["hips"]
        left = f["pelvisLeft"]
        new_hips = [hips[0], hips[1] - drop, hips[2] - 0.08]  # Down, and back over the heels (the back leans out over the knees).
        for side, sign in (("L", 1.0), ("R", -1.0)):
            hip = add(hips, scl(left, HIP_HALF * sign))
            ankle = add(add(hip, scl(f["thigh" + side], THIGH)), scl(f["shin" + side], SHIN))
            ankle[2] = hips[2] + (ankle[2] - hips[2]) * stride  # Shorter steps, fore and aft.
            new_hip = add(new_hips, scl(left, HIP_HALF * sign))
            f["thigh" + side], f["shin" + side] = knee_solve(new_hip, ankle)
        f["hips"] = new_hips
        for key, degrees in (("pelvisUp", PELVIS_TIP), ("pelvisLeft", PELVIS_TIP), ("spine", SPINE_TIP),
                             ("chestUp", CHEST_TIP), ("chestLeft", CHEST_TIP), ("neck", NECK_TIP)):
            f[key] = norm(tip(f[key], degrees))
        for side in ("L", "R"):
            f["shoulder" + side] = norm(tip(f["shoulder" + side], CHEST_TIP))
            f["upperArm" + side] = norm(tip(f["upperArm" + side], 22.0))           # The arms hang a little forward...
            f["forearm" + side] = norm(add(f["forearm" + side], [0.0, 0.0, 0.55]))  # ...elbows bent, hands in front.
            f["fingers" + side] = norm(add(f["fingers" + side], [0.0, 0.0, 0.45]))
        for key in list(f):
            f[key] = [round(c, 5) for c in f[key]]
    clip["name"] = "Crouched " + clip.get("name", "")
    clip["cycleDistance"] = round(clip.get("cycleDistance", 0.0) * stride, 4)
    json.dump(clip, open(target, "w"), separators=(",", ":"))
    print(f"{source} -> {target}: hips down {drop} m, steps x{stride}, {len(clip['frames'])} frames")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]))
