#!/usr/bin/env python3
"""
프리팹 모듈화 — 모듈 소유 프리팹·전용 에셋을 모듈 폴더로 옮긴다(git mv, .meta 함께 → GUID 유지, 씬·프리팹 연결 그대로).
  python prefab_moves.py            # 드라이런
  python prefab_moves.py --apply    # 실제 이동
규칙
  · <모듈>/Prefabs/…  : 모듈이 쓰는 프리팹(코드가 직접 부르지 않고 씬·프리팹이 GUID 로 참조하는 것)
  · <모듈>/Resources/ : Resources.Load("이름") 으로 불리는 것 — Resources 폴더 안 상대 경로를 그대로 둔다
  · <모듈>/Art/…      : 그 모듈 프리팹만 쓰는 스프라이트(폰트는 공용이라 Common/Fonts)
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
A = os.path.join(REPO, 'Assets')
S = '01_Script/'

MOVES = [
    # ── Common ──
    ('02_UI/02_Prefabs/OnScreenKeyboard-AllKey.prefab', S + 'Common/Prefabs/Keyboard/OnScreenKeyboard-AllKey.prefab'),
    ('02_UI/02_Prefabs/OnScreenKeyboard_10keyless.prefab', S + 'Common/Prefabs/Keyboard/OnScreenKeyboard_10keyless.prefab'),
    ('02_UI/02_Prefabs/VRKeyboard.prefab', S + 'Common/Prefabs/Keyboard/VRKeyboard.prefab'),
    ('07_Resources/Prefabs/UI/Lobby/VRKeyboard.prefab', S + 'Common/Prefabs/Keyboard/Lobby/VRKeyboard.prefab'),
    ('07_Resources/Prefabs/Anchor/SharedAnchorPrefab.prefab', S + 'Common/Prefabs/Anchor/SharedAnchorPrefab.prefab'),
    ('Resources/BuildCompatNotice.prefab', S + 'Common/Resources/BuildCompatNotice.prefab'),     # Resources.Load("BuildCompatNotice")
    ('02_UI/01_Sprites/arrow.png', S + 'Common/Art/Sprites/Keyboard/arrow.png'),
    ('02_UI/01_Sprites/backspace.png', S + 'Common/Art/Sprites/Keyboard/backspace.png'),
    # ── Ui ──
    ('02_UI/02_Prefabs/Preb_TaskItem.prefab', S + 'Ui/Prefabs/Preb_TaskItem.prefab'),
    ('03_3D/Object/O_Prefabs/WorldHoverLabel.prefab', S + 'Ui/Prefabs/WorldHoverLabel.prefab'),
    ('Resources/ItemNameTagLabel.prefab', S + 'Ui/Resources/ItemNameTagLabel.prefab'),            # Resources.Load("ItemNameTagLabel") 18곳
    ('02_UI/01_Sprites/InGameNew/training_section_checklist.png', S + 'Ui/Art/Sprites/training_section_checklist.png'),
    ('02_UI/01_Sprites/InGameNew/check_on.png', S + 'Ui/Art/Sprites/check_on.png'),
    ('02_UI/01_Sprites/InGameNew/check_off.png', S + 'Ui/Art/Sprites/check_off.png'),
    # ── Avatar ──
    ('03_3D/Character/C_Prefabs/PcAvatar_Mechanic.prefab', S + 'Avatar/Prefabs/Avatars/PcAvatar_Mechanic.prefab'),
    ('03_3D/Character/C_Prefabs/PcAvatar_Swat.prefab', S + 'Avatar/Prefabs/Avatars/PcAvatar_Swat.prefab'),
    ('07_Resources/Prefabs/Player/VR_Player.prefab', S + 'Avatar/Prefabs/Players/VR_Player.prefab'),   # EV 스크립트 없는 VR 플레이어(Ui·Safety 모듈 스크립트 포함)
    # ── Content (EV 조합 플레이어) ──
    ('07_Resources/Prefabs/Player/PC_Player.prefab', S + 'Content/Prefabs/Players/PC_Player.prefab'),
    ('07_Resources/Prefabs/Player/Mobile_Player.prefab', S + 'Content/Prefabs/Players/Mobile_Player.prefab'),
    ('07_Resources/Prefabs/Player/NetworkPlayer.prefab', S + 'Content/Prefabs/Players/NetworkPlayer.prefab'),
    ('03_3D/Character/C_Prefabs/VR_Player(EV).prefab', S + 'Content/Prefabs/Players/VR_Player(EV).prefab'),
]
# 폴더째 옮김 — 05_Font 전체(Pretendard·DS-DIGI 폰트와 SDF 에셋)는 모든 모듈 UI 가 쓰는 공용이라 Common/Fonts
FOLDER_MOVES = [('05_Font', S + 'Common/Fonts')]


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
    plan = []
    for s, d in MOVES:
        plan.append((os.path.join(A, s.replace('/', os.sep)), os.path.join(A, d.replace('/', os.sep))))
    for s, d in FOLDER_MOVES:
        base = os.path.join(A, s.replace('/', os.sep))
        for f in sorted(os.listdir(base)):
            if f.endswith('.meta'): continue
            plan.append((os.path.join(base, f), os.path.join(A, d.replace('/', os.sep), f)))
    miss = [s for s, _ in plan if not os.path.exists(s)]
    clash = [d for _, d in plan if os.path.exists(d)]
    print('이동 %d개, 원본 없음 %d, 목적지 중복 %d' % (len(plan), len(miss), len(clash)))
    for m in miss: print('  없음', m)
    for c in clash: print('  중복', c)
    if miss or clash: sys.exit(1)
    if not apply:
        print('드라이런입니다. --apply 로 이동'); return
    for s, d in plan: mv(s, d)
    # 비어 버린 05_Font 폴더(.meta 만 남음) 정리
    base = os.path.join(A, '05_Font')
    if os.path.isdir(base) and all(n.endswith('.meta') for n in os.listdir(base)):
        for n in os.listdir(base):
            p = os.path.join(base, n); git('rm', '-q', '-f', p)
            if os.path.exists(p): os.remove(p)
        os.rmdir(base)
        if os.path.exists(base + '.meta'):
            git('rm', '-q', '-f', base + '.meta')
            if os.path.exists(base + '.meta'): os.remove(base + '.meta')
    print('완료', len(plan))


if __name__ == '__main__':
    main()
