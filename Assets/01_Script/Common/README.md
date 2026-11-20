# DoDrill.Common (L0)

모든 모듈이 공통으로 쓰는 바닥층이다. 다른 DoDrill 모듈을 참조하지 않는다.

## 내용

| 폴더 | 내용 |
|---|---|
| `Core` | `Define`, 확장 메서드, `Managers`(사운드 등), `HookRegistry`(훅 등록 자가 점검), `NetworkObjectFinder`, `UserInfo` |
| `Platform` | PC·모바일·VR 플랫폼 판정 |
| `Ui` | `VrUi`(VR 여부·HUD 캔버스 생성 도우미) |
| `Network` | 빌드 호환 확인, 접속 시간 초과 감시, 빈 룸 서버 자동 종료(`RoomIdleShutdown`), 마스터 스포너 감시(`SpawnerWatchdog`) |
| `Keyboard` | VR 한글 키보드 |
| `Performance`, `Diagnostics`, `SpatialAnchor`, `World` | 프레임 설정, 모바일 최적화, 안드로이드 파일 로그 등 |

## 훅

| 훅 | 설명 | EV 등록 위치 |
|---|---|---|
| `VrUi.IsVRProvider` | 지금 VR로 실행 중인가 | `EvAvatarBinding` |

## 참조 플러그인

FishNet, MasterServerToolkit, AutoHand, FinalIK(RootMotion), InputSystem, URP, TextMeshPro, XR(Management·OpenXR·ARFoundation·Meta), UI, SerializedCollections

## 주의

- `RoomIdleShutdown`은 배치 모드 전용 서버에서만 동작한다(접속자 0명이 3분/2분 지속되면 종료).
- `SpawnerWatchdog`은 스포너가 한 번 등록된 프로세스(마스터)에서만 동작한다.
