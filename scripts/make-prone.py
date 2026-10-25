"""Make the still pose for hiding under a bed (prone.anim.json) from two Mixamo clips (both made by convert-animation.py).

Mixamo's prone set is all a soldier with a rifle; its unarmed stomach pose ("Female Laying Pose: On Stomach, Elbows
Supporting") props the chest up on the elbows with the head up, which is how someone lies under a bed looking out, but it
lies turned to one side with the feet kicked up in the air (they would go through the bed). This turns it to face ahead
and gives it the flat legs of the first frame of "Standing Up" (from lying on the stomach), the hips with them.

Usage: python scripts/make-prone.py laying.json standup.json src/NunMassacre.Multiplayer/Players/Assets/prone.anim.json
"""
from __future__ import annotations
import json
import math
import sys

LEGS = ["hips", "pelvisUp", "pelvisLeft"] + [p + s for s in ("L", "R") for p in ("thigh", "shin", "toes", "sole")]


def unit(v):
    n = math.sqrt(sum(c * c for c in v))
    return [round(c / n, 5) for c in v]


def facing_ahead(frame):
    """The frame turned about the vertical so the body (pelvis to chest) points along +Z."""
    sx, sz = frame["spine"][0], frame["spine"][2]
    yaw = math.atan2(sx, sz)  # How far the body is turned towards +X.
    c, s = math.cos(yaw), math.sin(yaw)
    def turn(v):
        return [round(v[0] * c - v[2] * s, 5), v[1], round(v[0] * s + v[2] * c, 5)]
    return {k: (v if k == "hips" else turn(v)) for k, v in frame.items()}, math.degrees(yaw)


def main(laying_path, standup_path, target_path):
    laying, standup = json.load(open(laying_path)), json.load(open(standup_path))
    upper, a = facing_ahead(laying["frames"][0])
    lower, b = facing_ahead(standup["frames"][0])
    pose = dict(upper)
    for key in LEGS:
        pose[key] = lower[key]
    pose["hips"] = [0.0, lower["hips"][1], 0.0]
    # Low enough for under a bed (about 30 cm to the slats): the chest on the floor and the chin just off it, the elbows out
    # to the sides and the forearms flat on the floor, hands together under the chin
    # (the stomach pose has its chest up on its elbows and its hands at its cheeks, which go through the slats).
    pose["spine"] = unit([0.0, 0.05, 1.0]); pose["chestUp"] = unit([0.0, 0.12, 0.99]); pose["neck"] = unit([0.0, 0.18, 0.98])
    pose["chestLeft"] = [-1.0, 0.0, 0.0]; pose["headFace"] = unit([0.0, -0.1, 1.0]); pose["headUp"] = unit([0.0, 1.0, 0.1])  # Square, looking ahead.
    for side, x in (("L", -1.0), ("R", 1.0)):
        pose["upperArm" + side] = unit([0.6 * x, -0.45, 0.66])
        pose["forearm" + side] = unit([-0.78 * x, -0.02, 0.62])
        pose["fingers" + side] = unit([-0.6 * x, -0.05, 0.8])
        pose["palm" + side] = [0.0, -1.0, 0.0]
    clip = {"name": "Prone", "fps": 30.0, "legLength": laying["legLength"], "cycleDistance": 0.5, "frames": [pose, pose]}
    json.dump(clip, open(target_path, "w"), separators=(",", ":"))
    print(f"laying pose turned {a:.1f} degrees, legs {b:.1f}; spine {pose['spine']} neck {pose['neck']} thigh L {pose['thighL']} shin L {pose['shinL']}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
