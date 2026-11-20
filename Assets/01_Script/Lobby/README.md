# DoDrill.Lobby (L4)

EV에서 쓰는 로비(MasterServerToolkit 기반: 로그인 → 방 목록 → 방 만들기 → 대기실 → 시작)를 그대로 옮겨 쓰는 모듈. 참조: Common, Engine, Ui, Solo + MasterServerToolkit·FishNet·TextMeshPro·Localization·SerializedCollections. `Content.*`가 이 모듈을 참조하고 반대는 없다.

## 구성

| 폴더 | 내용 |
|---|---|
| `Login` | `LoginPanel`(게스트 로그인·종료), `ClientState`(접속 상태 표시), `MasterReconnectGuard`(마스터 끊김 보호) |
| `Rooms` | `LobbyListPanel`(방 목록), `CreateNewLobbyPanel`(방 만들기), `LobbyPanel`(대기실·준비·채팅·시작), `MyLobbyFactoryModule`·`DefaultFactoriesModule`(마스터의 로비 팩토리), `MatchView`, `LobbyBottomPanel`, `LobbyPlayerInfos` |
| `Menu` | `StartPanel`(시작 화면), `LobbyModeSelector`·`ModeSelectUI`(혼자/함께), 시나리오 목록(`ScenarioListPanel`·`ScenarioListCardUI`·`ScenarioDataLoader`), 팝업(종료·로그아웃) |
| `Language`, `Loading` | 언어 선택, 로딩 배경 |
| `App` | `LobbySettings`(설정), `ScenarioAutoLoader`(룸 서버가 방 옵션의 시나리오를 읽어 `ScenarioDistributor`로 전달), `SessionData`, `GameLoadCurtain`(게임 씬 로딩 막), `PlatformDetector` |
| `Prefabs` | `LobbyCanvas`(로비 UI 캔버스 전체 — 화면 13개), `LobbyUI`(EV 씬의 덮어쓴 값을 담은 변형), `Parts/`(`LobyElement`·`bottomButtons`·`preb_ContentList`·`PlayerView`), `PC_Mobile_Lobby`(카메라 + EventSystem), `Server/`(`MasterServer`·`CRoomServer`) |
| `Art/Sprites` | 로비 전용 스프라이트(`Legacy/`는 예전 UI 텍스처) |
| `Scenes` | 템플릿 씬 `Client`·`Master`·`Spawner`·`Room` |
| `Resources` | `LobbySettings.asset`(EV 기본값) |
| `Editor` | `LobbySceneInstaller`(메뉴 `Tools ▸ DoDrill ▸ 로비 템플릿 씬 설치`), `LobbyBuildProfiles`(메뉴 `Tools ▸ DoDrill ▸ 로비 빌드 프로필` — 룸·마스터 서버·PC 클라이언트 빌드 버튼) |

필요 에셋 목록: `Prefabs/ASSET_MANIFEST.md`(자동 생성).

## 흐름과 서버

서버 3종과 클라이언트가 따로 돈다.

| 구성 | 씬 | 내용 |
|---|---|---|
| 마스터 서버 | `Master` | `MasterServerBehaviour`와 모듈들(`MasterServer` 프리팹), 스포너 연결 |
| 스포너 | `Spawner` | 방(룸 서버) 프로세스를 띄운다 |
| 룸 서버 | `Room` | `CRoomServer` 프리팹(FishNet `NetworkManager`+`RoomServerManager`), 게임 씬을 로드한다 |
| 클라이언트 | `Client` | MST 클라이언트(`S_MasterConnector`·`S_ClientManager`·`S_RoomClientManager`), `LobbyCanvas`, `PlatformDetector`, `ScenarioDataLoader` |

게스트 로그인 → 방 목록 → 방 만들기(시나리오·최대 인원·비밀번호) → 대기실(준비·채팅) → 시작(마스터가 룸 서버를 띄움) → 클라이언트가 방 접속 → `LobbySettings.gameSceneName` 씬 → 룸 서버의 `ScenarioAutoLoader`가 방 옵션의 `scenarioId`를 읽어 시나리오를 전달.

## 설정 `LobbySettings`

`Resources/LobbySettings.asset`(없으면 기본값 = EV). 필드: `clientSceneName`(Client), `gameSceneName`(Game), `lobbyFactoryId`(Game), `defaultScenarioId`, `scenarioResourcePath`(Scenarios), `thumbnailResourcePath`(Thumbnails). 시나리오 JSON은 `Resources/<scenarioResourcePath>`에 둔다.

## 새 프로젝트에 적용

1. 플러그인: FishNet, MasterServerToolkit, TextMeshPro, Localization, SerializedCollections 등(루트 `README.md` 1단계). Odin은 없어도 된다.
2. 폴더 복사(`.meta` 포함): `Common`·`Engine`·`Ui`·`Solo`(+필요한 하위 모듈)와 `Lobby`. `Prefabs/ASSET_MANIFEST.md`의 폴더 밖 에셋(폰트·스프라이트)도 함께.
3. **Localization**: `Common/Localization`(설정·로케일 3개·`UI_Text`·`TaskNames` 테이블)이 함께 복사되고, 4단계의 설치 메뉴가 `Tools ▸ DoDrill ▸ Localization 설치`를 먼저 실행해 활성 Localization 설정과 Addressables 항목을 만든다. 문구를 바꾸거나 더하려면 `UI_Text` 테이블을 편집한다.
4. 메뉴 `Tools ▸ DoDrill ▸ 로비 템플릿 씬 설치` → `Assets/Scenes/Lobby`에 4개 씬 복사, FishNet `DefaultScene` 경로 보정, 빌드 설정에 Client·Game 추가.
5. 씬의 서버 주소 설정(템플릿은 `127.0.0.1`): `ClientToMasterConnector.serverIp`(Client·Room), `SpawnerBehaviour.machineIp`(Master·Spawner), Tugboat `_clientAddress`(Client·Room). 마스터 포트 기본 9035.
6. 게임 씬(`gameSceneName`)과 시나리오 JSON(`Resources/Scenarios`)을 콘텐츠가 만든다.
7. 서버 빌드: 메뉴 `Tools ▸ DoDrill ▸ 로비 빌드 프로필`의 버튼("서버 둘 다 빌드")이 아래 과정을 자동으로 한다. 수동 절차는 다음과 같다(EV 방식 = Linux Dedicated Server; Windows 플레이어로 빌드하면 셰이더 변형 준비가 수십 분 이상 걸린다):
   - Unity Hub에서 해당 에디터 버전에 **Linux Dedicated Server Build Support**(와 Linux Build Support)를 추가한다. Windows에서 빌드하려면 패키지 `com.unity.toolchain.win-x86_64-linux`, `com.unity.sdk.linux-x86_64`(1.1.1)가 필요하다.
   - 마스터: 빌드 대상 Linux, Server 서브타깃, 씬 `Master`만 → `MasterServer.x86_64`. 룸: 씬 `Room`(첫 번째)·게임 씬 → `RoomServer.x86_64`. 빌드 전 XR 자동 초기화를 끈다. EV는 `Tools ▸ Dodrill ▸ 빌드 프로필`이 이 설정을 한 번에 한다(일반화하지 않음).
   - Docker(NAS 등)로 올릴 때: 빌드 프로필 창의 `룸 서버 실행 경로`에 **컨테이너 안** 경로(예: `/home/RoomServer/RoomServer.x86_64`), `스포너 machineIp`에 클라이언트가 접속할 서버 주소를 적고 빌드한다. 마스터 빌드 폴더의 내용을 컨테이너 폴더 루트에, 룸 서버 빌드 폴더의 내용을 그 안 `RoomServer/`에 넣고, 마스터 포트(9035)와 룸 서버 포트 범위를 컨테이너에 게시한다. 클라이언트 씬의 `ClientToMasterConnector.serverIp`는 그 서버 주소로 바꾼다.
   - 마스터 안 스포너의 `executableFilePath`·`machineIp`를 룸 서버 파일·마스터 주소로 지정한 뒤 마스터를 빌드한다. 마스터 실행 인자는 MST 기본(`-mstStartMaster -mstMasterIp <ip> -mstMasterPort 9035` 등).

## 한계

- `UI_Text` 테이블에는 EV 문구(부품 이름·설명 등)도 함께 들어 있다(로비·공용 UI·EV 아이템이 같은 테이블을 쓴다). 다른 프로젝트는 필요 없는 항목을 지우거나 자기 항목을 더하면 된다. `InGame` 테이블(`Game.unity` 전용)은 EV에 남아 있다.
- 룸 서버가 뜨는 "시작" 이후(마스터가 방 프로세스를 띄우고 클라이언트가 접속)는 룸 서버 빌드가 있어야 해서 이 환경에서 확인하지 못했다.
- PC 로비만. VR 로비(VR 앵커·VR 키보드 로비 UI)는 템플릿에서 뺐다.
- 템플릿 씬의 `NetworkManager`는 EV의 `DefaultPrefabObjects`를 가리킨다(FishNet이 새 프로젝트에서 자기 목록으로 자동으로 채운다).
- 로비 UI의 `carSprites`(차량 썸네일 목록)와 일부 스프라이트는 EV에서도 비어 있는 기존 끊김이다.
- 프리팹 `LobbyUI`는 EV 씬에서 덮어쓴 값을 한 번 복사해 둔 변형이다. EV 화면을 고치면 다시 만들어야 한다(레이아웃 그룹이 계산하는 값은 열 때마다 달라진다).
