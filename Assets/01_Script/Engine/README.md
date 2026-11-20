# DoDrill.Engine (L3)

시나리오 JSON을 읽어 단계(Task)를 순서대로 진행하는 서버 권한 엔진이다. 선임 대화, 평가(감점·결과)도 포함한다. 참조: Common, Interaction.

## 흐름

1. `ScenarioBootstrapper.Awake`: `ScenarioHooks`에 등록된 모듈 공급자로 `ModuleRegistry`를 채운다.
2. 서버가 `ScenarioRunner.StartScenario(data)`를 호출한다. 로비에서는 `ScenarioDistributor`가 호출한다.
3. 단계마다 `moduleId`로 모듈을 찾아 `OnStart` → 매 프레임 `OnUpdate` → 완료 조건에서 `onComplete`가 호출되면 다음 단계로 넘어간다.
4. 상태는 `TaskStateBroadcast`로 클라이언트에 전달된다. 클라이언트는 `ScenarioStateReceiver`로 받는다.
5. 마지막 단계가 끝나면 평가 요약 후 `ScenarioEndBroadcast`를 보낸다.

모듈을 찾지 못하면 더미 모듈로 대기한다. 이때 `[ModuleRegistry] 모듈 없음` 로그가 남는다.

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

## 단계 모듈 만들기

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
