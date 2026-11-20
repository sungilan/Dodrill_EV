#!/usr/bin/env python3
"""
로비 UI 모듈화 — 로비 캔버스 프리팹(UI_Canvas → LobbyCanvas)과 그것이 쓰는 로비 전용 스프라이트·부품 프리팹을 Lobby 폴더로 옮긴다
(git mv, .meta 함께 → GUID 유지, Client.unity 연결 그대로). 게임 화면(Game.unity·Canvas_PCUI·VR_UICanvas 등)도 쓰는 스프라이트 16개는 옮기지 않는다.
목록은 에셋 참조 그래프(전용/공유 분석)로 만든 것이다.
  python lobby_ui_moves.py            # 드라이런
  python lobby_ui_moves.py --apply
Unity 가 플레이·빌드 중이 아닐 때만 --apply.
"""
import os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
A = os.path.join(REPO, 'Assets')

MOVES = [
    ('02_UI/02_Prefabs/UI_Canvas.prefab', '01_Script/Lobby/Prefabs/LobbyCanvas.prefab'),
    ('07_Resources/Prefabs/UI/Lobby/LobyElement.prefab', '01_Script/Lobby/Prefabs/Parts/LobyElement.prefab'),
    ('07_Resources/Prefabs/UI/Lobby/bottomButtons.prefab', '01_Script/Lobby/Prefabs/Parts/bottomButtons.prefab'),
    ('02_UI/01_Sprites/000. Language Selection/language_settings_board_off.png', '01_Script/Lobby/Art/Sprites/000. Language Selection/language_settings_board_off.png'),
    ('02_UI/01_Sprites/000. Language Selection/language_settings_board_on.png', '01_Script/Lobby/Art/Sprites/000. Language Selection/language_settings_board_on.png'),
    ('02_UI/01_Sprites/000. Language Selection/language_settings_ic_off.png', '01_Script/Lobby/Art/Sprites/000. Language Selection/language_settings_ic_off.png'),
    ('02_UI/01_Sprites/000. Language Selection/language_settings_ic_on.png', '01_Script/Lobby/Art/Sprites/000. Language Selection/language_settings_ic_on.png'),
    ('02_UI/01_Sprites/001. Main/main_dashboard.png', '01_Script/Lobby/Art/Sprites/001. Main/main_dashboard.png'),
    ('02_UI/01_Sprites/001. Main/main_logo.png', '01_Script/Lobby/Art/Sprites/001. Main/main_logo.png'),
    ('02_UI/01_Sprites/001. Main/main_logo_underline.png', '01_Script/Lobby/Art/Sprites/001. Main/main_logo_underline.png'),
    ('02_UI/01_Sprites/002. Login/login_dashboard_popup.png', '01_Script/Lobby/Art/Sprites/002. Login/login_dashboard_popup.png'),
    ('02_UI/01_Sprites/002. Login/login_field_off.png', '01_Script/Lobby/Art/Sprites/002. Login/login_field_off.png'),
    ('02_UI/01_Sprites/002. Login/login_field_on.png', '01_Script/Lobby/Art/Sprites/002. Login/login_field_on.png'),
    ('02_UI/01_Sprites/002. Login/power_on.png', '01_Script/Lobby/Art/Sprites/002. Login/power_on.png'),
    ('02_UI/01_Sprites/003. Training Content List/logout_ic.png', '01_Script/Lobby/Art/Sprites/003. Training Content List/logout_ic.png'),
    ('02_UI/01_Sprites/003. Training Content List/set_dropdown_arrow.png', '01_Script/Lobby/Art/Sprites/003. Training Content List/set_dropdown_arrow.png'),
    ('02_UI/01_Sprites/003. Training Content List/training_content_list.png', '01_Script/Lobby/Art/Sprites/003. Training Content List/training_content_list.png'),
    ('02_UI/01_Sprites/003. Training Content List/training_content_set_dropdown.png', '01_Script/Lobby/Art/Sprites/003. Training Content List/training_content_set_dropdown.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_disabled.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_disabled.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_disabled_reverse.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_disabled_reverse.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_enabled.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_enabled.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_enabled_rever.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_enabled_rever.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_enabled_reverse.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_enabled_reverse.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_arrow_panel.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_arrow_panel.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_image.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_image.png'),
    ('02_UI/01_Sprites/004. Create Training Content/create_training_input_field.png', '01_Script/Lobby/Art/Sprites/004. Create Training Content/create_training_input_field.png'),
    ('02_UI/01_Sprites/005. Training Session/training_session_image.png', '01_Script/Lobby/Art/Sprites/005. Training Session/training_session_image.png'),
    ('02_UI/01_Sprites/005. Training Session/training_session_participant_info_board.png', '01_Script/Lobby/Art/Sprites/005. Training Session/training_session_participant_info_board.png'),
    ('02_UI/01_Sprites/005. Training Session/training_session_participant_info_input_field.png', '01_Script/Lobby/Art/Sprites/005. Training Session/training_session_participant_info_input_field.png'),
    ('02_UI/01_Sprites/005. Training Session/training_session_participant_ready.png', '01_Script/Lobby/Art/Sprites/005. Training Session/training_session_participant_ready.png'),
    ('02_UI/01_Sprites/005. Training Session/training_session_participant_unready.png', '01_Script/Lobby/Art/Sprites/005. Training Session/training_session_participant_unready.png'),
    ('02_UI/01_Sprites/006. Join Training/back_ic.png', '01_Script/Lobby/Art/Sprites/006. Join Training/back_ic.png'),
    ('02_UI/01_Sprites/006. Join Training/join_training_list.png', '01_Script/Lobby/Art/Sprites/006. Join Training/join_training_list.png'),
    ('02_UI/01_Sprites/006. Join Training/join_training_set_off.png', '01_Script/Lobby/Art/Sprites/006. Join Training/join_training_set_off.png'),
    ('02_UI/01_Sprites/006. Join Training/logout_ic.png', '01_Script/Lobby/Art/Sprites/006. Join Training/logout_ic.png'),
    ('02_UI/01_Sprites/006. Join Training/reload_ic.png', '01_Script/Lobby/Art/Sprites/006. Join Training/reload_ic.png'),
    ('02_UI/01_Sprites/009. Add/UI_COUNTRY_SELECTION.png', '01_Script/Lobby/Art/Sprites/009. Add/UI_COUNTRY_SELECTION.png'),
    ('02_UI/01_Sprites/009. Add/UI_COUNTRY_SELECTION_AmericanFlag.png', '01_Script/Lobby/Art/Sprites/009. Add/UI_COUNTRY_SELECTION_AmericanFlag.png'),
    ('02_UI/01_Sprites/009. Add/UI_COUNTRY_SELECTION_KoreanFlag.png', '01_Script/Lobby/Art/Sprites/009. Add/UI_COUNTRY_SELECTION_KoreanFlag.png'),
    ('02_UI/01_Sprites/009. Add/UI_COUNTRY_SELECTION_VietnameseFlag.png', '01_Script/Lobby/Art/Sprites/009. Add/UI_COUNTRY_SELECTION_VietnameseFlag.png'),
    ('02_UI/01_Sprites/020. Fix/button_off.png', '01_Script/Lobby/Art/Sprites/020. Fix/button_off.png'),
    ('02_UI/01_Sprites/020. Fix/button_on.png', '01_Script/Lobby/Art/Sprites/020. Fix/button_on.png'),
    ('02_UI/01_Sprites/020. Fix/close_ic_disabled.png', '01_Script/Lobby/Art/Sprites/020. Fix/close_ic_disabled.png'),
    ('02_UI/01_Sprites/020. Fix/close_ic_enabled.png', '01_Script/Lobby/Art/Sprites/020. Fix/close_ic_enabled.png'),
    ('02_UI/01_Sprites/020. Fix/close_panel_disabled.png', '01_Script/Lobby/Art/Sprites/020. Fix/close_panel_disabled.png'),
    ('02_UI/01_Sprites/020. Fix/close_panel_enabled.png', '01_Script/Lobby/Art/Sprites/020. Fix/close_panel_enabled.png'),
    ('02_UI/01_Sprites/020. Fix/content_title.png', '01_Script/Lobby/Art/Sprites/020. Fix/content_title.png'),
    ('02_UI/01_Sprites/020. Fix/title_underline.png', '01_Script/Lobby/Art/Sprites/020. Fix/title_underline.png'),
    ('02_UI/01_Sprites/020. Fix/training_content_list.png', '01_Script/Lobby/Art/Sprites/020. Fix/training_content_list.png'),
    ('02_UI/01_Sprites/020. Fix/training_content_scroll.png', '01_Script/Lobby/Art/Sprites/020. Fix/training_content_scroll.png'),
    ('02_UI/01_Sprites/020. Fix/training_content_scroll_bar.png', '01_Script/Lobby/Art/Sprites/020. Fix/training_content_scroll_bar.png'),
    ('02_UI/01_Sprites/020. Fix/training_content_set_off.png', '01_Script/Lobby/Art/Sprites/020. Fix/training_content_set_off.png'),
    ('02_UI/01_Sprites/020. Fix/training_content_set_on.png', '01_Script/Lobby/Art/Sprites/020. Fix/training_content_set_on.png'),
    ('02_UI/01_Sprites/021. FixUI/join_training_bullet_point.png', '01_Script/Lobby/Art/Sprites/021. FixUI/join_training_bullet_point.png'),
    ('02_UI/01_Sprites/021. FixUI/join_training_button_off.png', '01_Script/Lobby/Art/Sprites/021. FixUI/join_training_button_off.png'),
    ('02_UI/01_Sprites/021. FixUI/join_training_button_on.png', '01_Script/Lobby/Art/Sprites/021. FixUI/join_training_button_on.png'),
    ('02_UI/01_Sprites/021. FixUI/join_training_list_line.png', '01_Script/Lobby/Art/Sprites/021. FixUI/join_training_list_line.png'),
    ('02_UI/01_Sprites/021. FixUI/training_content_list_panel.png', '01_Script/Lobby/Art/Sprites/021. FixUI/training_content_list_panel.png'),
    ('02_UI/02_Prefabs/preb_ContentList.prefab', '01_Script/Lobby/Prefabs/Parts/preb_ContentList.prefab'),
    ('07_Resources/Prefabs/UI/Lobby/PlayerView.prefab', '01_Script/Lobby/Prefabs/Parts/PlayerView.prefab'),
    ('07_Resources/Textures/UI/PNG/2. 콘텐츠 리스트/content_button_on.png', '01_Script/Lobby/Art/Sprites/Legacy/2. 콘텐츠 리스트/content_button_on.png'),
    ('07_Resources/Textures/UI/PNG/3. 훈련 생성/text_board_off.png', '01_Script/Lobby/Art/Sprites/Legacy/3. 훈련 생성/text_board_off.png'),
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
