# DoDrill.Ui (L3)

콘텐츠와 무관한 화면 부품 모음이다. 참조: Common, Interaction, Avatar, Engine.

## 내용

| 폴더 | 내용 |
|---|---|
| `Widgets` | `ClickButton`(눌림 그래픽·고정 표시 `IPressLatch`), `ToggleButton`, `SlidingPanel`, `ClickDropdown`, `CustomInputField`, `SimpleTogglePanelUI`, `BillboardUI`, `PopupWorldSpaceAnchor` |
| `Vr` | `VRUIFollower`(시선 따라오는 창), `VrToastFollow`, `VRUiMetaRepositionDrag`(창 재배치), `VrUiMasterVolumeToggle`, `VrTaskListToggle` |
| `Hud` | `ScreenToast`, `WarningPopup`(PC·모바일은 토스트, VR은 월드 창), `WorldHoverLabel`, `ItemNameTag`, `UIHoverFader`, `CountdownUI`, `PlayerUISetup`, `InfoCardPolicy` |
| `Hud/Task` | `SimpleTaskHud`(프리팹 없는 시험용 단계 표시), `TaskProgressUI`(단계 진행 패널, 엔진 상태 구독), `TaskListPanel`, `TaskItemUI`, `ProgressHUD` |

## 콘텐츠와 연결

- 단계 진행: `TaskProgressUI`가 엔진의 `ScenarioStateReceiver` 이벤트를 직접 구독한다. 단계 제목은 시나리오 JSON의 `extra` "title"을 쓴다.
- 보조 진행 문구: `TaskProgressUI.SetSubProgress("남은 볼트: 3 / 8")`. 빈 값을 넘기면 숨긴다.
- 알림 팝업: `TaskProgressUI.RequestToast(richText)`
- VR 여부: Common의 `VrUi.IsVR`를 쓴다.

## 훅 (`UiHooks`)

| 훅 | 설명 | EV 등록 |
|---|---|---|
| `DisplayName` | 내부 ID → 표시 이름(호버 라벨) | `HoverNameFormatter.Format` |

EV 등록 위치는 `Content/Features/App/Bindings/EvUiBinding.cs`이다.

## 참조 플러그인

FishNet, AutoHand, DOTween, InputSystem, Localization, TextMeshPro, UI
