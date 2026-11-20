# Lobby 프리팹·씬 — 필요 에셋 목록

> 자동 생성(2026-11-20, 범용 로비). 이 모듈 폴더(`Assets/01_Script/Lobby`)를 다른 프로젝트로 옮길 때, 아래 '폴더 밖 에셋'도 함께 가져가야 로비 화면이 깨지지 않는다.
> 플러그인·패키지(Packages/, Plugins/)는 뺐다 — MasterServerToolkit·FishNet·TextMeshPro·Localization 은 새 프로젝트에 설치한다.
> 템플릿 씬의 FishNet `NetworkManager`가 EV의 `DefaultPrefabObjects`(스폰 프리팹 목록)를 가리키지만, 새 프로젝트에서는 FishNet 이 자기 목록으로 자동으로 채우므로 가져갈 필요가 없다(이 목록에서도 뺐다).

## 이 모듈 폴더 안의 프리팹 (9개) · 씬 (4개)
- `Prefabs/LobbyCanvas.prefab`
- `Prefabs/LobbyUI.prefab`
- `Prefabs/Parts/bottomButtons.prefab`
- `Prefabs/Parts/LobyElement.prefab`
- `Prefabs/Parts/PlayerView.prefab`
- `Prefabs/Parts/preb_ContentList.prefab`
- `Prefabs/PC_Mobile_Lobby.prefab`
- `Prefabs/Server/CRoomServer.prefab`
- `Prefabs/Server/MasterServer.prefab`
- `Scenes/Client.unity`
- `Scenes/Master.unity`
- `Scenes/Room.unity`
- `Scenes/Spawner.unity`

## 폴더 밖 에셋 (23개)

### `Assets/01_Script/Common/Fonts/` (6개)
- `Pretendard-Bold SDF.asset`  ← Client, LobbyCanvas, LobbyUI
- `Pretendard-Light SDF.asset`  ← Client, LobbyCanvas, LobbyUI
- `Pretendard-Medium SDF.asset`  ← Client, LobbyCanvas, LobbyUI
- `Pretendard-Medium.otf`  ← Client, LobbyCanvas, LobbyUI
- `Pretendard-SemiBold SDF.asset`  ← bottomButtons, Client, LobbyCanvas
- `Pretendard-SemiBold.otf`  ← bottomButtons, Client, LobbyCanvas

### `Assets/02_UI/01_Sprites/002. Login/` (2개)
- `power_ic.png`  ← Client, LobbyCanvas, LobbyUI
- `power_off.png`  ← Client, LobbyCanvas, LobbyUI

### `Assets/02_UI/01_Sprites/003. Training Content List/` (2개)
- `training_content_progress.png`  ← Client, LobbyCanvas, LobbyUI
- `training_content_progress_bar.png`  ← Client, LobbyCanvas, LobbyUI

### `Assets/02_UI/01_Sprites/008. Popup/` (4개)
- `button_off.png`  ← Client, LobbyCanvas, LobbyUI
- `button_on.png`  ← Client, LobbyCanvas, LobbyUI
- `notice_dashboard_popup.png`  ← Client, LobbyCanvas, LobbyUI
- `warning_ic.png`  ← Client, LobbyCanvas, LobbyUI

### `Assets/07_Resources/Prefabs/UI/` (1개)
- `EventSystem.prefab`  ← Client, PC_Mobile_Lobby

### `Assets/07_Resources/Textures/UI/` (6개)
- `PNG/2. 콘텐츠 리스트/content_board.png`  ← Client, LobbyCanvas, LobbyUI
- `PNG/2. 콘텐츠 리스트/content_button_off.png`  ← bottomButtons
- `PNG/2. 콘텐츠 리스트/content_choice_board_off.png`  ← Client, LobbyCanvas, LobbyUI
- `PNG/5. 훈련세션/exit_button_off.png`  ← Client, LobbyCanvas, LobbyUI
- `PNG/8. 설정/fa_icons_solid_1.png`  ← Client, LobbyCanvas, LobbyUI
- `PNG/9. 결과/result_board.png`  ← Client, LobbyCanvas, LobbyUI

### `Assets/Resources/Thumbnails/` (1개)
- `1_vital_signs_complex.png`  ← Client, LobbyCanvas, LobbyUI

### `Assets/ThirtParty/SimpleSpinner/Spinners/` (1개)
- `7.png`  ← Client, LobbyCanvas, LobbyUI
