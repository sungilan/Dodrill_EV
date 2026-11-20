# 모듈 최소 예제

EV 콘텐츠 없이 `DoDrill.Engine`(+Common, Interaction, FishNet)만으로 시나리오를 완주하는 예제다.

## 실행

1. 메뉴 **Tools ▸ DoDrill ▸ 모듈 최소 예제 씬 만들기** → `Example_MinimalScenario.unity` 생성. 지금 열린 씬은 건드리지 않는다.
2. 생성된 씬을 열고 Play.
3. 화면 왼쪽 위 로그 순서:
   - 모듈 등록(`Example_Wait`, `Example_PressKey`)
   - 시나리오 시작 → '잠시 대기' 3초 뒤 자동 완료
   - '스페이스 키 누르기' → Space를 누르면 완료
   - 모든 단계 완료 → 시나리오 종료

## 파일

| 파일 | 내용 |
|---|---|
| `Scripts/ExampleModules.cs` | 단계 모듈 2개(`ITaskModule` 구현), `extra` 설정 읽기 |
| `Scripts/ExampleScenarioStarter.cs` | 모듈 등록 → 호스트 시작 → JSON 읽기 → `ScenarioRunner.StartScenario` |
| `Scenarios/example_minimal.json` | 시나리오 정의(단계 2개) |
| `Editor/ExampleSceneBuilder.cs` | 예제 씬 생성 메뉴 |
| `Example_MinimalScenario.unity` | 생성된 예제 씬 |
| `ExampleSpawnablePrefabs.asset` | FishNet 스폰 프리팹 목록(빈 목록 — 예제는 네트워크 오브젝트를 스폰하지 않음) |

## 새 프로젝트로 옮길 때

- 시작기의 등록 코드는 실제 프로젝트에서 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 바인딩 파일로 옮긴다(EV의 `Ev*Binding` 참고).
- 시나리오 JSON은 `Resources/Scenarios/`에 두고 `ScenarioLoader.Load(id)`로 읽는다. 예제 JSON은 EV 로비 목록에 섞이지 않도록 Resources 밖에 둔다.
- 빌드 씬 목록에는 넣지 않았다.
