"""Convert the user-supplied character archives to embedded skinned Unity meshes.

Build-time only: Python 3.12 and `pip install ufbx==0.0.5`.
Exports each mesh, its rig (joint positions and hierarchy, bones renamed to the mod's standard
names) and up to four bone weights per vertex.
The FBX has no animation clips; the mod animates the rig procedurally at runtime.
Does not modify the source FBX. The runtime requires no Python.
"""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import zipfile
import ufbx


def xyz(v):
    return [v.x, v.y, v.z]


def transform(matrix, vector, position=False):
    columns = [xyz(matrix.c0),xyz(matrix.c1),xyz(matrix.c2)]
    coords = xyz(vector)
    translation = xyz(matrix.c3) if position else [0,0,0]
    return [sum(columns[j][i]*coords[j] for j in range(3))+translation[i] for i in range(3)]


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def sub(a, b):
    return [a[i]-b[i] for i in range(3)]


def triangulate(points):
    """Ear clipping handles the model's triangles, quads, and pentagons."""
    normal = [0.0, 0.0, 0.0]
    for a, b in zip(points, points[1:] + points[:1]):
        normal[0] += (a[1]-b[1])*(a[2]+b[2])
        normal[1] += (a[2]-b[2])*(a[0]+b[0])
        normal[2] += (a[0]-b[0])*(a[1]+b[1])
    drop = max(range(3), key=lambda k: abs(normal[k]))
    p = [[v[k] for k in range(3) if k != drop] for v in points]
    area = sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(p, p[1:]+p[:1]))
    sign = 1 if area >= 0 else -1
    def side(a, b, c):
        return sign*((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))
    left = list(range(len(p)))
    output = []
    while len(left) > 3:
        found = False
        for i, b in enumerate(left):
            a, c = left[i-1], left[(i+1) % len(left)]
            if side(p[a], p[b], p[c]) <= 1e-12:
                continue
            if any(j not in (a,b,c) and side(p[a],p[b],p[j]) >= -1e-12
                   and side(p[b],p[c],p[j]) >= -1e-12
                   and side(p[c],p[a],p[j]) >= -1e-12 for j in left):
                continue
            output.append((a,b,c))
            left.pop(i)
            found = True
            break
        if not found:
            raise ValueError("Cannot triangulate a non-convex/degenerate FBX polygon")
    if len(left) == 3:
        output.append(tuple(left))
    return output


# Each character: archive, optional archive nested inside it, FBX, texture, output name, and the
# rig's own bone names mapped to the standard ones (on top of BONE_NAMES).
CHARACTERS = {
    "male_casual": {"zip": "male_casual.zip", "inner": None, "fbx": "male_casual.fbx", "texture": "man_tex.png", "out": "male_casual"},
    "secretary": {"zip": "female-secretary-character-psx.zip", "inner": "source/female_secretary.zip", "fbx": "female_secretary.fbx", "texture": "secretary_tex.png", "out": "secretary"},
    "scientist": {"zip": "scientist-psx-style.zip", "inner": "source/scientist.zip", "fbx": "scientist.fbx", "texture": "scientist_alt.png", "out": "scientist",
                  "bones": {"shoulder_l": "shoulder_left", "upper_arm_l": "upper_arm_left", "forearm_l": "forearm_left", "hand_l": "hand_left",
                            "shoulder_r": "shoulder_right", "upper_arm_r": "upper_arm_right", "forearm_r": "forearm_right", "hand_r": "hand_right",
                            "thigh_l": "thigh_left", "calf_l": "shin.L", "foot_l": "foot_left", "thigh_r": "thigh_right", "calf_r": "shin.R", "foot_r": "foot_right"}},
    # Maverick: the scientist's rig names. The cigarette in his mouth is left out (its faces are the only ones drawn
    # entirely from the cigarette at the top of the texture); his cap and the pack at his hip stay.
    "redneck": {"zip": "redneck-character-psx-style.zip", "inner": "source/redneck.zip", "fbx": "redneck.fbx", "texture": "redneck_tex.png", "out": "redneck",
                "drop_uv": [(0.60, 0.95, 0.78, 1.0)],
                "bones": {"shoulder_l": "shoulder_left", "upper_arm_l": "upper_arm_left", "forearm_l": "forearm_left", "hand_l": "hand_left",
                          "shoulder_r": "shoulder_right", "upper_arm_r": "upper_arm_right", "forearm_r": "forearm_right", "hand_r": "hand_right",
                          "thigh_l": "thigh_left", "calf_l": "shin.L", "foot_l": "foot_left", "thigh_r": "thigh_right", "calf_r": "shin.R", "foot_r": "foot_right"}},
    # The ghost's legs, arms and neck all hang off one Spine bone: it drives as the pelvis (bob only,
    # so leaning the torso does not swing the legs). Its "Shoulder" is the upper arm.
    "ghost": {"zip": "character-ghost.zip", "inner": None, "fbx": "source/Character_Ghost.fbx", "texture": "textures/Ghost.png", "out": "ghost",
              "bones": {"Spine": "pelvis", "Neck": "neck", "Head": "head",
                        "L_Shoulder": "upper_arm_left", "L_Forearm": "forearm_left", "L_Hand": "hand_left",
                        "R_Shoulder": "upper_arm_right", "R_Forearm": "forearm_right", "R_Hand": "hand_right",
                        "L_Leg": "thigh_left", "L_Leg_01": "shin.L", "L_Foot": "foot_left",
                        "R_Leg": "thigh_right", "R_Leg_01": "shin.R", "R_Foot": "foot_right"}},
}

# The runtime animation rig addresses bones by these names.
BONE_NAMES = {
    "hips": "pelvis", "shoulder.L": "shoulder_left", "upper_arm.L": "upper_arm_left", "forearm.L": "forearm_left", "hand.L": "hand_left",
    "shoulder.R": "shoulder_right", "upper_arm.R": "upper_arm_right", "forearm.R": "forearm_right", "hand.R": "hand_right",
    "thigh.L": "thigh_left", "foot.L": "foot_left", "thigh.R": "thigh_right", "foot.R": "foot_right",
}


def read_sources(entry):
    with zipfile.ZipFile(entry["zip"]) as archive:
        if entry["inner"]:
            import io
            archive = zipfile.ZipFile(io.BytesIO(archive.read(entry["inner"])))
        return archive.read(entry["fbx"]), archive.read(entry["texture"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("characters", nargs="*", default=list(CHARACTERS))
    parser.add_argument("--output", default="src/NunMassacre.Multiplayer/Players/Assets")
    args = parser.parse_args()
    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)
    for name in args.characters:
        convert(CHARACTERS[name], out)


def convert(entry, out):
    data, texture = read_sources(entry)
    scene = ufbx.load_memory(data, target_axes=ufbx.axes_left_handed_y_up, target_unit_meters=1.0)
    # Retain native list/property wrappers: ufbx 0.0.5 child lifetimes depend on them.
    keep = [scene]
    meshes = scene.meshes
    keep.append(meshes)
    result = {"name":entry["out"], "vertices":[], "normals":[], "uv":[], "triangles":[],
              "boneNames":[], "boneParents":[], "bonePositions":[], "boneIndex":[], "boneWeight":[]}
    # Bones: one per skin cluster. The axis-converted bind matrices are mirrored (determinant -1),
    # so only joint positions are exported; at runtime every bone gets an axis-aligned frame,
    # which skins identically and lets animation rotate about the body's own axes.
    # The rig is the skin with the most bones (the body's); any other mesh (a cap) is skinned to the same bones,
    # matched by node.
    skins = []
    for mi in range(len(meshes)):
        mesh = meshes[mi]
        mesh_deformers = mesh.skin_deformers
        keep.extend([mesh, mesh_deformers])
        if len(mesh_deformers):
            mesh_skin = mesh_deformers[0]
            mesh_clusters = mesh_skin.clusters
            keep.extend([mesh_skin, mesh_clusters])
            skins.append((len(mesh_clusters), mi, mesh_skin, mesh_clusters))
    _, _, skin, clusters = max(skins, key=lambda s: (s[0], -s[1]))
    bone_names = dict(BONE_NAMES, **entry.get("bones", {}))
    bone_nodes = []
    parent_ids = []
    for ci in range(len(clusters)):
        cluster = clusters[ci]
        node = cluster.bone_node
        bind = cluster.bind_to_world
        translation = bind.c3
        parent = node.parent
        keep.extend([cluster, node, bind, translation, parent])
        bone_nodes.append(node)
        result["boneNames"].append(bone_names.get(str(node.name), str(node.name)))
        result["bonePositions"].extend(xyz(translation))
        parent_ids.append(parent.element_id if parent else -1)
    names = result["boneNames"]
    # Parents by node identity: a rig may name a bone after its own armature.
    node_ids = [node.element_id for node in bone_nodes]
    for index, parent_id in enumerate(parent_ids):
        parent = node_ids.index(parent_id) if parent_id in node_ids else -1
        result["boneParents"].append(parent if parent != index else -1)
    def weights_of(mesh):
        """Each vertex's (bone, weight) pairs, the mesh's own skin clusters matched to the rig's bones."""
        mesh_deformers = mesh.skin_deformers
        keep.append(mesh_deformers)
        if not len(mesh_deformers):
            return None
        mesh_skin = mesh_deformers[0]
        mesh_clusters, skin_vertices, skin_weights = mesh_skin.clusters, mesh_skin.vertices, mesh_skin.weights
        keep.extend([mesh_skin, mesh_clusters, skin_vertices, skin_weights])
        to_bone = []
        for ci in range(len(mesh_clusters)):
            cluster = mesh_clusters[ci]
            node = cluster.bone_node
            keep.extend([cluster, node])
            to_bone.append(node_ids.index(node.element_id) if node.element_id in node_ids else 0)
        vertex_weights = []
        for vi in range(len(skin_vertices)):
            sv = skin_vertices[vi]
            keep.append(sv)
            entries = []
            for k in range(sv.num_weights):
                w = skin_weights[sv.weight_begin + k]
                keep.append(w)
                entries.append((to_bone[int(w.cluster_index)], float(w.weight)))
            vertex_weights.append(entries)
        return vertex_weights
    def corner_weights(vertex_weights, vertex):
        if vertex_weights is None:
            return [0, 0, 0, 0], [1.0, 0.0, 0.0, 0.0]
        pairs = sorted(vertex_weights[vertex], key=lambda p: -p[1])[:4]
        while len(pairs) < 4:
            pairs.append((0, 0.0))
        total = sum(w for _, w in pairs) or 1
        return [p[0] for p in pairs], [round(p[1] / total, 5) for p in pairs]
    dropped = 0
    for mesh in meshes:
        instances = mesh.instances
        node = instances[0]
        matrix = node.geometry_to_world
        norm_matrix = node.get_compatible_matrix_for_normals()
        positions, normals, uv = mesh.vertex_position, mesh.vertex_normal, mesh.vertex_uv
        pv, pi = positions.values, positions.indices
        nv, ni = normals.values, normals.indices
        uvv, uvi = uv.values, uv.indices
        faces = mesh.faces
        keep.extend([mesh, instances, node, matrix, norm_matrix, positions, normals, uv, pv, pi, nv, ni, uvv, uvi, faces])
        vertex_weights = weights_of(mesh)
        for face in faces:
            indices = list(range(face.index_begin, face.index_begin+face.num_indices))
            tex = [[uvv[uvi[i]].x, uvv[uvi[i]].y] for i in indices]
            if any(all(u0 <= u <= u1 and v0 <= v <= v1 for u, v in tex) for u0, v0, u1, v1 in entry.get("drop_uv", [])):
                dropped += 1
                continue
            pts = [transform(matrix, pv[pi[i]], True) for i in indices]
            nms = [transform(norm_matrix, nv[ni[i]]) for i in indices]
            start = len(result["vertices"])//3
            for i in indices:
                bones, weights = corner_weights(vertex_weights, pi[i])
                result["boneIndex"].extend(bones)
                result["boneWeight"].extend(weights)
            for pt, nm, st in zip(pts,nms,tex):
                length = math.sqrt(sum(v*v for v in nm)) or 1
                result["vertices"].extend(round(v,7) for v in pt)
                result["normals"].extend(round(v/length,7) for v in nm)
                result["uv"].extend(round(v,7) for v in st)
            for a,b,c in triangulate(pts):
                normal = cross(sub(pts[b],pts[a]),sub(pts[c],pts[a]))
                avg = [nms[a][k]+nms[b][k]+nms[c][k] for k in range(3)]
                if sum(normal[k]*avg[k] for k in range(3)) < 0:
                    b,c = c,b
                result["triangles"].extend([start+a,start+b,start+c])
    vertices = result["vertices"]
    bottom = min(vertices[1::3])
    height = max(vertices[1::3])-bottom
    center_x = (max(vertices[0::3])+min(vertices[0::3]))/2
    center_z = (max(vertices[2::3])+min(vertices[2::3]))/2
    # Unit height, feet at origin: runtime scales to the game's player height.
    for i in range(0,len(vertices),3):
        vertices[i] = round((vertices[i]-center_x)/height,7)
        vertices[i+1] = round((vertices[i+1]-bottom)/height,7)
        vertices[i+2] = round((vertices[i+2]-center_z)/height,7)
    joints = result["bonePositions"]
    for i in range(0,len(joints),3):
        joints[i] = round((joints[i]-center_x)/height,7)
        joints[i+1] = round((joints[i+1]-bottom)/height,7)
        joints[i+2] = round((joints[i+2]-center_z)/height,7)
    assert len(result["boneIndex"]) == 4 * (len(vertices)//3) == len(result["boneWeight"])
    assert all(0 <= b < len(names) for b in result["boneIndex"])
    assert len(vertices)//3 == len(result["uv"])//2
    assert len(result["triangles"]) % 3 == 0
    assert all(0 <= i < len(vertices)//3 for i in result["triangles"])
    (out/(entry["out"] + ".mesh.json")).write_text(json.dumps(result,separators=(",",":")),encoding="utf-8")
    (out/(entry["out"] + ".png")).write_bytes(texture)
    print(f"{entry['out']}: converted {len(vertices)//3} mesh corners / {len(result['triangles'])//3} triangles, {len(names)} bones; source height {height:.3f}m."
          + (f" Left out {dropped} faces." if dropped else ""))
    print("Skinned supplied model and original texture; animation is procedural at runtime.")


if __name__ == "__main__":
    main()
