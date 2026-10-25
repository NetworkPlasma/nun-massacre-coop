"""Give one of the mod's clips the arms of another (both made by convert-animation.py, both starting on the same foot),
resampled to its length, with the elbows bent a little more.

Mixamo's Crouched Walking holds one arm out wide and the other out behind; teammates sneaking with it looked odd. With
the Standard Walk's arm swing (elbows bent for the crouch) it reads as a careful, crouched walk.

Usage: python scripts/swap-arms.py src/NunMassacre.Multiplayer/Players/Assets/crouch_walk.anim.json
                                   src/NunMassacre.Multiplayer/Players/Assets/walk_male.anim.json 0.35
"""
from __future__ import annotations
import json
import math
import sys

ARMS = [p + s for s in ("L", "R") for p in ("shoulder", "upperArm", "forearm", "fingers", "palm")]
BENT = [p + s for s in ("L", "R") for p in ("forearm", "fingers")]


def norm(v):
    n = math.sqrt(sum(c * c for c in v))
    return [c / n for c in v] if n > 1e-6 else v


def main(target_path, source_path, bend):
    target, source = json.load(open(target_path)), json.load(open(source_path))
    frames, donor = target["frames"], source["frames"]
    for i, frame in enumerate(frames):
        at = i / (len(frames) - 1) * (len(donor) - 1)  # The same point of the cycle in the other clip.
        a, b = donor[int(at)], donor[min(int(at) + 1, len(donor) - 1)]
        t = at - int(at)
        for key in ARMS:
            v = norm([a[key][k] + (b[key][k] - a[key][k]) * t for k in range(3)])
            if key in BENT:  # Elbows bent: the forearm and hand come forward.
                v = norm([v[0], v[1], v[2] + bend])
            frame[key] = [round(c, 5) for c in v]
    json.dump(target, open(target_path, "w"), separators=(",", ":"))
    print(f"arms from {source.get('name')} into {target.get('name')}, elbows +{bend}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 0.35)
