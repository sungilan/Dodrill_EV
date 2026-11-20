# 모듈 예제

예제는 세 개다.

| 예제 | 씬 | 콘텐츠 코드 | 보여 주는 것 |
|---|---|---|---|
| 멀티플레이 예제(아바타) | `Example_MultiplayerAvatar.unity` | 없음 | 위와 같되 플레이어가 정비사 아바타(`GenericAvatarPlayer`) — 걷기 애니메이션·손 뻗기·웅크리기(왼쪽 Ctrl). 메뉴 `Tools ▸ DoDrill ▸ 멀티플레이 예제 씬 만들기 (아바타 플레이어)` |
| 멀티플레이 예제 | `Example_Multiplayer.unity` | 없음 | 범용 프리팹(플레이어·네트워크 아이템·엔진·HUD)만으로 호스트 시작 → 접속자마다 플레이어 생성 → 렌치 집기·작업대에 놓기·버튼 누르기 |
| 범용 모듈 예제 | `Example_Workbench.unity` | 없음 | 렌치 집기 → 작업대에 놓기 → 버튼 누르기를 JSON과 모듈 부품만으로 구성 |
| 최소 예제 | `Example_MinimalScenario.unity` | 단계 모듈 2개 | C# 단계 모듈을 직접 만들어 등록하는 방법 |

## 멀티플레이 예제 (코드 없음, 범용 프리팹)

1. 메뉴 **Tools ▸ DoDrill ▸ 멀티플레이 예제 씬 만들기 (범용 프리팹)** → `Example_Multiplayer.unity` 생성(범용 프리팹이 없으면 먼저 만든다). 지금 열린 씬은 건드리지 않는다.
2. 씬만 열고 Play(다른 NetworkManager가 든 씬은 닫는다). 호스트로 시작해 `GenericPlayer`가 생기고 왼쪽 위에 단계가 뜬다.
3. 조작: WASD 이동, 오른쪽 버튼 끌기 시점, 왼쪽 클릭 집기·놓기·누르기, F 사용
   - 1단계: 왼쪽 테이블의 렌치 클릭 → 집힘(서버 권한 `SyncGrab`)
   - 2단계: 렌치를 든 채 작업대 위 초록 자리를 보고 클릭 → 그 자리에 놓임
   - 3단계: 오른쪽 빨간 버튼 클릭 → "모든 단계 완료"
4. 여러 명 접속은 아직 이 예제로 확인하지 않았다. `ScenarioAutoStarter`가 호스트를 시작하므로, 두 번째 기기는 `Auto Start Host`를 끄고 FishNet 접속(`ClientManager.StartConnection`)을 따로 호출해야 한다.

씬 구성(모두 범용 프리팹 인스턴스):

| 오브젝트 | 프리팹 | 설정 |
|---|---|---|
| NetworkManager | `Engine/Prefabs/NetworkManagerHost` | Spawnable Prefabs = `ExampleMultiplayerPrefabs.asset`(GenericPlayer, Item_Wrench) |
| ScenarioEngine | `Engine/Prefabs/ScenarioEngine` | 시나리오 JSON = `example_multiplayer.json`, 프리팹 목록(자식 `Network`의 `ScenarioSpawner`) `Wrench` → `Item_Wrench` |
| SpawnPoint_Table | `Engine/Prefabs/SpawnPoint` | `pointId` = `SpawnPoint_Table` |
| Zone_Bench | `Interaction/Prefabs/TaskZone` | `zoneId` = `Zone_Bench` |
| StartButton | `Interaction/Prefabs/TaskButton` | `targetId` = `StartButton` |
| Hud | `Ui/Prefabs/SimpleTaskHud` | — |
| Anchor | (태그 `Anchor` 빈 오브젝트) | 위치 동기화 기준 — 없으면 처음 3초는 동기화를 기다린다 |
| (런타임) 플레이어 | `Avatar/Prefabs/Players/GenericPlayer` | 접속 때 `PlayerSpawner`가 생성 |

- 렌치는 `Interaction/Prefabs/NetworkItem`의 **변형** `Prefabs/Item_Wrench`(prefabId `Wrench`, 렌치 모양)다. 새 아이템은 같은 방법으로 변형을 만들어 FishNet 목록과 `ScenarioSpawner` 목록에 넣는다.
- 아이템 스폰은 시나리오 JSON의 `spawnObjects`(prefabId·spawnPointId·persist)로 한다 — `Scenarios/example_multiplayer.json`.
- 한계: 같은 PC 한 개 에디터에서는 호스트 한 명만 시험할 수 있다. 두 명 이상·VR·모바일은 빌드로 확인이 필요하다. VR 플레이어 프리팹(`Avatar/Prefabs/Players/VR_Player`)은 아직 이 예제에 연결하지 않았다.

## 범용 모듈 예제 (코드 없음)

1. 메뉴 **Tools ▸ DoDrill ▸ 범용 모듈 예제 씬 만들기 (렌치→작업대→버튼)** → `Example_Workbench.unity` 생성. 지금 열린 씬은 건드리지 않는다.
2. 씬을 열고 Play. 왼쪽 위에 단계 제목과 안내가 뜬다.
3. 조작: WASD 이동, Q/E 아래·위, 오른쪽 버튼 끌기로 시점, 왼쪽 클릭으로 집기·놓기·누르기, F 사용
   - 1단계: 왼쪽 테이블의 렌치 클릭 → 집힘
   - 2단계: 작업대 위 초록 자리 클릭 → 놓임
   - 3단계: 오른쪽 빨간 버튼 클릭 → "모든 단계 완료"

시나리오 `Scenarios/example_workbench.json`:

| moduleId | type | 설정 |
|---|---|---|
| `Pick_Wrench` | `GrabAll` | `requiredItems: ["Wrench"]` |
| `Place_Wrench` | `GrabToZone` | `requiredItems: ["Wrench"]`, `targetZoneId: "Zone_Bench"` |
| `Press_Start` | `Confirm` | `targetObjName: "StartButton"` |

씬에 붙인 부품(모두 모듈 폴더 안):

| 오브젝트 | 부품 | 모듈 |
|---|---|---|
| Scenario | `ScenarioBootstrapper`, `ScenarioRunner`, `ScenarioAutoStarter`, `ScenarioReceiver`, `ScenarioStateReceiver` | Engine |
| InteractionBridge | `NetworkObject`, `NetworkInteractionBridge` | Engine |
| Wrench | `TaskItem`(prefabId `Wrench`) + 콜라이더 | Interaction |
| Zone_Bench | `TaskInteractionZone`(zoneId `Zone_Bench`) + 트리거 콜라이더, 렌더러(단계 중에만 보임) | Interaction |
| StartButton | `TaskClickTarget`(targetId `StartButton`) | Interaction |
| Main Camera | `SimplePcInteractor` | Avatar |
| Hud | `SimpleTaskHud` | Ui |
| NetworkManager | FishNet `NetworkManager`, `TransportManager`, `Tugboat` | FishNet |

같은 방식으로 새 콘텐츠를 만들 때: 물체에 위 부품을 붙이고 ID를 JSON과 맞춘 뒤 `ScenarioAutoStarter`에 JSON을 연결한다.

한계:
- 아이템이 씬에 놓인 일반 오브젝트라 여러 명이 접속하면 위치가 동기화되지 않는다 → 멀티플레이는 위의 멀티플레이 예제(범용 프리팹)를 쓴다.
- 조작부는 PC만 있다.

# 최소 예제 (C# 단계 모듈)

EV 콘텐츠 없이 `DoDrill.Engine`(+Common, Interaction, FishNet)만으로 시나리오를 완주하는 예제다.

## 실행

1. 메뉴 **Tools ▸ DoDrill ▸ 모듈 최소 예제 씬 만들기** → `Example_MinimalScenario.unity` 생성. 지금 열린 씬은 건드리지 않는다.
2. 생성된 씬을 열고 Play.
3. 화면 왼쪽 위 로그 순서:
   - 모듈 등록(`Example_Wait`, `Example_PressKey`)
   - 시나리오 시작 → '잠시 대기' 3초 뒤 자동 완료
   - '스페이스 키 누르기' → Space를 누르면 완료
   - 모든 단계 완료 → 시나리오 종료

## 파일

| 파일 | 내용 |
|---|---|
| `Scripts/ExampleModules.cs` | 단계 모듈 2개(`ITaskModule` 구현), `extra` 설정 읽기 |
| `Scripts/ExampleScenarioStarter.cs` | 모듈 등록 → 호스트 시작 → JSON 읽기 → `ScenarioRunner.StartScenario` |
| `Scenarios/example_minimal.json` | 시나리오 정의(단계 2개) |
| `Editor/ExampleSceneBuilder.cs` | 최소 예제 씬 생성 메뉴 |
| `Editor/WorkbenchExampleBuilder.cs` | 범용 모듈 예제 씬 생성 메뉴 |
| `Editor/GenericPrefabBuilder.cs` | 범용 프리팹 8개를 각 모듈 폴더에 만드는 메뉴(Tools ▸ DoDrill ▸ 범용 프리팹 만들기) |
| `Editor/MultiplayerExampleBuilder.cs`, `Example_Multiplayer.unity`, `Scenarios/example_multiplayer.json`, `Prefabs/Item_Wrench.prefab`, `ExampleMultiplayerPrefabs.asset` | 멀티플레이 예제 |
| `Scenarios/example_workbench.json`, `Example_Workbench.unity`, `Materials/` | 범용 모듈 예제 |
| `Example_MinimalScenario.unity` | 생성된 예제 씬 |
| `ExampleSpawnablePrefabs.asset` | FishNet 스폰 프리팹 목록(빈 목록 — 예제는 네트워크 오브젝트를 스폰하지 않음) |

## 새 프로젝트로 옮길 때

- 시작기의 등록 코드는 실제 프로젝트에서 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 바인딩 파일로 옮긴다(EV의 `Ev*Binding` 참고).
- 시나리오 JSON은 `Resources/Scenarios/`에 두고 `ScenarioLoader.Load(id)`로 읽는다. 예제 JSON은 EV 로비 목록에 섞이지 않도록 Resources 밖에 둔다.
- 빌드 씬 목록에는 넣지 않았다.
