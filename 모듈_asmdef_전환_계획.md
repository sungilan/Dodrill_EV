# 모듈 asmdef 전환 계획 (2026-11-20)

> 목적: 모듈을 분리해 다른 프로젝트에 적용. 코드 의존은 훅·이동으로 이미 층 구조(공통 < 인터랙션 < 아바타 < 엔진 < 콘텐츠)에 맞췄다(실제 위반 0, 도구 오탐 3).
> 이 문서는 **어셈블리(asmdef)로 실제로 나누는 단계**의 계획이다. 파일은 아직 옮기지 않았다(빌드·플레이 중 재컴파일 방지). 도구: `Tools/ModuleMigration/`.

## 1. 어셈블리 5개

| 어셈블리 | 폴더(이동 후) | .cs | 들어가는 것 | 참조 |
|---|---:|---:|---|---|
| `DoDrill.Common` | `01_Script/Common` | 43 | `01_Core`·`03_ETC`(UserInfo·VrUi·VrDevice·NetworkObjectFinder 등), 네트워크 보조 | 플러그인만 |
| `DoDrill.Interaction` | `01_Script/Interaction` | 10 | `SyncGrab`·`TaskItem`·`TaskInteractionZone`·`InteractionEvents`·`GrabHooks`·`HeldItemOnTop` 등 | Common |
| `DoDrill.Avatar` | `01_Script/Avatar` | 17 | 플레이어·아바타(`AvatarHooks` 포함) | Common·Interaction |
| `DoDrill.Engine` | `01_Script/Engine` | 52 | 시나리오 엔진·대화·평가 코어·Debug 기반 | Common·Interaction·Avatar |
| `DoDrill.Content` | `01_Script/Content` | 264 | EV 장비·UI·로비·보호구·혼자하기·간호(레거시)·EV 입력 + 외부 스크립트(Inventory·MiniMap·Scripts/Lobbies) | 위 전부 |

`Editor` 폴더는 이동하지 않는다(Assembly-CSharp-Editor 는 asmdef 를 자동 참조). 배정은 `Tools/ModuleMigration/assignment.json`.

## 2. 플러그인 참조 (실제 asmdef 이름 — `migrate.py --check` 로 확인됨)
`FishNet.Runtime` · `MasterServerToolkit` · `AutoHandAssembly`(이름 주의) · `RootMotion`(이름 주의) · `EPO` · `AYellowpaper.SerializedCollections` · `Unity.TextMeshPro` · `UnityEngine.UI` · `Unity.InputSystem` · `Unity.Localization` · `Unity.XR.Management` · `Unity.XR.Interaction.Toolkit` · `Unity.RenderPipelines.Core.Runtime` · `Unity.RenderPipelines.Universal.Runtime`. Odin(Sirenix)은 미리 컴파일된 dll 이라 자동 참조.

## 3. 막힘 요소와 해결
1. **DOTween 에 asmdef 가 없다** — 확장 메서드(`DOFade` 등)가 `Plugins/Demigiant/DOTween/Modules/*.cs`(소스)에 있어 asmdef 코드가 못 본다. → `DOTween.Modules.asmdef`(precompiled `DOTween.dll`, 참조 `UnityEngine.UI`·`Unity.TextMeshPro`·`EPO`)를 만든다. `DOTweenPro`(DOTweenAnimation·TMP)도 필요하면 `DOTweenPro.asmdef` 추가. **여기서 가장 많이 막힐 것으로 예상.**
2. **asmdef 가 없는 스크립트 폴더**(Assembly-CSharp)는 asmdef 가 참조할 수 없다 — `Inventory/Scripts`(EVInventoryUI), `MiniMap/_Scripts`, `Scripts/Lobbies` 는 콘텐츠와 서로 얽혀 `Content/External/` 로 옮긴다. `QuickOutline/Scripts`·`ThirtParty/Monqo Lomo Camera` 는 서로 의존이 없는 잎이라 제자리에 asmdef 만 만든다.
3. **문자열로 타입·메서드를 찾는 코드**(`Type.GetType`, `SendMessage`, 리플렉션)는 어셈블리가 나뉘면 깨질 수 있다 — 전환 뒤 `grep` 으로 점검(예: `ScenarioRunner` 의 `SessionPlayerServer._players` 리플렉션은 같은 엔진 어셈블리라 유지).
4. **FishNet 코드 생성** — `FishNet.Runtime` 을 참조하는 어셈블리는 자동으로 처리되지만, 새 어셈블리마다 컴파일 후 서버 프리팹을 다시 빌드(DefaultPrefabObjects)해야 한다.
5. **`RuntimeInitializeOnLoadMethod` 바인딩**(EvScenarioBinding·EvAvatarBinding·EvInteractionBinding·EngineInteractionBinding·NursingModuleSet)은 어셈블리 상관없이 실행되지만, **Content 어셈블리가 빌드에서 제외되거나 안 불리면 훅 등록이 빠진다** — 빌드 후 모듈 개수·손 뻗기·이름표를 확인.
6. 씬·프리팹은 스크립트 GUID 로 연결돼 이동해도 유지된다(`.meta` 를 같이 옮기므로). 단, 클래스 이름·네임스페이스는 바꾸지 않는다.
7. 층 위반 오탐 3개(`Ring`·`Check`·`LocalizedString` — 이름이 같은 다른 클래스)는 실제 참조가 아니다.

## 4. 실행 순서
1. 모든 변경을 커밋하고 빌드·플레이를 끝낸다(Unity 에서 컴파일이 끼어들지 않게). 전용 브랜치에서 진행.
2. `python Tools/ModuleMigration/migrate.py` (드라이런) → 이동 개수·asmdef 확인, `--check` 로 참조 이름 확인.
3. `--apply` (git mv + .meta + asmdef 생성). 이어서 Unity 를 연다.
4. **아래층부터 컴파일 오류를 고친다**: DOTween.Modules → Common → Interaction → Avatar → Engine → Content. 어셈블리 하나가 깨끗해지면 다음으로.
5. 서버 프리팹 재빌드 → PC·모바일·VR 동시 접속, 시나리오 완주, 자유 탈거 확인(훅 등록 누락 증상: 모듈 0개·이름표 없음·손 뻗기 없음).
6. 폴더 정리(어셈블리 폴더 안 이중 경로 `Content/Equipment/…` 등)는 동작 확인 뒤.

## 5. 다른 프로젝트에 적용할 때
- 공통 엔진은 `Common`+`Interaction`+`Avatar`+`Engine` 4개 어셈블리 + 플러그인(FishNet·MST·AutoHand 등).
- 콘텐츠 프로젝트가 할 일: `ScenarioHooks`·`AvatarHooks`·`GrabHooks`·`InteractionEvents.ZoneValidator` 에 자기 기능을 등록(예: `Test/Ev*Binding.cs` 가 견본)하고, `ITaskModule` 구현을 `ScenarioHooks.AddModuleProvider` 로 등록.
- 아직 남은 결합: 엔진이 평가 코어(`EvaluationService`)를 `ScenarioRunner` 에서 직접 호출, 엔진·대화가 `VrUi`(Common)를 사용 — 모두 같은 층이라 분리에 문제는 없지만, 평가를 빼려면 훅이 필요하다.
