#!/usr/bin/env python3
"""
Localization 모듈화 — 로컬라이제이션 설정·로케일·공용 문자열 테이블을 Common/Localization 으로 옮긴다(git mv, .meta 함께 → GUID 유지).
  python localization_moves.py            # 드라이런
  python localization_moves.py --apply
옮기는 것: Localization Settings, 로케일 3개(en·ko·vi-VN), UI_Text(로비·공용 UI·EV 아이템 이름), TaskNames(시나리오 단계 이름)
옮기지 않는 것: InGame 테이블(Game.unity 만 사용 — EV 전용)
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
A = os.path.join(REPO, 'Assets')
S = '01_Script/Common/Localization/'
R = '07_Resources/'

MOVES = [(R + 'Localization Settings.asset', S + 'Settings/Localization Settings.asset')]
for n in ('English (en)', 'Korean (ko)', 'Vietnamese (Vietnam) (vi-VN)'):
    MOVES.append((R + n + '.asset', S + 'Locales/' + n + '.asset'))
for tbl in ('UI_Text', 'TaskNames'):
    for f in (tbl + '.asset', tbl + ' Shared Data.asset', tbl + '_en.asset', tbl + '_ko.asset', tbl + '_vi-VN.asset'):
        MOVES.append((R + f, S + 'Tables/' + tbl + '/' + f))


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
            else: sys.exit('git mv 실패: %s | %s' % (a, r.stderr))


def main():
    apply = '--apply' in sys.argv
    plan = [(os.path.join(A, s.replace('/', os.sep)), os.path.join(A, d.replace('/', os.sep))) for s, d in MOVES]
    miss = [s for s, _ in plan if not os.path.exists(s)]
    clash = [d for _, d in plan if os.path.exists(d)]
    print('이동 %d개, 원본 없음 %d, 목적지 중복 %d' % (len(plan), len(miss), len(clash)))
    for m in miss: print('  없음', m)
    for c in clash: print('  중복', c)
    if miss or clash: sys.exit(1)
    if not apply:
        print('드라이런입니다. --apply 로 이동'); return
    for s, d in plan: mv(s, d)
    print('완료', len(plan))


if __name__ == '__main__':
    main()
