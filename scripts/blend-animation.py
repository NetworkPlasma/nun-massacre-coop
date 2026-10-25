"""Blend one of the mod's clips toward another, part by part (both made by convert-animation.py, same frame count,
same starting foot).

Used for the female walk: Mixamo's Female Walk is a catwalk (feet crossing the middle line, wide hip sway, wrists
flicked out, leaning back), which looks awkward at the game's walking pace. Taking it about halfway to the Standard
Walk keeps its lighter step and arm swing but walks like a person.

Usage: python scripts/blend-animation.py walk_female_raw.anim.json walk_male.anim.json walk_female.anim.json
       (the raw clip: python scripts/convert-animation.py "Female Walk.fbx" walk_female_raw.anim.json)
"""
from __future__ import annotations
import json
import math
import sys

# How far each part goes toward the other clip (0 = keep, 1 = the other clip's).
PARTS = {
    "hips": 0.55, "pelvisUp": 0.65, "pelvisLeft": 0.65,           # hip sway and roll
    "spine": 0.6, "chestUp": 0.6, "chestLeft": 0.5,               # leaning back
    "neck": 0.6, "headUp": 0.6, "headFace": 0.6,
    "thighL": 0.55, "thighR": 0.55, "shinL": 0.6, "shinR": 0.6,   # feet crossing
    "toesL": 0.55, "toesR": 0.55, "soleL": 0.55, "soleR": 0.55,
    "shoulderL": 0.5, "shoulderR": 0.5, "upperArmL": 0.5, "upperArmR": 0.5,
    "forearmL": 0.55, "forearmR": 0.55,
    "fingersL": 0.7, "fingersR": 0.7, "palmL": 0.7, "palmR": 0.7, # flicked wrists
}
POSITIONS = {"hips"}  # Everything else is a direction.


def blend(a, b, t, direction):
    v = [a[i] + (b[i] - a[i]) * t for i in range(3)]
    if direction:
        n = math.sqrt(sum(c * c for c in v))
        if n > 1e-6: v = [c / n for c in v]
    return [round(c, 5) for c in v]


def main(base_path, other_path, out_path):
    base, other = json.load(open(base_path)), json.load(open(other_path))
    if len(base["frames"]) != len(other["frames"]): raise SystemExit("the clips differ in length")
    size = base["legLength"] / other["legLength"]  # The other clip's positions, at this clip's size.
    for fa, fb in zip(base["frames"], other["frames"]):
        for key, t in PARTS.items():
            target = [c * size for c in fb[key]] if key in POSITIONS else fb[key]
            fa[key] = blend(fa[key], target, t, key not in POSITIONS)
    stride = PARTS["thighL"]
    base["cycleDistance"] = round(base["cycleDistance"] + (other["cycleDistance"] * size - base["cycleDistance"]) * stride, 4)
    base["name"] = base.get("name", "") + " (blended)"
    json.dump(base, open(out_path, "w"), separators=(",", ":"))
    print("wrote", out_path, "cycle", base["cycleDistance"])


if __name__ == "__main__":
    main(*sys.argv[1:4])
