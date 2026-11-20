# 두드릴 모듈 적용 가이드

스크립트는 11개 어셈블리(asmdef)로 나뉘어 있다. 아래층은 위층을 모르고, 위층(콘텐츠)이 시작할 때 아래층의 훅(public static 델리게이트)에 자기 기능을 등록한다.

## 모듈 구성 (하위 → 상위)

| 층 | 모듈 | 폴더 | 역할 | 다른 프로젝트 재사용 |
|---|---|---|---|---|
| L0 | `DoDrill.Common` | `Common` | 플랫폼 판정, 공용 UI 유틸, 네트워크 공용, 훅 자가 점검 | O |
| L1 | `DoDrill.Interaction` | `Interaction` | 잡기·동기화(SyncGrab), 작업 아이템·존, 상호작용 이벤트 | O |
| L2 | `DoDrill.Avatar` | `Avatar` | PC·모바일·VR 아바타, IK, PC·모바일 이동·시점 공통부 | O |
| L3 | `DoDrill.Engine` | `Engine` | 시나리오 엔진(JSON 단계 정의·실행·스폰), 선임 대화, 평가 | O |
| L3 | `DoDrill.Solo` | `Solo` | 혼자하기(서버 없이 호스트로 실행) | O |
| L3 | `DoDrill.Safety` | `Safety` | 고전압 안전 상태, LOTO, 사고 연출, 보호구 동기화 | 전기·안전 교육 계열 |
| L3 | `DoDrill.Ui` | `Ui` | 위젯, VR 창 따라오기, 진행 패널·토스트·경고창·이름표 | O |
| L4 | `DoDrill.Content.*` | `Content` | 전기차 정비 콘텐츠(Base·Core·Shell·Features) | X (EV 전용) |

각 모듈 폴더의 `README.md`에 역할, 참조, 훅 목록을 정리해 두었다.

## 새 프로젝트에 적용하는 순서

1. **외부 플러그인 설치**: FishNet 4.6.x, MasterServerToolkit, AutoHand(+FinalIK), TextMeshPro, DOTween, Newtonsoft Json, Localization. 가져갈 모듈 asmdef의 `references`에 적힌 것만 있으면 된다.
2. **모듈 폴더 복사**: Common → Interaction → (Avatar) → Engine 순서로, 하위부터 필요한 만큼. 하위만으로도 컴파일된다. `.meta`까지 같이 복사해야 프리팹 연결이 유지된다.
3. **단계 정의**: 먼저 범용 모듈(`config.type`: Confirm, GrabToZone, UseItem, GrabAll, Measure, Wait)로 JSON만 작성한다(`Engine/README.md` 범용 모듈 표). 범용으로 안 되는 단계만 `ITaskModule`을 구현한다. `ModuleId`는 시나리오 JSON의 `moduleId`와 같아야 한다. 완료 조건이 되면 `onComplete`를 호출한다. (예: `Assets/99_ModuleExample/Scripts/ExampleModules.cs`)
4. **콘텐츠 등록(바인딩)**: `ScenarioHooks.AddModuleProvider("내콘텐츠", 순서, registry => registry.Register(...))`. 필요한 다른 훅도 같은 곳에서 채운다. EV는 `Content/Features/App/Bindings/Ev*Binding.cs`가 이 역할을 한다.
5. **시나리오 JSON 작성**: `Resources/Scenarios/<scenarioId>.json`. 형식은 `Engine/Scenario/Data/ScenarioData.cs` 참고. 단계 목록은 `scenario.tasks`, 단계별 설정은 `modules[].config`(`extra` key/ko/en/vi)에 넣는다.
6. **씬 구성**: FishNet `NetworkManager`, `ScenarioBootstrapper`, `ScenarioRunner`(`_bootstrapper` 연결), 필요하면 `ScenarioSpawner`·`SpawnPoint`. 서버가 `ScenarioRunner.StartScenario(data)`를 호출하면 진행이 시작된다.
7. **등록 확인**: 시작 로그의 `[HookCheck:<이름>]` 줄을 본다. `HookRegistry.Verify("이름", 훅 클래스들)`를 콘텐츠 쪽에서 한 번 부르면 비어 있는 훅이 경고로 남는다. 등록이 빠져도 오류 없이 기능만 사라지므로 이 로그로 확인한다.

## 최소 예제

`Assets/99_ModuleExample`: EV 콘텐츠 없이 엔진만으로 시나리오 단계 2개를 완주한다.

1. 메뉴 **Tools ▸ DoDrill ▸ 모듈 최소 예제 씬 만들기** → `Example_MinimalScenario.unity` 생성. 열린 씬은 건드리지 않는다.
2. 생성된 씬을 열고 Play.
3. 호스트 시작 → '잠시 대기'(3초 뒤 자동 완료) → '스페이스 키 누르기' → 시나리오 종료. 진행 로그가 화면 왼쪽 위에 표시된다.

## 주의

- 훅은 전역(static)이다. 같은 프로젝트에 콘텐츠가 둘이면, 등록을 씬이나 모드 조건으로 나눈다. 예제는 시작기 `Awake`에서만 등록한다.
- `Resources/Scenarios`에 둔 JSON은 EV 로비의 시나리오 목록에 모두 뜬다. 예제·테스트용 JSON은 Resources 밖에 둔다.
- FishNet 네트워크 오브젝트가 달린 스크립트의 어셈블리를 옮겼으면 서버와 클라이언트를 같이 다시 빌드한다. 직렬화·RPC 코드가 어셈블리 단위로 생성되기 때문이다.
