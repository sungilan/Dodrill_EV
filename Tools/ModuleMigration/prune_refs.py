#!/usr/bin/env python3
"""
asmdef 참조 정리 — 코드가 실제로 쓰는 네임스페이스의 소유 어셈블리만 남긴다.
  python prune_refs.py            # 드라이런: 어셈블리별 '현재 → 제안' 비교만 출력
  python prune_refs.py --apply    # asmdef 를 제안대로 쓴다(없으면 새로 만든다)
DoDrill.* 끼리의 참조는 ASM 표의 허용 목록(층 규칙)으로 정한다. 컴파일에서 'CS0012(참조 안 된 어셈블리 타입)' 가 나오면
그 어셈블리를 EXTRA 에 넣고 다시 돌린다(타입이 시그니처로만 노출되는 경우는 코드 스캔으로 못 잡는다).
"""
import os, re, json, sys
from collections import defaultdict
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SCRIPT = os.path.join(REPO, 'Assets', '01_Script')

LOWER = ['DoDrill.Common', 'DoDrill.Interaction', 'DoDrill.Avatar', 'DoDrill.Engine']
# 어셈블리 이름 : (폴더, 참조할 수 있는 DoDrill.* 어셈블리)  — 아래층만
ASM = {
    'DoDrill.Common': ('Common', []),
    'DoDrill.Interaction': ('Interaction', ['DoDrill.Common']),
    'DoDrill.Avatar': ('Avatar', ['DoDrill.Common', 'DoDrill.Interaction']),
    'DoDrill.Engine': ('Engine', ['DoDrill.Common', 'DoDrill.Interaction']),
    'DoDrill.Solo': ('Solo', []),
    'DoDrill.Content.Base': ('Content/Base', LOWER),
    'DoDrill.Content.Core': ('Content/Core', LOWER + ['DoDrill.Content.Base']),
    'DoDrill.Content.Shell': ('Content/Shell', LOWER + ['DoDrill.Solo', 'DoDrill.Content.Base']),
    'DoDrill.Content.Features': ('Content/Features', LOWER + ['DoDrill.Solo', 'DoDrill.Content.Base', 'DoDrill.Content.Core', 'DoDrill.Content.Shell']),
}
# 네임스페이스 이름이 우연히 겹쳐 오탐이 나는 어셈블리
EXCLUDE = {'Unity.VisualScripting.Core', 'Unity.VisualScripting.Flow', 'Unity.VisualScripting.State', 'Unity.InternalAPIEngineBridge.004'}
# 코드 스캔으로 안 잡히지만 필요한 것(컴파일 오류로 확인된 것들) — Autohand 네임스페이스는 asmdef 가 여러 개라 스캔이 못 고른다
CONTENT_EXTRA = ['QuickOutline', 'LomoCamera', 'Unity.ResourceManager', 'Unity.InputSystem', 'Unity.XR.Interaction.Toolkit']
EXTRA = {
    'DoDrill.Common': ['AutoHandAssembly', 'Unity.XR.OpenXR'],
    'DoDrill.Avatar': ['Unity.InputSystem'],
}
# 예전 단일 Content 어셈블리가 컴파일되던 외부 참조 — 새로 나눈 Content.* 의 출발점(스캔에 잡히면 유지)
CONTENT_PREV = ['AYellowpaper.SerializedCollections', 'AutoHandAssembly', 'Autohand.FinalIK', 'Autohand.OpenXR', 'Autohand.XR', 'DOTween.Modules', 'EPO', 'FishNet.Runtime',
                'LomoCamera', 'MasterServerToolkit', 'QuickOutline', 'RootMotion', 'Unity.Addressables', 'Unity.InputSystem', 'Unity.Localization',
                'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Universal.Runtime', 'Unity.TextMeshPro', 'Unity.XR.Interaction.Toolkit', 'UnityEngine.UI', 'Unity.ResourceManager']
for k in ('DoDrill.Content.Base', 'DoDrill.Content.Core', 'DoDrill.Content.Shell', 'DoDrill.Content.Features'):
    EXTRA[k] = list(CONTENT_EXTRA)


def strip(s):
    s = re.sub(r'/\*.*?\*/', ' ', s, flags=re.S)
    s = re.sub(r'//[^\n]*', ' ', s)
    return re.sub(r'"(?:\\.|[^"\\])*"', '""', s)


def asmdef_map():
    asm_dirs = {}
    for base in ('Assets', 'Packages', 'Library/PackageCache'):
        for d, _, fs in os.walk(os.path.join(REPO, base)):
            for f in fs:
                if f.endswith('.asmdef'):
                    try:
                        j = json.load(open(os.path.join(d, f), encoding='utf-8-sig'))
                        nm = j.get('name')
                        if nm and not nm.startswith('DoDrill.') and not any(x in nm for x in ('Tests', 'Test', 'Editor', 'Samples', 'Sample', 'Demo', 'Example')):
                            asm_dirs[os.path.normcase(d)] = nm
                    except Exception:
                        pass
    ns_re = re.compile(r'^\s*namespace\s+([A-Za-z_][\w.]*)', re.M)
    owner = defaultdict(set)
    for d in sorted(asm_dirs, key=len, reverse=True):
        nm = asm_dirs[d]
        for root, dd, fs in os.walk(d):
            if os.path.normcase(root) != d and os.path.normcase(root) in asm_dirs:
                dd[:] = []; continue
            for f in fs:
                if f.endswith('.cs'):
                    try:
                        t = open(os.path.join(root, f), encoding='utf-8-sig', errors='replace').read(8000)
                    except Exception:
                        continue
                    for m in ns_re.finditer(t): owner[m.group(1)].add(nm)
    return owner


def own_files(folder):
    """이 어셈블리 폴더의 .cs (안쪽에 다른 DoDrill asmdef 폴더가 있으면 그쪽은 제외)"""
    base = os.path.join(SCRIPT, folder.replace('/', os.sep))
    inner = [os.path.join(SCRIPT, f.replace('/', os.sep)) for _, (f, _) in ASM.items() if f != folder and f.startswith(folder + '/')]
    for root, dd, fs in os.walk(base):
        dd[:] = [x for x in dd if os.path.join(root, x) not in inner]
        for f in fs:
            if f.endswith('.cs'): yield os.path.join(root, f)


def main():
    apply = '--apply' in sys.argv
    owner = asmdef_map()
    tok_re = re.compile(r'[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+')
    using1 = re.compile(r'^\s*using\s+(?:static\s+)?([A-Za-z_]\w*)\s*;', re.M)   # 점 없는 한 단어 네임스페이스(Autohand, TMPro …)
    for name, (folder, internal) in ASM.items():
        used = defaultdict(set)
        for p in own_files(folder):
            s = strip(open(p, 'rb').read().decode('utf-8-sig', 'replace'))
            for tk in [m.group(0) for m in tok_re.finditer(s)] + [m.group(1) for m in using1.finditer(s)]:
                parts = tk.split('.')
                for k in range(len(parts), 0, -1):
                    ns = '.'.join(parts[:k])
                    if ns in owner:
                        for o in owner[ns]: used[o].add((ns, len(owner[ns]) == 1))   # (네임스페이스, 소유 어셈블리가 하나뿐인가)
                        break
        p = os.path.join(SCRIPT, folder.replace('/', os.sep), name + '.asmdef')
        if os.path.exists(p):
            j = json.load(open(p, encoding='utf-8'))
            cur_ext = [r for r in j['references'] if not r.startswith('DoDrill.')]
            if name.startswith('DoDrill.Content.'): cur_ext = sorted(set(cur_ext) | set(CONTENT_PREV))   # 파일이 층 사이를 옮겨 다니므로 항상 출발점에서 다시 고른다
        else:
            j = {'name': name, 'rootNamespace': '', 'references': [], 'includePlatforms': [], 'excludePlatforms': [], 'allowUnsafeCode': False,
                 'overrideReferences': False, 'precompiledReferences': [], 'autoReferenced': True, 'defineConstraints': [], 'versionDefines': [], 'noEngineReferences': False}
            cur_ext = list(CONTENT_PREV) if name.startswith('DoDrill.Content.') else []
        # 이미 컴파일되던 참조는 스캔에 잡히면 유지, 새로 넣는 것은 네임스페이스 소유자가 하나뿐일 때만(여러 어셈블리가 같은 네임스페이스를 쓰면 오탐)
        found = [r for r, evid in used.items() if r in cur_ext or any(u for _, u in evid)]
        sug_ext = sorted((set(found) - EXCLUDE - set(ASM)) | set(EXTRA.get(name, [])))
        sug = list(internal) + sug_ext
        print('== %s: 외부 %d → %d, 내부 %d' % (name, len(cur_ext), len(sug_ext), len(internal)))
        if apply:
            j['references'] = sug
            os.makedirs(os.path.dirname(p), exist_ok=True)
            json.dump(j, open(p, 'w', encoding='utf-8'), indent=2)
    if not apply: print('\n드라이런입니다. --apply 로 덮어씁니다.')


if __name__ == '__main__':
    main()
