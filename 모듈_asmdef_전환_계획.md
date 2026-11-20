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

## 6. ④ 혼자하기 모듈 — `DoDrill.Solo` (2026-11-20 분리 완료)
- **폴더** `Assets/01_Script/Solo/` — `SoloModeLauncher.cs` 한 파일(+`SoloModeRunner`·`SoloModeWatcher`, 어셈블리 내부 전용) + `DoDrill.Solo.asmdef`.
- **참조**: `FishNet.Runtime` 뿐. DoDrill 어셈블리를 하나도 참조하지 않는 **잎 모듈**이다(MST 부품은 이름 문자열 + 리플렉션으로만 건드려 MST 참조도 없음).
- **공개 API**: `SoloModeLauncher.Begin(onFailed)` · `IsSolo`(읽기 전용) · `ScenarioId`(기본 시나리오 ID).
- **연결 지점(Content 쪽, 앱이 구현)**: ① 시작 화면 버튼 `StartPanel.AddSoloButton` → `Begin` 호출, ② `ScenarioAutoLoader` 가 `IsSolo` 이면 `ScenarioId` 로 시나리오 자동 시작.
- **다른 프로젝트에서 쓰려면**: FishNet 호스트 + `Client`(로비) 씬/`Game` 씬 구조(서버가 시작되면 `DefaultScene` 이 Game 씬을 불러옴)와, 위 ①② 두 연결을 만들면 된다. 마스터 서버(MST) 없이 동작.
- 의존이 없어 어셈블리 분리에서 가장 쉬웠다. 코드 변경 없이 파일만 이동.

## 7. 구조 점검 결과 (2026-11-20, 분리 직후 정적 분석)
측정: 어셈블리 6개, .cs 407개. Common 4.8k줄 · Interaction 2.6k · Avatar 3.1k · Engine 8.4k · Content **56.9k(69%)** · Solo 0.3k · Editor 6.2k.

**양호**: ① 아래 4층은 컴파일로 층 규칙이 강제됨(실제 위로 향한 참조 0, 도구가 잡은 것은 이름 충돌 오탐). ② 훅(ScenarioHooks·AvatarHooks·GrabHooks…)으로 EV 지식이 아래층에 안 새어 있음(아래 4층의 EV 용어는 주석·예시 수준). ③ Solo 는 완전한 잎.

**막는 것 (모듈화 순서에 영향)**
1. **Content 가 덩어리** — `Test/`(116파일·23k줄)는 실제로는 VR 손바닥 메뉴·GuideSystem·계측기·HV 같은 제품 코드. `Equipment`(71파일·21k줄)↔`Test` 가 서로 28·33개 타입으로 맞물려 있어 폴더 단위로는 ③보호구·⑤UI 를 못 가른다(기능 단위로 가르기 전에 공통 기반 타입을 아래로 내려야 함).
2. **아바타(①)가 자급자족이 아님** — PC·모바일 조작(FreeLookController 2.1k줄, Mobile 1.2k줄)이 Content/06_EVInput 에 있고 Equipment·Test 타입 25개 이상을 직접 부른다. 아바타 어셈블리에는 IK·스포너·VRPlayer 만 있음. `IWorldClickable`·`WorldClickPriority`·`EquipFx`·`InteractablePart`·`Bolt` 같은 '공통 인터랙션 개념'이 Equipment/Test 안에 있음 → Interaction 이나 Content.Core 로 내려야 함.
3. **Common 이 공통이 아님** — 앱 정책(프레임레이트·VR 그래픽·모바일 최적화·로그·캔버스 스케일러)과 EV 작업장 좌표(WorkshopFloorCollider)·네트워크 계약(BuildCompat)이 들어 있고 RuntimeInitialize 자동 실행이 10개. 가져다 쓰면 말없이 앱 동작이 바뀐다.
4. **암묵적 설치** — RuntimeInitializeOnLoadMethod 가 Common 10 · Engine 4 · Content 48. 5개 Ev*Binding 이 사실상 조립 지점인데 빠지면 오류 없이 기능이 사라진다(모듈 0개·이름표 없음). 시작 시 훅 등록 자가 점검이 필요.
5. **asmdef 참조 과다** — 분리 직후 컴파일을 맞추려고 상위 어셈블리마다 플러그인 24~36개를 몰아서 넣었다. 실제 필요보다 많아 '다른 프로젝트에 가져가면 MST·AutoHand·XR 전부 설치해야 컴파일' 이 된다 → 사용 기준으로 가지치기 필요.
6. **씬·프리팹·Resources 결합** — asmdef 는 에셋을 안 옮긴다. Game.unity(GuideSystem 인스펙터 연결), PC/Mobile/VR 플레이어 프리팹, Resources(Scenarios·Dialogue·Shaders/UIOnTop·HudIcons), DOTweenSettings. 이름으로 씬 오브젝트를 찾는 코드(PcHudLayout: CNV_Controller_PC·TaskProgressUI_Mobile·CanvasMiniMap_PC_01 등) 와 FindObjects 계열 Content 176 · Engine 28.
7. **플랫폼 분기** — 47파일 135곳 PlatformType, IsVR 89곳. PC/모바일/VR 이 클래스 안 if-else 와 복제 코드로 갈려, 한 플랫폼 수정이 다른 플랫폼에 빠지는 일이 반복(모바일 조준점·레이저 색·VR 선임 대사). 
8. **큰 클래스** — CarPartService 3.6k줄(+Tray) · FreeLookController 2.1k · GdsTablet 1.7k · SyncGrab 1.5k · TireService 1.3k · GuideSystem 1.2k · ScenarioRunner 1.2k(타입 42개 의존, 평가·디버그 직결) · FreeLoto 1.1k.
9. **네임스페이스 없음** — 407개 중 27개만 네임스페이스. 전역에 Managers·Define·Sound·SpawnPoint·TaskState·TaskStatus·LocalizationManager 같은 흔한 이름 → 다른 프로젝트와 충돌 위험(스크립트는 GUID 로 연결되므로 네임스페이스를 붙여도 씬·프리팹은 안전, 네트워크 방송 타입 이름만 주의).
10. **설정이 코드에** — 서버 주소 3곳(MentorDialogue·LoginPanel 등), TTSManager 의 apiKey 직렬화 필드(Engine/Test), MentorDialogue 의 Resources 경로. 주입 가능한 설정으로 빼야 함.
11. **외부·예제 의존** — `Autohand.FinalIK.asmdef` 를 AutoHand 예제 폴더 안에 만들어 둠(예제 폴더를 지우면 Avatar·Content 컴파일이 깨짐 → AutoHandVRIK 를 우리 Avatar 로 옮기는 편이 안전). DOTween 은 `createASMDEF=1` 설정이 꺼지면 asmdef 를 자동 삭제.
12. **자동 테스트 0개**, 간호술기 레거시(NursingModuleSet·예제 모듈)와 중단된 평가 코드가 계속 컴파일에 들어감.

**권고 순서**: (P0) 참조 가지치기 → 훅 자가 점검 → 공통 인터랙션 개념(IWorldClickable·EquipFx 등)을 아래로 → (P1) Content 를 Core / Equipment / VrUi / EvInput 으로 기능 단위 분리(③ 보호구, ⑤ UI 는 이 뒤) → (P2) Common 정리·네임스페이스·설정 주입·플랫폼 전략화.

## 8. 기능 폴더 분류 (2026-11-20, 완료)
어셈블리 경계는 두고 폴더만 기능별로 다시 나눴다 → **`코드_기능분류표.md`**(파일별 역할·비고). 배정표 `Tools/ModuleMigration/feature_map.py`, 이동 도구 `reorg.py`. 이전의 `assignment.json`·`migrate.py` 경로는 이 정리 전 기준이라 더는 쓰지 않는다.
Content 의 새 폴더: App · Lobby · Menu · Input · Scenario · Guide · Hud · Ui · Vr · Safety · Evaluation · Measure · Tools · Vehicle · Disassembly · Manual · World · Legacy · Debug · External. 다음 단계(asmdef 분리)는 이 경계를 따른다.

## 9. P0 정리 (2026-11-20, 완료 — 컴파일 오류 0·런타임 미검증)
1. **asmdef 참조 가지치기** — `Tools/ModuleMigration/prune_refs.py`(코드가 쓰는 네임스페이스의 소유 어셈블리만 남김, `--apply`). 외부 참조 수: Common 25→16, Interaction 24→6, Avatar 25→13, Engine 26→11, Content 35→25, Solo 1. **Engine→Avatar 참조를 끊었다**(실제로 안 씀). 스캔이 못 잡는 것(시그니처로만 노출되는 타입·여러 asmdef 가 같은 네임스페이스를 쓰는 AutoHand)은 스크립트의 `EXTRA` 에 적어 둔다: Common{AutoHandAssembly, Unity.XR.OpenXR}, Avatar{Unity.InputSystem}, Content{QuickOutline, LomoCamera, Unity.ResourceManager}. ※ 에디터(Windows)에서 컴파일되지 않는 `#if UNITY_ANDROID` 구간은 이 스캔이 포함하지만 컴파일로는 못 확인했다 → Android(VR·모바일) 빌드로 확인 필요.
2. **훅 등록 자가 점검** — `Common/Core/HookRegistry.cs`(훅 클래스의 public static 델리게이트 필드를 훑어 빈 것을 로그, `[OptionalHook]` 으로 제외) + `Content/App/Bindings/EvHookSelfCheck.cs`(앱 시작 직후 ScenarioHooks·AvatarHooks·GrabHooks·InteractionEvents·VrUi 점검, 모듈 공급자 ≥1 필수). 로그 `[HookCheck:EV]` — 정상이면 "훅 N개 모두 등록됨", 아니면 빠진 이름·오류. `ScenarioHooks.ProviderCount` 추가.
3. **공통 인터랙션 개념 정리** — `IWorldClickable` 을 `Interaction/Click/` 으로 이동(ClickableAnimator 를 아는 `WorldClickPriority` 는 `Content/World/Interactive/` 로 분리). `EquipFx.cs` 를 책임별로 분리: EquipFx(효과·소리·안내) / `Disassembly/Bolt/BoltWork·BoltHint·BoltHintFlash` / `Vr/Interaction/LocalHands`. 남은 '아래로 내릴 후보'(`EquipFx` 45개 파일이 사용, `Bolt`·`InteractablePart`)는 Content.Core 어셈블리 분리(P1)에서 처리.

## 10. P1 — Content 를 의존 층으로 4분할 (2026-11-20, 컴파일 오류 0·런타임/빌드 미검증)
**분석 결과**: 기능 폴더(Safety·Vr·Equipment…) 단위로는 어셈블리를 나눌 수 없다 — Content 의 .cs 269개 중 **103개(35.6k줄, Content 의 70%)가 하나의 순환 덩어리(강연결 성분)** 이다(Bolt·InteractablePart·EquipFx·ClickableAnimator·FreeLoto·CarPartService·GuideSystem·FreeLookController·VrPalmMenu 가 서로를 부름). 폴더로 자르면 어셈블리끼리 순환 참조가 되어 컴파일이 안 된다. 그래서 **코드는 한 줄도 바꾸지 않고, 의존 방향으로** 나눴다(`Tools/ModuleMigration/content_layers.py`, partial 클래스는 한 덩어리):

| 어셈블리 | 폴더 | 파일·줄 | 의미 | 참조 |
|---|---|---:|---|---|
| `DoDrill.Content.Base` | `Content/Base/…` | 36 · 4.7k | 코어가 쓰기만 하고 코어를 모르는 하위 부품(WorldHoverLabel·ScreenToast·Bolt 일부·LocalHands·LiftControllerBase…) | Common·Interaction·Avatar·Engine |
| `DoDrill.Content.Core` | `Content/Core/…` | 103 · 35.6k | EV 정비 도메인의 서로 얽힌 핵심 덩어리 | + Base |
| `DoDrill.Content.Shell` | `Content/Shell/…` | 72 · 6.3k | 코어와 무관한 껍데기 — 로비·메뉴·UI 위젯·평가 UI·간호술기 레거시·VR UI 일부 | + Base, Solo |
| `DoDrill.Content.Features` | `Content/Features/…` | 58 · 10.3k | 코어 위에 얹힌 기능 — EV 바인딩·시나리오 모듈·진단기·보관함 화면·HUD 레이아웃 | + Base·Core·Shell, Solo |
(각 폴더 안은 이전 기능 폴더 구조 그대로, 예: `Content/Core/Disassembly/Bolt/…`. 예전 `DoDrill.Content` 는 없앴다. 어셈블리 폴더·참조는 `prune_refs.py` 의 ASM 표가 원본.)
- 컴파일 확인 중 고친 것: `internal` 이던 `VrPartTray.PokeSurface`·`NetworkedSessionUserList.ResolveLocalDisplayName/PlatformToByte`·`HvStatusHud.MakeCircle` 를 `public` 으로(어셈블리 경계).
- **효과**: 새 코드가 아래→위 참조를 만들면 컴파일이 막는다(층 규칙 강제). Shell·Features 는 코어 없이 따로 컴파일되고, 코어는 껍데기·기능을 못 부른다.
- **한계**: ③ 보호구(Safety)·⑤ VR UI 의 알맹이(FreeLoto·ElectricSafetyManager·VrPalmMenu…)는 아직 Core 안이다.

### 코어를 쪼개려면 (다음 단계)
파일 단위 의존 390개 중 **약 59개를 끊어야 순환이 풀린다**(추정 최소). 가장 많이 '거꾸로' 불리는 대상 = FreeLoto(7), CarPartService(6), FreeLookController(4), GuideSystem(3), VrPalmMenu(3), FocusMode·AirImpactWrench·ImpactWrench·ScenarioExitHandler·VrControllerHints·FreeLockControllerMobile·CarPartRules·MiniMapComponent(각 2). 이 호출들을 `GrabHooks`·`AvatarHooks` 같은 훅/인터페이스로 바꾸면 Safety·VrUi·EvInput·Equipment 를 어셈블리로 뽑을 수 있다(작업량 큼, 기능 하나씩).

## 11. Core 분해 계획 — 전체 분석 결과 (2026-11-20, 분석만·코드 변경 없음)
**측정 방법**: Core 103개 파일을 최상위 타입 기준(중첩 타입·메서드명 오탐 제거)으로 기능 폴더 36개에 묶어 의존 그래프를 만들고, 아래→위 방향 순서를 정해 '거꾸로 가는 참조'를 센다. 거꾸로 가는 참조가 0이면 그 순서대로 어셈블리를 나눌 수 있다.

### 11.1 결론
- 순환의 원인은 **전체가 서로 얽힌 것이 아니라 소수의 허브 클래스**다. 목표 구조(아래 11.2)로 나누려면 끊어야 할 참조 타입이 **약 49개**(이 중 상당수는 같은 허브를 부르는 것이라 실제 작업은 **8개 작업 묶음**).
- 검색(어닐링)으로 본 하한: 어셈블리 6개·각 9k줄 이하 → 31, 8개 → 33, 10개 → 37. 즉 **의미 있게 나누려면 대략 30~50개 참조를 끊는 작업**이 필요하고, 그보다 적게는 안 된다.
- 허브(거꾸로 가장 많이 불리는 것): **CarPartService**(Safety·Equipment·Disassembly 안쪽에서 7타입), **FreeLoto**(Bolt·ClickableAnimator·Measure·Disassembly 등 4곳), **GuideSystem**(Disassembly·Measure·Evaluation·VrPalmMenu), **WarningPopup/DischargeTimerUI/UIHoverFader**(작은 UI 부품이 Core 안에 갇힘), **FreeLookController·FreeLockControllerMobile**(CreeperRide·EVInventorySlot 이 직접 부름), **VrPalmMenu**(EquipFx 가 설정값 하나 때문에 부름).

### 11.2 목표 어셈블리 (아래 → 위)
| 층 | 내용 | 줄 | 비고 |
|---|---|---:|---|
| Foundation | Tools/Core·World·World/Interactive·Disassembly/Bolt·Input/Shared·Vehicle/Body | 3.5k | 모든 기능의 바닥 |
| Measure | Measure·Measure/Probe(멀티미터·메거) | 3.0k | |
| **Safety (③)** | Safety/Accident·HighVoltage | 2.0k | 보호구·LOTO·감전/사고 |
| Equipment | Tools/Hand·Workshop | 4.8k | 공구·작업장 장비 |
| Disassembly | Disassembly/*·Manual/* | 8.5k | 분해·조립·타이어·보관함·매뉴얼 점검 |
| Vehicle | Vehicle/Service·Diagnostics | 0.8k | |
| Guide | Guide·Guide/View·MiniMap | 3.5k | |
| Hud | Hud·Hud/Session·Hud/Task | 1.7k | |
| **VrUi (⑤)** | Vr/Hints·Tutorial·Ui·Tools/Belt | 2.5k | |
| Input (①의 입력부) | Input/Pc·Mobile·External/Inventory | 4.0k | |
| Menu | Evaluation·Lobby·Menu/Popups | 1.3k | |
(③·⑤ 를 독립 모듈로 쓰려면 Safety·VrUi 가 아래쪽에 있을수록 좋다 — 그래서 아래 작업 묶음이 Safety 를 CarPartService·Guide 에서 떼어 내는 데 집중한다.)

### 11.3 작업 묶음 (효과 큰 순서 · 한 묶음씩 컴파일·동작 확인)
| # | 묶음 | 끊는 참조 | 방법 | 난이도 |
|---|---|---|---|---|
| 1 | **오탐·작은 부품 정리(기계적)** | WarningPopup·GuideMarker·LobbyPanel 은 이미 Base 로 내려갈 수 있음(Target·Follow 오탐), `UIHoverFader` 를 FreeLookController.cs 에서 별도 파일로, `VrProbeHandling.IsVRDevice`→`VrDevice`, `VrPalmMenu.InfoCardsAllowed`→설정 정적 클래스, `HoldScalePolicy` 의 소품 타입 검사→마커 인터페이스, `VrToastFollow` 이동 | ~15타입 | 파일 이동·이름 정리 | 쉬움 |
| 2 | **FreeLoto 의 자유 실습 상태 분리** | IsFreeScenario·FreeScenarioId·IsFreePractice·Battery12VOff 를 `FreePracticeState`(Foundation)로 — Bolt·ClickableAnimator·ElectricSafetyManager·Battery12VServiceVisual·HvVoltageCheck 가 FreeLoto(1.1k줄)를 안 부르게. `TryTerminalVoltage`(Measure)·`InterceptMsdLever`(ClickableAnimator)는 훅 | ~6 | 정적 클래스 추출 + 훅 2개 | 보통 |
| 3 | **CarPartService 허브 분리** | PartDef·Defs·IndexOf·NameOfBolt(데이터)를 `CarPartCatalog`(Foundation)로, 상태 조회(Instance·StateOf·IsFreeSlotNear·TryGetPartAnchor)는 `ICarPartService` 인터페이스(Foundation)로 — Safety·Equipment·Manual·Rules 가 구현이 아닌 인터페이스를 본다. CarPartService(3.5k줄) 자체는 Disassembly 에 그대로 | ~10 | 인터페이스 추출 | 보통~어려움 |
| 4 | **Guide 훅** | GuideSystem 직접 호출(ShowHintForCurrentTask·CenterPopupEnabled·Instance 조회·GhostManager·NextActionGuide)을 `GuideHooks`(Foundation 또는 Engine)로 | ~6 | 훅/이벤트 | 보통 |
| 5 | **입력 컨트롤러 인터페이스화** | CreeperRide·EVInventorySlot 이 FreeLookController/FreeLockControllerMobile 을 직접 찾는 것을 `IPcLookController`(Avatar 에 이미 있음)로 — 필요한 멤버만 인터페이스에 추가 | ~4 | 기존 인터페이스 확장 | 쉬움 |
| 6 | **Hud 정리** | DischargeTimerUI·FreeDischargeHud·EVInventoryUI.TryToggle·EvaluationClientUI 호출을 이벤트/훅으로 | ~6 | 훅 | 보통 |
| 7 | **차량·장비 잔여** | ShopCrane.All(Accident), CoolantReservoir(Rules·Body), Battery12VServiceVisual(FreeLoto), AirImpactWrench(Workshop), TorqueSpec(Hand), TireWheel/TireService(Foundation) — 각각 인터페이스 또는 위치 이동 | ~10 | 개별 | 보통 |
| 8 | **어셈블리 분할 실행** | 11.2 표대로 폴더·asmdef 분할(`content_layers.py` 와 같은 방식, 층 순서만 11.2 로) | — | 도구 + 컴파일 오류 수정 | 쉬움(위 1~7 뒤) |

### 11.4 순서·위험·확인
- **순서**: 1 → 2 → 3(가장 큼) → 4·5 → 6·7 → 8. 1·5 는 위험이 낮고 효과가 바로 보인다(Core 가 줄어듦). 3 이 끝나면 Safety·Equipment 가 Disassembly 에서 풀린다.
- **매 묶음 뒤**: `content_layers.py` 로 Core 크기 재측정(줄어야 함) → 컴파일 → PC·모바일·VR 시나리오 한 번 → `[HookCheck:EV]` 확인.
- **위험**: ① 훅 추가가 늘면 '등록 누락 = 기능 사라짐' 위험이 커진다 → HookRegistry 가 새 훅을 자동 점검하므로 필수. ② FreeLoto·CarPartService 는 네트워크 메시지·RPC 를 가진다 → 클래스 이름·메시지 구조를 바꾸지 말고(서버·클라 계약 유지) 정적 상태·인터페이스만 새로 만든다. ③ 씬·프리팹이 MonoBehaviour 를 GUID 로 참조하므로 **MonoBehaviour 클래스는 파일을 쪼개지 말고**(새 클래스만 새 파일) 이동만 한다.
- **끝났을 때 얻는 것**: Safety(③)·VrUi(⑤)·Input(①)·Equipment·Disassembly 가 컴파일 단위로 독립 → 다른 프로젝트에 어셈블리 단위로 가져가고, 훅으로만 연결.

## 12. 묶음 1 결과 — Core 103 → 46개 (2026-11-20, 컴파일 오류 0·런타임/빌드 미검증)
**한 일(동작 변경 없음)**
- `VrProbeHandling.IsVRDevice` → `VrDevice.IsActive`(Common) 20개 파일 치환 — 가장 큰 순환 고리였다(25곳이 VrProbeHandling 클래스 전체에 매여 있었음).
- `InfoCardPolicy`(새 정적 클래스)로 설명 카드 설정을 분리 → EquipFx 가 VrPalmMenu 를 더 이상 부르지 않음(VrPalmMenu.ShowInfoCards·InfoCardsAllowed 는 그대로 위임).
- `UIHoverFader` 를 FreeLookController.cs 에서 별도 파일로.
- `ToolMarkers.cs`: `IHoldScaleExempt`(HoldScalePolicy 용)·`IFloorCarried`(Openable 용) 마커 인터페이스 → 장비 클래스 6개에 붙이고 HoldScalePolicy·Openable 이 구체 장비 타입을 직접 부르지 않게.
- 층 분류 도구를 **최상위 타입 기준**(중첩 타입·메서드명 오탐 제거)으로 바꾸고 다시 배치(`content_layers.py`, `EvHookSelfCheck` 는 Features 로 고정).
- `ToolRenderSnap.FindAvatarHandBone` internal→public, prune_refs: Content 층들이 파일 이동 때마다 출발 참조(CONTENT_PREV)에서 다시 고르도록, Unity.XR.Interaction.Toolkit 추가.

**결과**: Core 103개(35.6k줄) → **46개(17.4k줄)**, Base 46(8.6k), Features 72(19.7k), Shell 108(11.3k). Core 의 순환이 풀려 이제 Core 안의 어셈블리 간 거꾸로 참조는 **기능 폴더 수준 약 20개**(이전 49개 → 20개).

**남은 Core(46개)와 끊어야 할 것 — 묶음 2 이후**
- Safety ↔ Disassembly: `CarPartService`·`CarPartRules`·`CarPartAccident`·`EmergencyEventManager` ↔ `FreeLoto`·`ElectricSafetyManager` (묶음 2·3).
- Measure ↔ Safety: `MultimeterMaster` → `FreeLoto.TryTerminalVoltage`/`HvVoltageCheck`/`ElectricSafetyManager` (묶음 2 훅).
- Guide: `GuideSystem` ← Evaluation·VrProbeHandling (묶음 4).
- Tools/Hand → Workshop(`AirCompressor`·`SocketRack`), Tools/Hand → Disassembly/Bolt(`BoltHint`·`BoltPartName`·`BoltWork` 가 Core 에 있음 → Bolt 계열을 아래로) (묶음 1 후속).
- Hud → Safety(`FreeDischargeHud`·`HvShockGauge`·`LOTOSystem`), Vehicle/Body ↔ Service(`CoolantReservoir`), World/Interactive → `FreeLoto`.
