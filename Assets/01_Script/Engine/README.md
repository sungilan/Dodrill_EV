# DoDrill.Engine (L3)

시나리오 JSON을 읽어 단계(Task)를 순서대로 진행하는 서버 권한 엔진이다. 선임 대화, 평가(감점·결과)도 포함한다. 참조: Common, Interaction.

## 흐름

1. `ScenarioBootstrapper.Awake`: `ScenarioHooks`에 등록된 모듈 공급자로 `ModuleRegistry`를 채운다.
2. 서버가 `ScenarioRunner.StartScenario(data)`를 호출한다. 로비에서는 `ScenarioDistributor`가 호출한다.
3. 단계마다 `moduleId`로 모듈을 찾아 `OnStart` → 매 프레임 `OnUpdate` → 완료 조건에서 `onComplete`가 호출되면 다음 단계로 넘어간다.
4. 상태는 `TaskStateBroadcast`로 클라이언트에 전달된다. 클라이언트는 `ScenarioStateReceiver`로 받는다.
5. 마지막 단계가 끝나면 평가 요약 후 `ScenarioEndBroadcast`를 보낸다.

모듈 찾는 순서: ① `moduleId`로 등록된 C# 모듈 → ② `config.type`의 범용 모듈 → ③ `moduleId` 자체가 범용 종류 이름. 셋 다 없으면 더미 모듈로 대기한다. 이때 `[ModuleRegistry] 모듈 없음` 로그가 남는다.

## 내용

| 폴더 | 내용 |
|---|---|
| `Scenario/Data` | `ScenarioData`(JSON 구조), `ScenarioLoader`(Resources/Scenarios), 브로드캐스트, FishNet 직렬화 |
| `Scenario/Runtime` | `ScenarioBootstrapper`, `ScenarioRunner`, `ScenarioSpawner`, `SpawnPoint`, `ScenarioDistributor` |
| `Scenario/Client` | `ScenarioReceiver`, `ScenarioStateReceiver` |
| `Scenario/Hooks` | `ScenarioHooks`(확장 지점), `EngineInteractionBinding` |
| `Modules` | `ITaskModule`, `ModuleRegistry`, 기본 모듈(`ConfirmTask`, `GrabUseTask`, `GrabZoneTask`, `GrabAllItems`) |
| `Evaluation` | 평가 모드·규칙·감점·결과 CSV |
| `Dialogue` | 선임 대화(`MentorDialogue`), TTS, 음성 로더 |
| `Session`, `Debug`, `Localization`, `Legacy` | 세션 플레이어, 디버그 콘솔, 다국어, 간호 레거시 모듈 |

## 범용 모듈 (코드 없이 JSON만으로)

엔진이 항상 등록한다. 콘텐츠 바인딩이 없는 프로젝트에서도 바로 쓸 수 있다. 같은 `moduleId`의 C# 모듈이 있으면 그쪽이 먼저다.

| `type` | 완료 조건 | 쓰는 설정 |
|---|---|---|
| `Confirm` | 대상 오브젝트 클릭 또는 확인 버튼 | `targetObjName`(비우면 확인 버튼만) |
| `GrabToZone` | 아이템을 목표 구역에 가져다 대기 | `targetZoneId`, `requiredItems` |
| `UseItem` | 아이템을 들고 사용 | `requiredItems` |
| `GrabAll` | 필요 아이템 모두 집기 | `requiredItems`, extra `perPlayer`=true면 사람마다 |
| `Measure` | 측정값이 기준 안 | `targetObjName`(단자), `targetValue`·`toleranceRatio` 또는 `maxValue`, extra `tolerance` |
| `Wait` | 시간 경과 | extra `seconds`(기본 3) |

```json
"scenario": { "tasks": [
  { "moduleId": "Pick_Wrench", "spawnObjects": [ { "prefabId": "Wrench", "spawnPointId": "SP_Table" } ] },
  { "moduleId": "Place_Wrench" },
  { "moduleId": "Press_Start" }
]},
"modules": [
  { "moduleId": "Pick_Wrench",  "config": { "type": "GrabAll",    "requiredItems": ["Wrench"] } },
  { "moduleId": "Place_Wrench", "config": { "type": "GrabToZone", "requiredItems": ["Wrench"], "targetZoneId": "Zone_Bench" } },
  { "moduleId": "Press_Start",  "config": { "type": "Confirm",    "targetObjName": "StartButton" } }
]
```

`completionMode`는 생략하면 `AnyPlayer`다. 로비 없이 씬을 열자마자 시나리오를 돌리려면 `ScenarioAutoStarter`(호스트 시작 + JSON 실행)를 붙인다. 전체 예제는 `Assets/99_ModuleExample/Example_Workbench.unity`. 코드는 `Modules/Generic/GenericModules.cs`.

## 단계 모듈 만들기 (범용으로 안 될 때)

```csharp
public class MyModule : ITaskModule
{
    public string ModuleId => "My_Step";          // JSON moduleId 와 동일
    Action _done;
    public void OnStart(ModuleConfig c, Action onComplete, Action onFail) { _done = onComplete; }
    public void OnUpdate(float dt) { if (조건) { _done?.Invoke(); _done = null; } }
    public void OnComplete() { }
    public void OnFail() { }
}
// 등록(콘텐츠 바인딩)
ScenarioHooks.AddModuleProvider("MyContent", 100, r => r.Register(new MyModule()));
```

전체 예제는 `Assets/99_ModuleExample`에 있다.

## 훅 (`ScenarioHooks`)

| 훅 | 설명 |
|---|---|
| `AddModuleProvider` | 단계 모듈 묶음 등록(필수) |
| `RecentActor`, `LiftActor` | 방금 조작한 사람 판정(평가 기록용) |
| `SpawnFilter`, `ScenarioListFilter` | 스폰 제외, 시나리오 목록 필터 |
| `ZoneSignalFilter` | 클라이언트 존 신호 필터 |
| `IsPerPlayerPrefab` | 인원수만큼 스폰할 프리팹 |
| `TargetStateOk` | 클릭 완료 단계의 상태 확인 |
| `OnMapObjectSpawned` | 지도 오브젝트 스폰 후처리 |

EV 등록 위치는 `Content/Features/App/Bindings/EvScenarioBinding.cs`이다.

## 참조 플러그인

FishNet, AutoHand, DOTween, Localization, ResourceManager, TextMeshPro, UI (Newtonsoft Json은 자동 참조)

## 범용 프리팹

| 프리팹 | 내용 |
|---|---|
| `Prefabs/ScenarioEngine.prefab` | 시나리오 엔진 묶음. 루트(네트워크 아님): `ScenarioBootstrapper`·`ScenarioRunner`·`ScenarioReceiver`·`ScenarioStateReceiver`·`ScenarioAutoStarter`. 자식 `Network`: `NetworkObject`+`ScenarioSpawner`+`NetworkInteractionBridge`. 씬에 놓고 `ScenarioAutoStarter`의 시나리오 JSON, `ScenarioSpawner`의 프리팹 목록(prefabId → 프리팹)만 채운다 |
| `Prefabs/SpawnPoint.prefab` | 아이템 스폰 자리(`pointId`를 JSON `spawnPointId`와 맞춘다) |
| `Prefabs/NetworkManagerHost.prefab` | FishNet `NetworkManager`+`TransportManager`+`Tugboat`+`PlayerSpawner`(플레이어 = `GenericPlayer`). Spawnable Prefabs는 `GenericSpawnablePrefabs.asset`(플레이어·`NetworkItem`) — 아이템을 더하면 씬에서 목록 에셋을 따로 만들어 바꾼다 |

- 이 폴더의 프리팹은 `Avatar`(GenericPlayer)·`Interaction`(NetworkItem, 재질)의 에셋을 참조한다(`Prefabs/ASSET_MANIFEST.md`). 모듈을 따로 옮길 때 함께 가져간다.
- **서버를 켜는 `ScenarioAutoStarter`는 `NetworkObject`가 있는 오브젝트에 붙이지 않는다.** 씬에 놓인 NetworkObject는 FishNet이 서버가 켜질 때까지 꺼 두므로 시작기가 영영 실행되지 못한다(그래서 네트워크 부품을 자식으로 분리).
- 서버 시작·시나리오 시작은 `ScenarioAutoStarter`가 한다(호스트로 시작 → JSON 읽기 → 클라이언트에 전송 → 시작).
