import json, struct, sys, math

CT = {5120:('b',1),5121:('B',1),5122:('h',2),5123:('H',2),5125:('I',4),5126:('f',4)}
NC = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}

def load(path):
    d = open(path,'rb').read()
    assert d[:4]==b'glTF'
    off=12; js=None; bin_=None
    while off < len(d):
        ln, ty = struct.unpack_from('<II', d, off); off+=8
        ch = d[off:off+ln]; off+=ln
        if ty==0x4E4F534A: js=json.loads(ch.decode('utf-8').rstrip('\x00 '))
        else: bin_=ch
    return js, bin_

def acc(js, bin_, i):
    a = js['accessors'][i]
    fmt, sz = CT[a['componentType']]; nc = NC[a['type']]
    bv = js['bufferViews'][a['bufferView']]
    stride = bv.get('byteStride') or nc*sz
    base = bv.get('byteOffset',0) + a.get('byteOffset',0)
    out=[]
    for k in range(a['count']):
        o = base + k*stride
        out.append(struct.unpack_from('<'+fmt*nc, bin_, o))
    return out

def main(pa, pb):
    ja, ba = load(pa); jb, bb = load(pb)
    ma, mb = ja['meshes'][0], jb['meshes'][0]
    print(f"primitives: {len(ma['primitives'])} vs {len(mb['primitives'])}")
    # joint remap: map cue joint index -> name, fm joint index -> name
    jna = [ja['nodes'][n].get('name') for n in ja['skins'][0]['joints']]
    jnb = [jb['nodes'][n].get('name') for n in jb['skins'][0]['joints']]
    print("joint name sets equal:", sorted(map(str,jna))==sorted(map(str,jnb)))
    print("joint order equal:", jna==jnb)
    remap = {}
    idxb = {n:i for i,n in enumerate(jnb)}
    for i,n in enumerate(jna):
        remap[i] = idxb.get(n)
    tot={}
    for p in range(len(ma['primitives'])):
        pa_, pb_ = ma['primitives'][p], mb['primitives'][p]
        for attr in ['POSITION','NORMAL','TANGENT','TEXCOORD_0','COLOR_0','JOINTS_0','WEIGHTS_0']:
            if attr not in pa_['attributes'] or attr not in pb_['attributes']: continue
            va = acc(ja, ba, pa_['attributes'][attr]); vb = acc(jb, bb, pb_['attributes'][attr])
            if len(va)!=len(vb):
                print(f"  prim{p} {attr}: COUNT {len(va)} vs {len(vb)}"); continue
            if attr=='JOINTS_0':
                nd = sum(1 for x,y in zip(va,vb) if tuple(remap.get(c,c) for c in x)!=tuple(y))
                d = tot.setdefault(attr,[0,0,0.0]); d[0]+=nd; d[1]+=len(va)
                continue
            mx=0.0; nd=0
            for x,y in zip(va,vb):
                dd = max(abs(p1-p2) for p1,p2 in zip(x,y))
                if dd>0: nd+=1
                if dd>mx: mx=dd
            d = tot.setdefault(attr,[0,0,0.0]); d[0]+=nd; d[1]+=len(va); d[2]=max(d[2],mx)
        ia = acc(ja, ba, pa_['indices']); ib = acc(jb, bb, pb_['indices'])
        d = tot.setdefault('INDICES',[0,0,0.0])
        d[1]+=len(ia); d[0]+= (0 if ia==ib else sum(1 for x,y in zip(ia,ib) if x!=y))
    print()
    for k,(nd,n,mx) in tot.items():
        print(f"  {k:<12} differing {nd}/{n} ({100.0*nd/max(n,1):.2f}%)  maxAbsDelta={mx}")

main(sys.argv[1], sys.argv[2])
