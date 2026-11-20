#!/usr/bin/env python3
"""
모듈화 3단계 — asmdef 전환 도구 (2026-11-20)

층 규칙: Common < Interaction < Avatar < Engine < Content  (assignment.json 이 파일마다 어셈블리를 정해 둔다)
  python migrate.py            # 드라이런(기본): 아무것도 바꾸지 않고 무엇을 할지 보여 준다
  python migrate.py --check    # asmdef 가 참조하는 이름(플러그인 어셈블리)이 프로젝트에 실제로 있는지 확인
  python migrate.py --apply    # 실제로 파일을 옮기고(git mv, .meta 함께) asmdef 를 만든다

주의
  · Unity 가 빌드 중이거나 플레이 중일 때 --apply 를 돌리지 말 것(재컴파일이 끼어든다).
  · --apply 는 반드시 전용 브랜치에서, 깨끗한 작업 트리(또는 커밋 직후)에서 돌릴 것. 돌린 뒤 Unity 가 컴파일하며 asmdef 오류를 낸다 — docs 의
    '막힘 요소' 순서대로 고친다.
  · 이동은 폴더 구조를 유지한 채 앞에 어셈블리 폴더를 붙인다(예: Equipment/X.cs → Content/Equipment/X.cs). 폴더 정리는 나중에.
"""
import argparse, json, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
ASSETS = os.path.join(REPO, 'Assets')
SCRIPT = os.path.join(ASSETS, '01_Script')
ROOTDIR = {'Common': 'Common', 'Interaction': 'Interaction', 'Avatar': 'Avatar', 'Engine': 'Engine', 'Content': 'Content'}
ASMNAME = {a: 'DoDrill.' + a for a in ROOTDIR}
INTERNAL = {'Common': [], 'Interaction': ['Common'], 'Avatar': ['Common', 'Interaction'],
            'Engine': ['Common', 'Interaction', 'Avatar'], 'Content': ['Common', 'Interaction', 'Avatar', 'Engine']}

# asmdef 가 없는 스크립트 폴더 — 우리 코드가 직접 참조하는 것들
EXTERNAL_TO_CONTENT = [  # (원래 폴더, 새 위치) — 콘텐츠(UI·EV)와 서로 얽혀 있어 Content 안으로 옮긴다
    ('Inventory/Scripts', 'Content/External/Inventory'),
    ('MiniMap/_Scripts', 'Content/External/MiniMap'),
    ('Scripts/Lobbies', 'Content/External/ScriptsLobbies'),
]
EXTERNAL_ASMDEF_IN_PLACE = [  # 제자리에 asmdef 만 만든다(서로 의존이 없는 잎)
    ('QuickOutline/Scripts', 'QuickOutline', []),
    ('ThirtParty/Monqo Lomo Camera', 'LomoCamera', []),
]
# DOTween 은 asmdef 가 없어 확장 메서드(DOFade 등)를 asmdef 코드가 못 본다 → 공식 방식대로 모듈 asmdef 를 만든다(검증 필요)
DOTWEEN_ASMDEFS = [
    ('Plugins/Demigiant/DOTween/Modules', 'DOTween.Modules',
     {'references': ['UnityEngine.UI', 'Unity.TextMeshPro', 'EPO'], 'overrideReferences': True, 'precompiledReferences': ['DOTween.dll']}),
]

PLUGIN_NAME_FIX = {  # plan 의 표기 → 실제 asmdef 이름
    'Autohand': 'AutoHandAssembly',      # Assets/Packages/AutoHand/Scripts/Autohand.asmdef 의 name
    'RootMotion.Runtime': 'RootMotion',  # Assets/Plugins/RootMotion/RootMotion.Runtime.asmdef 의 name
    'DOTween(asmdef 없음 — 생성 필요)': 'DOTween.Modules',
    'Sirenix(미리 컴파일된 dll — 자동 참조)': None,
    '(에디터 전용 — #if UNITY_EDITOR 안에서만)': None,
}


def load():
    with open(os.path.join(HERE, 'assignment.json'), encoding='utf-8') as f:
        return json.load(f)


def git(*args):
    return subprocess.run(['git', '-C', REPO] + list(args), capture_output=True, text=True)


def find_asmdef_names():
    names = {}
    for base in ('Assets', 'Packages', os.path.join('Library', 'PackageCache')):
        for d, _, fs in os.walk(os.path.join(REPO, base)):
            for f in fs:
                if f.endswith('.asmdef'):
                    try:
                        j = json.load(open(os.path.join(d, f), encoding='utf-8-sig'))
                        names[j.get('name')] = os.path.relpath(os.path.join(d, f), REPO)
                    except Exception:
                        pass
    return names


def asmdef_json(asm, plugin_refs, extra=None):
    refs = [ASMNAME[i] for i in INTERNAL[asm]]
    for p in plugin_refs:
        n = PLUGIN_NAME_FIX.get(p, p)
        if n and n not in refs: refs.append(n)
    if asm == 'Content':
        for n in ('QuickOutline', 'LomoCamera'):
            if n not in refs: refs.append(n)
    j = {'name': ASMNAME[asm], 'rootNamespace': '', 'references': refs, 'includePlatforms': [], 'excludePlatforms': [],
         'allowUnsafeCode': False, 'overrideReferences': False, 'precompiledReferences': [], 'autoReferenced': True,
         'defineConstraints': [], 'versionDefines': [], 'noEngineReferences': False}
    if extra: j.update(extra)
    return j


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true')
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    data = load()
    assign = data['assign']
    refs = data['refs']

    if a.check:
        names = find_asmdef_names()
        bad = []
        for asm in ROOTDIR:
            for p in refs.get(asm, {}):
                n = PLUGIN_NAME_FIX.get(p, p)
                if n and n not in names and n != 'DOTween.Modules':
                    bad.append((asm, n))
        print('프로젝트 asmdef 이름 %d개 확인' % len(names))
        print('없는 참조:', bad if bad else '없음')
        return

    moves = []
    for rel, asm in sorted(assign.items()):
        src = os.path.join(SCRIPT, rel.replace('/', os.sep))
        dst = os.path.join(SCRIPT, ROOTDIR[asm], rel.replace('/', os.sep))
        moves.append((src, dst, asm))
    # 배정 목록에 없는 새 .cs(Editor 폴더 제외)는 Content 로 — 목록을 만든 뒤에 추가된 파일이 Assembly-CSharp 에 남아 컴파일이 깨지는 것을 막는다
    listed = {os.path.normcase(os.path.join(SCRIPT, r.replace('/', os.sep))) for r in assign}
    ed = os.path.normcase(os.path.join(SCRIPT, 'Editor')) + os.sep
    skip_roots = tuple(os.path.normcase(os.path.join(SCRIPT, x)) + os.sep for x in ROOTDIR.values())
    for d, _, fs in os.walk(SCRIPT):
        for f in fs:
            if not f.endswith('.cs'): continue
            src = os.path.join(d, f)
            k = os.path.normcase(src)
            if k in listed or k.startswith(ed) or k.startswith(skip_roots): continue
            rel = os.path.relpath(src, SCRIPT)
            moves.append((src, os.path.join(SCRIPT, 'Content', rel), 'Content'))
    for old, new in EXTERNAL_TO_CONTENT:
        sdir = os.path.join(ASSETS, old.replace('/', os.sep))
        if not os.path.isdir(sdir): continue
        for d, _, fs in os.walk(sdir):
            for f in fs:
                if f.endswith('.cs'):
                    src = os.path.join(d, f)
                    dst = os.path.join(SCRIPT, new.replace('/', os.sep), os.path.relpath(src, sdir))
                    moves.append((src, dst, 'Content'))

    counts = {}
    for _, _, asm in moves: counts[asm] = counts.get(asm, 0) + 1
    print('이동할 .cs: %d개 %s' % (len(moves), counts))
    missing = [m[0] for m in moves if not os.path.exists(m[0])]
    print('원본이 없는 항목(이미 이동했거나 이름이 바뀜):', len(missing))
    for m in missing[:10]: print('   -', os.path.relpath(m, REPO))
    print('만들 asmdef:')
    for asm in ROOTDIR:
        print('   ', ASMNAME[asm], '→', 'Assets/01_Script/%s/' % ROOTDIR[asm], json.dumps(asmdef_json(asm, refs.get(asm, {}))['references'], ensure_ascii=False))
    for folder, name, _ in EXTERNAL_ASMDEF_IN_PLACE: print('   ', name, '→', 'Assets/' + folder)
    for folder, name, _ in DOTWEEN_ASMDEFS: print('   ', name, '→', 'Assets/' + folder, '(검증 필요)')
    if not a.apply:
        print('\n드라이런입니다. 실제로 옮기려면 --apply (Unity 가 꺼져 있는지, 브랜치·작업 트리가 깨끗한지 확인).')
        return

    st = git('status', '--porcelain')
    if st.stdout.strip():
        print('작업 트리가 깨끗하지 않습니다 — 먼저 커밋하세요.\n' + st.stdout[:500]); sys.exit(1)

    for src, dst, asm in moves:
        if not os.path.exists(src): continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        r = git('mv', src, dst)
        if r.returncode: print('mv 실패', src, r.stderr.strip()); sys.exit(1)
        if os.path.exists(src + '.meta'):
            r = git('mv', src + '.meta', dst + '.meta')
            if r.returncode: print('meta mv 실패', src, r.stderr.strip()); sys.exit(1)
    # 비어 버린 옛 폴더(.meta 만 남음)는 Unity 가 열 때 경고하므로 필요하면 git rm 으로 정리한다
    for asm in ROOTDIR:
        p = os.path.join(SCRIPT, ROOTDIR[asm], ASMNAME[asm] + '.asmdef')
        os.makedirs(os.path.dirname(p), exist_ok=True)
        json.dump(asmdef_json(asm, refs.get(asm, {})), open(p, 'w', encoding='utf-8'), indent=2)
    for folder, name, extra_refs in EXTERNAL_ASMDEF_IN_PLACE:
        p = os.path.join(ASSETS, folder.replace('/', os.sep), name + '.asmdef')
        j = {'name': name, 'references': extra_refs, 'autoReferenced': True}
        json.dump(j, open(p, 'w', encoding='utf-8'), indent=2)
    for folder, name, extra in DOTWEEN_ASMDEFS:
        p = os.path.join(ASSETS, folder.replace('/', os.sep), name + '.asmdef')
        j = {'name': name, 'autoReferenced': True}
        j.update(extra)
        json.dump(j, open(p, 'w', encoding='utf-8'), indent=2)
    print('완료. Unity 를 열어 컴파일 오류를 하나씩 해결하세요(계획서의 막힘 요소 순서).')


if __name__ == '__main__':
    main()
