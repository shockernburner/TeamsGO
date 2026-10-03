# Extracts named nodes from a glTF binary (.glb) into OBJ + MTL files Unity imports natively.
# Node rotation and scale are applied, translation dropped; each piece is re-centred with its base at y = 0.
# Usage: python3 Tools/glb2obj.py pack.glb outdir   (the pieces to extract are listed at the bottom)
import json, math, os, struct, sys

def load(path):
    b = open(path, 'rb').read()
    off, j, binchunk = 12, None, None
    while off < len(b):
        ln, typ = struct.unpack('<II', b[off:off + 8])
        data = b[off + 8: off + 8 + ln]
        if typ == 0x4E4F534A: j = json.loads(data)
        elif typ == 0x004E4942: binchunk = data
        off += 8 + ln
    return j, binchunk

COMP = {5126: ('f', 4), 5123: ('H', 2), 5125: ('I', 4), 5121: ('B', 1)}
NUM = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}

def accessor(j, binchunk, i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    fmt, size = COMP[a['componentType']]; n = NUM[a['type']]
    stride = bv.get('byteStride', size * n)
    base = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    out = []
    for k in range(a['count']):
        o = base + k * stride
        v = struct.unpack('<' + fmt * n, binchunk[o:o + size * n])
        out.append(v if n > 1 else v[0])
    return out

def qrot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    # v' = q v q*
    ix = w * vx + y * vz - z * vy; iy = w * vy + z * vx - x * vz
    iz = w * vz + x * vy - y * vx; iw = -x * vx - y * vy - z * vz
    return (ix * w + iw * -x + iy * -z - iz * -y,
            iy * w + iw * -y + iz * -x - ix * -z,
            iz * w + iw * -z + ix * -y - iy * -x)

def to_srgb(c):
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055

def export(j, binchunk, node_names, out_dir, name):
    nodes = {n.get('name'): n for n in j['nodes']}
    verts, uvs, norms, groups = [], [], [], {}
    for entry in node_names:
        nn, shift = entry if isinstance(entry, tuple) else (entry, (0, 0, 0))
        n = nodes[nn]
        q = n.get('rotation', [0, 0, 0, 1]); s = n.get('scale', [1, 1, 1])
        for p in j['meshes'][n['mesh']]['primitives']:
            pos = accessor(j, binchunk, p['attributes']['POSITION'])
            nor = accessor(j, binchunk, p['attributes']['NORMAL'])
            uv = accessor(j, binchunk, p['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in p['attributes'] else [(0, 0)] * len(pos)
            idx = accessor(j, binchunk, p['indices']) if 'indices' in p else list(range(len(pos)))
            start = len(verts)
            for v in pos:
                r = qrot(q, (v[0] * s[0], v[1] * s[1], v[2] * s[2]))
                verts.append((r[0] + shift[0], r[1] + shift[1], r[2] + shift[2]))
            for v in nor:
                r = qrot(q, (v[0] / s[0], v[1] / s[1], v[2] / s[2])); l = math.sqrt(sum(c * c for c in r)) or 1
                norms.append(tuple(c / l for c in r))
            for t in uv: uvs.append((t[0], 1 - t[1]))
            mat = j['materials'][p['material']]['name']
            g = groups.setdefault(mat, [])
            for k in range(0, len(idx), 3):
                g.append((start + idx[k] + 1, start + idx[k + 1] + 1, start + idx[k + 2] + 1))
    xs = [v[0] for v in verts]; ys = [v[1] for v in verts]; zs = [v[2] for v in verts]
    cx, cz, y0 = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2, min(ys)
    with open(os.path.join(out_dir, name + '.obj'), 'w') as f:
        f.write(f'# {name}: from Quaternius, Ultimate Modular Ruins Pack (CC0 1.0)\nmtllib {name}.mtl\n')
        for v in verts: f.write(f'v {v[0] - cx:.5f} {v[1] - y0:.5f} {v[2] - cz:.5f}\n')
        for t in uvs: f.write(f'vt {t[0]:.5f} {t[1]:.5f}\n')
        for v in norms: f.write(f'vn {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n')
        for mat, faces in groups.items():
            f.write(f'g {mat}\nusemtl {mat}\n')
            for a, b2, c in faces: f.write(f'f {a}/{a}/{a} {b2}/{b2}/{b2} {c}/{c}/{c}\n')
    with open(os.path.join(out_dir, name + '.mtl'), 'w') as f:
        for mat in groups:
            m = next(x for x in j['materials'] if x['name'] == mat)
            pbr = m.get('pbrMetallicRoughness', {})
            col = pbr.get('baseColorFactor', [1, 1, 1, 1])
            f.write(f'newmtl {mat}\nKd {to_srgb(col[0]):.4f} {to_srgb(col[1]):.4f} {to_srgb(col[2]):.4f}\nKs 0 0 0\n')
            if 'baseColorTexture' in pbr:
                img = j['images'][j['textures'][pbr['baseColorTexture']['index']]['source']]
                f.write(f'map_Kd {img["name"]}\n')
    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    print(f'{name}: {len(verts)} verts, {sum(len(g) for g in groups.values())} tris, size {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f}, materials {list(groups)}')

def export_image(j, binchunk, out_dir, image_name):
    img = next(i for i in j['images'] if i['name'] == image_name)
    bv = j['bufferViews'][img['bufferView']]
    o = bv.get('byteOffset', 0)
    open(os.path.join(out_dir, image_name), 'wb').write(binchunk[o:o + bv['byteLength']])

if __name__ == '__main__':
    glb, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    j, b = load(glb)
    pieces = {
        # The lid is modelled flat at the hinge's origin: lift it onto the box and centre it, closed.
        'Ruins_Chest':                   ['Chest_Base', ('Chest_Top', (0.0, 0.572, -0.331))],
        'Ruins_WallArchOvergrownBroken': ['Wall_ArchRound_Overgrown_Broken'],
        'Ruins_WallBroken':              ['Wall_Broken'],
        'Ruins_ColumnShort':             ['Column_Round_Short'],
        'Ruins_Pot2Broken':              ['Pot2_Broken'],
        'Ruins_Pot3Broken':              ['Pot3_Broken'],
    }
    for name, nodes in pieces.items(): export(j, b, nodes, out, name)
    export_image(j, b, out, 'Leaf_Texture.png')
