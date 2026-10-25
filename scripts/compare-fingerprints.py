"""Compare two peers' world fingerprints (written by the test step `fp:<label>`).

Usage: python scripts/compare-fingerprints.py <host.txt> <guest.txt> [--positions]
Lists objects present on one side only and every differing field. Rounded positions (20 cm grid)
are compared only with --positions; rotations within 12 degrees match; poses of free physics
bodies and animated characters (loose=1) are not compared.
"""
import sys


def parse(path):
    objects = {}
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line:
            continue
        key, _, rest = line.partition(" | ")
        fields = {}
        for token in rest.split(" "):
            name, sep, value = token.partition("=")
            if sep:
                fields[name] = value
        objects[key] = fields
    return objects


def close_angles(a, b, tolerance=12):
    """Euler angles within a tolerance, allowing for wrap-around (359 vs 1)."""
    try:
        return all(abs((float(x) - float(y) + 180) % 360 - 180) <= tolerance for x, y in zip(a.split(","), b.split(",")))
    except ValueError:
        return False


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    positions = "--positions" in sys.argv
    host, guest = parse(args[0]), parse(args[1])
    only_host = sorted(set(host) - set(guest))
    only_guest = sorted(set(guest) - set(host))
    differences = []
    for key in sorted(set(host) & set(guest)):
        if "Killer%20Stuff%20new" in key:
            continue  # The Nun's live animation values: she runs on the host only.
        a, b = host[key], guest[key]
        for name in sorted(set(a) | set(b)):
            if name == "pos" and not positions:
                continue
            if name in ("pos", "rot", "loose") and (a.get("loose") or b.get("loose")):
                continue  # Ragdolls, loose props and animated bodies.
            if name == "rot" and a.get(name) and b.get(name) and close_angles(a[name], b[name]):
                continue
            if a.get(name) != b.get(name):
                differences.append((key, name, a.get(name), b.get(name)))
    print(f"objects: host {len(host)}, guest {len(guest)}; only host {len(only_host)}, only guest {len(only_guest)}; field differences {len(differences)}")
    for key in only_host[:20]:
        print("  only host:", key)
    for key in only_guest[:20]:
        print("  only guest:", key)
    for key, name, a, b in differences[:200]:
        print(f"  {key} :: {name} host={a} guest={b}")


if __name__ == "__main__":
    main()
