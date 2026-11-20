#!/usr/bin/env python3
"""
feature_map.py 의 배정대로 .cs 를 기능 폴더로 옮긴다(어셈블리 경계 불변, .meta 함께 git mv).
  python reorg.py          # 드라이런(옮길 목록·검증만)
  python reorg.py --apply  # 실제 이동 + 비어 버린 옛 폴더 정리
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, sys, subprocess, collections
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from feature_map import MAP, KEEP_PREFIXES
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SCRIPT = os.path.join(REPO, 'Assets', '01_Script')
ASMS = ['Common', 'Interaction', 'Avatar', 'Engine', 'Content', 'Solo']

dest_of = {}
for folder, names in MAP.items():
    for n in names:
        if n in dest_of: sys.exit('배정표에 중복: ' + n)
        dest_of[n] = folder

moves, errors = [], []
seen = set()
for a in ASMS:
    for d, _, fs in os.walk(os.path.join(SCRIPT, a)):
        for f in fs:
            if not f.endswith('.cs'): continue
            full = os.path.join(d, f)
            rel = os.path.relpath(full, SCRIPT).replace('\\', '/')
            if any(rel.startswith(k) for k in KEEP_PREFIXES): continue
            name = f[:-3]
            if name in seen: errors.append('같은 이름 파일이 둘: ' + name)
            seen.add(name)
            tgt = dest_of.get(name)
            if tgt is None: errors.append('배정표에 없음: ' + rel); continue
            if tgt.split('/')[0] != a: errors.append('어셈블리가 다름: %s (현재 %s → %s)' % (name, a, tgt)); continue
            new = SCRIPT + '/' + tgt + '/' + f
            if os.path.normcase(os.path.abspath(new)) != os.path.normcase(os.path.abspath(full)):
                moves.append((full, new))
missing = [n for n in dest_of if n not in seen]
for n in missing: errors.append('배정표에는 있는데 파일이 없음: ' + n)

per = collections.Counter(dest_of[os.path.basename(m[0])[:-3]].split('/')[0] + '/' + (dest_of[os.path.basename(m[0])[:-3]].split('/')[1] if '/' in dest_of[os.path.basename(m[0])[:-3]] else '') for m in moves)
print('이동할 파일 %d개, 오류 %d' % (len(moves), len(errors)))
for e in errors: print('  !', e)
if errors: sys.exit(1)
if '--apply' not in sys.argv:
    for k, v in sorted(per.items()): print('  %-28s %d' % (k, v))
    print('드라이런입니다. 실제로 옮기려면 --apply')
    sys.exit(0)


def git(*a):
    return subprocess.run(['git', '-C', REPO] + list(a), capture_output=True, text=True)


old_dirs = set()
for src, dst in moves:
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    old_dirs.add(os.path.dirname(src))
    for s, d in ((src, dst), (src + '.meta', dst + '.meta')):
        if not os.path.exists(s): continue
        r = git('mv', s, d)
        if r.returncode:
            sys.exit('git mv 실패: %s → %s\n%s' % (s, d, r.stderr))

# 비어 버린 옛 폴더(.meta 만 남은 것 포함) 정리 — 아래에서 위로
removed = 0
for d in sorted(old_dirs, key=len, reverse=True):
    cur = d
    while cur.startswith(SCRIPT) and cur != SCRIPT:
        entries = [e for e in os.listdir(cur)] if os.path.isdir(cur) else None
        if entries is None: break
        if entries: break   # 아직 뭔가 있음
        os.rmdir(cur)
        meta = cur + '.meta'
        if os.path.exists(meta):
            r = git('rm', '-q', '-f', meta)
            if r.returncode: os.remove(meta)
        removed += 1
        cur = os.path.dirname(cur)
print('완료: 이동 %d개, 비운 폴더 %d개 정리' % (len(moves), removed))
