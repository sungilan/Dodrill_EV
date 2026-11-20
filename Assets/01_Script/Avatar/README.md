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

## 범용 프리팹

| 프리팹 | 내용 |
|---|---|
| `Prefabs/Players/GenericPlayer.prefab` | 콘텐츠 없는 PC 1인칭 플레이어(네트워크). 몸통(캡슐)·카메라 받침·카메라·`HoldPoint`(태그), `LocalNetworkTransform`, `GenericNetworkPlayer`, `SimplePcInteractor`. 내 것일 때만 카메라·조작이 켜지고 남의 것은 몸통만 보인다. FishNet `PlayerSpawner`의 Player Prefab에 넣으면 접속자마다 생긴다 |
| `Prefabs/Players/GenericAvatarPlayer.prefab` | `GenericPlayer`의 몸을 정비사 아바타(`PcAvatar_Mechanic`)로 바꾼 플레이어. `SimplePcInteractor`(IPcLookController 구현)의 이동·시점 값으로 `PcMobileAvatarController`가 걷기 애니메이션·IK·손 뻗기·머리 방향을 구동하고 `NetworkAnimator`가 다른 사람 화면에 전파. 웅크리기는 왼쪽 Ctrl |
| `Prefabs/Players/VR_Player.prefab` | EV 스크립트 없는 VR 플레이어(Ui·Safety 모듈 스크립트 포함) |
| `Prefabs/Avatars/PcAvatar_*.prefab` | PC 아바타(몸) |

- 프로젝트 Tags에 `HoldPoint`가 있어야 한다(`SyncGrab`이 이 태그로 내 손 위치를 찾는다).
- 조작: WASD 이동, 오른쪽 버튼 끌기 시점, 왼쪽 클릭 집기·놓기·누르기, F 사용. 아이템을 들고 작업 구역을 보며 클릭하면 그 자리에 얹어 놓는다.
- 만들기: 메뉴 **Tools ▸ DoDrill ▸ 범용 프리팹 만들기**(`99_ModuleExample/Editor/GenericPrefabBuilder.cs`). 이미 있는 프리팹은 건너뛴다.

## 아트 에셋 위치

아바타가 쓰는 캐릭터 아트는 이 폴더 안에 있다(2026-11-20 이동, GUID 유지).

| 폴더 | 내용 |
|---|---|
| `Art/Character/FBX` | 정비사(`Rig_Player_Mechanic`)·특수요원(`Swat`) 모델, 애니메이터 컨트롤러, 기본 아바타 |
| `Art/Character/Materials`, `Art/Character/Textures` | 정비사·특수요원·청년 재질과 텍스처 |
| `Art/Vr/Materials`, `Art/Vr/Textures` | VR 플레이어(손·보안요원) 재질과 텍스처 |

패키지 샘플(XR Hands `HandVisualizer` 손 모델 2개)은 옮기지 않았다 — XR Hands 패키지의 샘플을 가져오면 된다.
