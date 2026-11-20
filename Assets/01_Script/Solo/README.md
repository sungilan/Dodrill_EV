# DoDrill.Solo (L3)

'혼자 하기' 모드다. 마스터 서버나 방 없이, 이 기기 안에서 FishNet 호스트(서버+클라이언트)를 시작해 바로 훈련한다. 참조: FishNet만 쓴다.

- `SoloModeLauncher.IsSolo`: 혼자 하기로 실행 중인지
- `SoloModeLauncher.ScenarioId`: 시작할 시나리오(Resources/Scenarios 파일명)
- 시작 전에 MST 방 서버·클라이언트 부품을 떼어 낸다. 그대로 두면 '방 접근 토큰 없음' 오류가 난다.
- 단계마다 `[Solo]` 로그를 남긴다. 실패하면 마지막 로그를 보면 된다.
