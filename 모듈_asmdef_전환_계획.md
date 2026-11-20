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
