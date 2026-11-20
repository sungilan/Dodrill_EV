#!/usr/bin/env python3
"""
로비 모듈화 — EV 로비 코드 30개를 Assets/01_Script/Lobby (DoDrill.Lobby) 로 옮긴다(git mv, .meta 함께 → GUID 유지).
  python lobby_moves.py            # 드라이런
  python lobby_moves.py --apply
UIManager 는 차량·Info 등 EV 전용을 써서 옮기지 않는다(범용_로비_설계.md 5.2).
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
A = os.path.join(REPO, 'Assets', '01_Script')
C = 'Content/'

# (원본 상대 경로(01_Script 기준), 목적지 폴더(Lobby/ 아래))
MOVES = [
    # 로그인·상태
    (C + 'Shell/Lobby/LoginPanel.cs', 'Login'),
    (C + 'Shell/Lobby/ClientState.cs', 'Login'),
    (C + 'Shell/Lobby/MasterReconnectGuard.cs', 'Login'),
    # 방 목록·만들기·대기실·팩토리
    (C + 'Shell/Lobby/LobbyListPanel.cs', 'Rooms'),
    (C + 'Shell/Lobby/CreateNewLobbyPanel.cs', 'Rooms'),
    (C + 'Shell/Lobby/DefaultFactoriesModule.cs', 'Rooms'),
    (C + 'Shell/Lobby/MyLobbyFactoryModule.cs', 'Rooms'),
    (C + 'Shell/Lobby/MatchView.cs', 'Rooms'),
    (C + 'Base/Lobby/LobbyPanel.cs', 'Rooms'),
    (C + 'Base/Lobby/LobbyBottomPanel.cs', 'Rooms'),
    (C + 'Base/Lobby/LobbyPlayerInfos.cs', 'Rooms'),
    # 메뉴(시작·모드·시나리오 목록·팝업)
    (C + 'Shell/Menu/StartPanel.cs', 'Menu'),
    (C + 'Shell/Menu/LobbyModeSelector.cs', 'Menu'),
    (C + 'Shell/Menu/ModeSelectUI.cs', 'Menu'),
    (C + 'Shell/Menu/ScenarioList/ScenarioListPanel.cs', 'Menu/ScenarioList'),
    (C + 'Shell/Menu/ScenarioList/ScenarioListCardUI.cs', 'Menu/ScenarioList'),
    (C + 'Base/Menu/ScenarioList/ScenarioDataLoader.cs', 'Menu/ScenarioList'),
    (C + 'Shell/Menu/Popups/Popup_LogOutPanel.cs', 'Menu/Popups'),
    (C + 'Shell/Menu/Popups/Popup_ExitPanel.cs', 'Menu/Popups'),
    # 언어·로딩
    (C + 'Shell/Menu/Language/LanguageButtonManager.cs', 'Language'),
    (C + 'Shell/Menu/Language/LanguageSelectionPanel.cs', 'Language'),
    (C + 'Shell/Menu/Language/Panel_LanguageToggle.cs', 'Language'),
    (C + 'Shell/Menu/Loading/LoadingBgFakeLoading.cs', 'Loading'),
    (C + 'Shell/Menu/Loading/LoadingBgVisibilityBinder.cs', 'Loading'),
    (C + 'Shell/Menu/Loading/PersistentClientLoadingRoot.cs', 'Loading'),
    # 앱(룸 서버의 시나리오 로더·세션 데이터·씬 전환 막)
    (C + 'Shell/App/ScenarioAutoLoader.cs', 'App'),
    (C + 'Shell/App/SessionData.cs', 'App'),
    (C + 'Shell/App/GameLoadCurtain.cs', 'App'),
    (C + 'Shell/Lobby/PlatformDetector.cs', 'App'),
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
    plan = [(os.path.join(A, s.replace('/', os.sep)), os.path.join(A, 'Lobby', d.replace('/', os.sep), os.path.basename(s))) for s, d in MOVES]
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
