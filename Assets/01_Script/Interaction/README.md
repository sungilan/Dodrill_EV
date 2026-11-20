# DoDrill.Interaction (L1)

물건 잡기·네트워크 동기화와 '작업 아이템이 존에 들어갔다' 같은 상호작용 신호를 담당한다. 참조: Common.

## 내용

| 폴더 | 내용 |
|---|---|
| `Grab` | `SyncGrab`(VR·PC 공통 잡기, 서버 권한 동기화, 놓은 뒤 서버 안착), `LocalNetworkTransform`, `KinematicGrab`, `SnapGuide`, `HeldItemOnTop` |
| `Hand` | `PcHandGrip` |
| `Task` | `TaskItem`(작업 아이템 ID), `TaskInteractionZone`(놓는 자리), `InteractionEvents`(신호 발생 → 서버 모듈 전달) |
| `Click` | `IWorldClickable`(PC 클릭·VR 찌르기 대상), `ClickProxy`(델리게이트로 동작을 지정하는 범용 클릭 대상) |

## 훅

| 훅 | 설명 |
|---|---|
| `GrabHooks.ItemGrabbed` | 이 클라이언트가 물건을 집었을 때(prefabId) |
| `GrabHooks.HasGuideMarker` | 안내 마커가 이미 붙은 도구인가 |
| `GrabHooks.ShowNameTag` / `HideNameTag` | 가리킨 물건 이름표 표시/숨김 |
| `GrabHooks.IsMeter` | 계측기인가(VR에서 맨 앞에 그림) |
| `GrabHooks.IsNotHandheld` | 손에 드는 물건이 아닌가(레이저로 당겨 오지 않음) |
| `InteractionEvents.ZoneValidator` | (zoneId, itemId) 존 신호 유효성 |

EV 등록 위치는 `Content/Features/App/Bindings/EvInteractionBinding.cs`이다.

## 참조 플러그인

FishNet, AutoHand, DOTween, Localization
