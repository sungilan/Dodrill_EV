import os, re, sys, json, collections
sys.setrecursionlimit(20000)
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
ROOT = os.path.join(REPO, 'Assets', '01_Script', 'Content')
LAYER_DIRS = ('Base', 'Core', 'Features', 'Shell')   # 이미 나눈 뒤에도 다시 돌릴 수 있게: 이 폴더 이름은 경로에서 벗긴다
BS = chr(92)

def strip(s):
    s = re.sub(r'/\*.*?\*/', ' ', s, flags=re.S)
    s = re.sub(r'//[^\n]*', ' ', s)
    return re.sub(r'"(?:' + BS + BS + r'.|[^"' + BS + BS + r'])*"', '""', s)

files = {}
REAL = {}
for d, _, fs in os.walk(ROOT):
    for f in fs:
        if f.endswith('.cs'):
            p = os.path.join(d, f)
            rel = os.path.relpath(p, ROOT).replace(BS, '/')
            if rel.split('/')[0] in LAYER_DIRS: rel_key = '/'.join(rel.split('/')[1:])
            else: rel_key = rel
            REAL[rel_key] = rel
            files[rel_key] = strip(open(p, 'rb').read().decode('utf-8-sig', 'replace'))

# partial 클래스는 한 덩어리(같이 움직여야 한다)
parent = {f: f for f in files}
def find(x):
    while parent[x] != x:
        parent[x] = parent[parent[x]]; x = parent[x]
    return x
partial_owner = collections.defaultdict(list)
for rel, s in files.items():
    for m in re.finditer(r'\bpartial\s+(?:class|struct|interface)\s+([A-Za-z_]\w*)', s):
        partial_owner[m.group(1)].append(rel)
for t, fl in partial_owner.items():
    for f in fl[1:]:
        parent[find(f)] = find(fl[0])
unit_of = {f: find(f) for f in files}
units = collections.defaultdict(list)
for f, u in unit_of.items(): units[u].append(f)

owners = collections.defaultdict(set)
for rel, s in files.items():
    for m in re.finditer(r'\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)', s):
        owners[m.group(1)].add(unit_of[rel])
uniq = {t: next(iter(u)) for t, u in owners.items() if len(u) == 1 and len(t) > 3}
udeps = {}
for u, fl in units.items():
    ts = set()
    for f in fl: ts |= set(re.findall(r'\b[A-Za-z_]\w*\b', files[f]))
    udeps[u] = {uniq[t] for t in ts if t in uniq and uniq[t] != u}

# SCC
idx = {}; low = {}; st = []; on = set(); sccs = []; c = [0]
def sc(v):
    idx[v] = low[v] = c[0]; c[0] += 1; st.append(v); on.add(v)
    for w in udeps.get(v, ()):
        if w not in idx: sc(w); low[v] = min(low[v], low[w])
        elif w in on: low[v] = min(low[v], idx[w])
    if low[v] == idx[v]:
        comp = []
        while True:
            w = st.pop(); on.discard(w); comp.append(w)
            if w == v: break
        sccs.append(comp)
for v in udeps:
    if v not in idx: sc(v)
big = set(max(sccs, key=len))
R = collections.defaultdict(set)
for a, ds in udeps.items():
    for b in ds: R[b].add(a)
def reach(start, G):
    seen = set(); stack = list(start)
    while stack:
        x = stack.pop()
        for y in G.get(x, ()):
            if y not in seen: seen.add(y); stack.append(y)
    return seen
down = reach(big, udeps) - big
up = reach(big, R) - big
side = set(udeps) - big - down - up
def expand(S):
    return sorted(f for u in S for f in units[u])
out = {'Core': expand(big), 'Base': expand(down), 'Features': expand(up), 'Shell': expand(side)}
json.dump(out, open(os.path.join(HERE, 'content_layers.json'), 'w'), ensure_ascii=False, indent=1)
tot = sum(len(v) for v in out.values())
print('units', len(units), 'files', len(files), 'SCC count', len(sccs), 'biggest', len(big), 'files in core', len(out['Core']))
for k, v in out.items():
    ln = sum(files[f].count('\n') for f in v)
    print(k, len(v), 'files', ln, 'lines')
cyc = [sorted(c) for c in sccs if len(c) > 1 and set(c) != big]
print('other cycles', cyc)


# ── 적용 ─────────────────────────────────────────────
#   python content_layers.py           # 분석·드라이런
#   python content_layers.py --apply   # Content/<옛 경로> → Content/<Base|Core|Features|Shell>/<옛 경로> (git mv, .meta 함께)
if '--apply' in sys.argv:
    import subprocess
    def git(*a): return subprocess.run(['git', '-C', REPO] + list(a), capture_output=True, text=True)
    moved = 0
    for layer, fl in out.items():
        for key in fl:
            src = os.path.join(ROOT, REAL[key].replace('/', os.sep))
            dst = os.path.join(ROOT, layer, key.replace('/', os.sep))
            if os.path.normcase(src) == os.path.normcase(dst): continue
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            for a, b in ((src, dst), (src + '.meta', dst + '.meta')):
                if not os.path.exists(a): continue
                r = git('mv', a, b)
                if r.returncode:
                    if 'not under version control' in r.stderr: os.replace(a, b)   # 아직 커밋 안 된 새 파일은 그냥 옮긴다
                    else: sys.exit('git mv 실패: ' + a + ' ' + r.stderr)
            moved += 1
    print('이동', moved)
