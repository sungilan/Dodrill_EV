# DoDrill.Safety (L3)

고전압 안전 상태, LOTO(잠금·표지), 사고 연출·처리, 보호구 착용 동기화를 담당한다. 참조: Common, Interaction, Avatar, Engine.

## 내용

| 폴더 | 내용 |
|---|---|
| `HighVoltage` | `ElectricSafetyManager`(절연 장갑·서비스 플러그 상태, 시나리오 진행과 동기화), `LOTOSystem`(자물쇠 체결), `SafetyUIHandler` |
| `Accident` | `EmergencyEventManager`(사고 종류·팝업·평가 연동), `AccidentFx`(감전·누액 등 연출) |
| `Ppe` | `PlayerEquipmentSync`(인원별 보호구 착용 동기화), `GloveItem`, `InsulationMat` |
| `Hooks` | `SafetyHooks` |

## 훅 (`SafetyHooks`)

| 훅 | 설명 | EV 등록 |
|---|---|---|
| `IsPracticeScenario` | 단계 없는 연습 시나리오인가 | `FreeLoto.FreeScenarioId` 비교 |
| `IsFreePractice` | 지금 자유 연습 중인가(사고 뒤 다시 시도) | `FreeLoto.IsFreePractice` |
| `IsResultShowing` | 평가 결과 화면이 떠 있는가 | `EvaluationClientUI.IsResultShowing` |
| `LotoLocked` | 자물쇠 체결 시(방전 타이머 시작) | `DischargeTimerUI.StartDischarge` |
| `Toast` | 화면 알림 | `TaskProgressUI.RequestToast` |
| `DisplayName` | 프리팹 ID → 표시 이름 | `HoverNameFormatter.Format` |

EV 등록 위치는 `Content/Features/App/Bindings/EvSafetyBinding.cs`이다. 등록이 없으면 연습 모드 아님, 결과 화면 없음, 알림 없음, ID 그대로 표시로 동작한다.

## 참조 플러그인

FishNet, URP, TextMeshPro, XR Interaction Toolkit, UI
