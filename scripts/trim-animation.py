"""Keep part of one of the mod's clips (made by convert-animation.py): the frames from `start` to `end` inclusive.

Mixamo's one-off motions run long (the floor pick-up: 9.6 s, most of it standing still); teammates play only the part
that shows the action. With `end` = `start` (or "last"), the result is that one frame held still: a pose (the corpse
lying where it fell is the last frame of Falling Back Death).

Usage: python scripts/trim-animation.py pickup_low.anim.json pickup_low.anim.json 20 170
       python scripts/trim-animation.py death.anim.json dead.anim.json last last
"""
from __future__ import annotations
import json
import sys


def main(source, target, start, end):
    clip = json.load(open(source))
    frames = clip["frames"]
    last = len(frames) - 1
    a = last if start == "last" else int(start)
    b = last if end == "last" else int(end)
    kept = frames[a:b + 1]
    if len(kept) == 1:
        pose = dict(kept[0])
        pose["hips"] = [0.0, pose["hips"][1], 0.0]  # Lying where the body is.
        kept = [pose, pose]
    clip["frames"] = kept
    json.dump(clip, open(target, "w"), separators=(",", ":"))
    print(f"{source}: frames {a}..{b} of {last + 1} -> {target} ({len(kept)} frames, {(len(kept) - 1) / clip['fps']:.2f} s)")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4])
