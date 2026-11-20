# DoDrill.Avatar (L2)

PC·모바일·VR 플레이어 아바타와 PC·모바일 1인칭 이동·시점 공통부를 담당한다. 참조: Common, Interaction.

## 내용

| 폴더 | 내용 |
|---|---|
| `Player` | `BasePlayer`, `PlayerSpawnner`, `AvatarBodyFit`(키 맞춤), `HeldObjectHandAttach`, `AvatarHooks` |
| `Vr` | `VRPlayer`, `VrLeftLaser` |
| `PcMobile` | `PcMobileAvatarController`(애니메이터·IK 구동), 팔 뻗기·머리 조준·앉기 IK, `PlayerCollision`(벽 충돌), `IPcLookController`, `PcMobileMotor`(이동·충돌·시점·카메라 준비 공통부), `MobileTouchUi`(모바일 UI 터치 보강), `SimplePcInteractor`(아바타 없는 최소 1인칭 조작: 이동·시점·클릭으로 집기/놓기/누르기 — 새 콘텐츠 시험용) |

## PC·모바일 조작 컨트롤러 만들기

입력을 읽는 컨트롤러는 콘텐츠 쪽에 둔다. EV는 `FreeLookController`(PC)와 `FreeLockControllerMobile`(모바일)을 쓴다. 이동과 시점은 `PcMobileMotor`를 호출한다.

```csharp
var dir = PcMobileMotor.PlanarDirection(transform, input) * moveSpeed;
_planarVelocity = PcMobileMotor.MoveWithCollision(transform, dir * Time.deltaTime, true, 0.28f, 1.7f, avatarScale, moveSpeed);
PcMobileMotor.ApplyLook(transform, cameraPivot, useAvatarRig, pitch, yaw);
```

`IPcLookController`를 구현하면 `PcMobileAvatarController`가 걷기 애니메이션과 IK를 자동으로 구동한다.

## 훅 (`AvatarHooks`)

| 훅 | 설명 | EV 등록 |
|---|---|---|
| `WorldClicked`(event) | 고정 오브젝트를 눌렀을 때 손 뻗기 | `ClickableAnimator`가 호출 |
| `ToolBusy` | 공구 작동 중(반대 손 뻗기 생략) | `ImpactWrench.RecentlyWorking` |
| `GroundBonus` | 접지 높이 보정(사다리 발판) | `LadderProp.BonusAt` |
| `ClimbStep` | Q/E·서기/앉기 버튼으로 한 칸 오르내리기 | `LadderProp.TryClimbStep` |
| `NoticeHandler` | 화면 알림 | `EquipFx.Notice` |

## 참조 플러그인

FishNet, AutoHand(+FinalIK·OpenXR 연동), FinalIK(RootMotion), InputSystem, XR Management, UI, SerializedCollections
