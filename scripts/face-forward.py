"""Turn one of the mod's clips (made by convert-animation.py) about the vertical so its hips face ahead on average.

Mixamo's Crouched Walking sneaks along with the whole body turned about 43 degrees to one side; teammates would look
like they crab along. Turned to face ahead, it steps forward like the other walks.

Usage: python scripts/face-forward.py src/NunMassacre.Multiplayer/Players/Assets/crouch_walk.anim.json
"""
from __future__ import annotations
import json
import math
import sys

POSITIONS = {"hips"}


def main(path):
    clip = json.load(open(path))
    frames = clip["frames"]
    # The hips' left side points along -X when facing +Z: their average heading.
    sx = sum(-f["pelvisLeft"][0] for f in frames)
    sz = sum(-f["pelvisLeft"][2] for f in frames)
    yaw = math.atan2(sz, sx)  # How far the hips are turned (radians, about +Y).
    c, s = math.cos(yaw), math.sin(yaw)
    def turn(v):  # Rotate about +Y so the hips face +Z again.
        return [round(v[0] * c + v[2] * s, 5), v[1], round(-v[0] * s + v[2] * c, 5)]
    for f in frames:
        for key, value in f.items():
            f[key] = turn(value)
    json.dump(clip, open(path, "w"), separators=(",", ":"))
    print(f"turned {math.degrees(yaw):.1f} degrees back to face ahead")


if __name__ == "__main__":
    main(sys.argv[1])
