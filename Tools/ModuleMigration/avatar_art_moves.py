#!/usr/bin/env python3
"""
아바타 아트 모듈화 — Avatar 프리팹이 쓰는 캐릭터 아트(FBX·재질·텍스처)와 VR 플레이어 전용 에셋을 Avatar 폴더로 옮긴다.
Avatar/Prefabs/ASSET_MANIFEST.md 의 '폴더 밖 에셋' 목록을 읽어 그대로 옮긴다(git mv, .meta 함께 → GUID 유지).
  python avatar_art_moves.py            # 드라이런
  python avatar_art_moves.py --apply
목적지
  Assets/03_3D/Character/C_FBX/<경로>        → Avatar/Art/Character/FBX/<경로>
  Assets/03_3D/Character/C_Materials/<경로>  → Avatar/Art/Character/Materials/<경로>
  Assets/03_3D/Character/C_Textures/<경로>   → Avatar/Art/Character/Textures/<경로>
  Assets/07_Resources/Materials/<이름>       → Avatar/Art/Vr/Materials/<이름>
  Assets/07_Resources/Textures/ModelTexture/ → Avatar/Art/Vr/Textures/
  Assets/07_Resources/Prefabs/Anchor/Anchor.prefab → Common/Prefabs/Anchor/Anchor.prefab
  Assets/Samples/…(패키지 샘플)는 옮기지 않는다 — XR Hands 패키지 설치 시 따라온다
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, re, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
A = os.path.join(REPO, 'Assets')
MANIFEST = os.path.join(A, '01_Script', 'Avatar', 'Prefabs', 'ASSET_MANIFEST.md')
S = '01_Script/'

RULES = [
    ('Assets/03_3D/Character/C_FBX/', S + 'Avatar/Art/Character/FBX/'),
    ('Assets/03_3D/Character/C_Materials/', S + 'Avatar/Art/Character/Materials/'),
    ('Assets/03_3D/Character/C_Textures/', S + 'Avatar/Art/Character/Textures/'),
    ('Assets/07_Resources/Materials/', S + 'Avatar/Art/Vr/Materials/'),
    ('Assets/07_Resources/Textures/ModelTexture/', S + 'Avatar/Art/Vr/Textures/'),
    ('Assets/07_Resources/Prefabs/Anchor/', S + 'Common/Prefabs/Anchor/'),
]


def git(*a):
    return subprocess.run(['git', '-C', REPO] + list(a), capture_output=True, text=True)


def mv(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    for a, b in ((src, dst), (src + '.meta', dst + '.meta')):
        if not os.path.exists(a):
            if a == src: sys.exit('원본 없음: ' + a)
            continue
        r = git('mv', a, b)
        if r.returncode:
            if 'not under version control' in r.stderr: os.replace(a, b)
            else: sys.exit('git mv 실패: %s\n%s' % (a, r.stderr))


def main():
    apply = '--apply' in sys.argv
    plan, skipped = [], []
    group = None
    for line in open(MANIFEST, encoding='utf-8-sig'):
        m = re.match(r'### `(Assets/[^`]*?)/` ', line)
        if m: group = m.group(1) + '/'; continue
        m = re.match(r'- `([^`]+)`  ← ', line)
        if not m or group is None: continue
        rel = group + m.group(1)
        for pre, dst in RULES:
            if rel.startswith(pre):
                plan.append((os.path.join(REPO, rel.replace('/', os.sep)), os.path.join(A, (dst + rel[len(pre):]).replace('/', os.sep))))
                break
        else:
            skipped.append(rel)
    miss = [s for s, _ in plan if not os.path.exists(s)]
    clash = [d for _, d in plan if os.path.exists(d)]
    print('이동 %d개, 건너뜀 %d, 원본 없음 %d, 목적지 중복 %d' % (len(plan), len(skipped), len(miss), len(clash)))
    for s in skipped: print('  건너뜀', s)
    for m in miss: print('  없음', m)
    for c in clash: print('  중복', c)
    if miss or clash: sys.exit(1)
    if not apply:
        print('드라이런입니다. --apply 로 이동'); return
    for s, d in plan: mv(s, d)
    print('완료', len(plan))


if __name__ == '__main__':
    main()
