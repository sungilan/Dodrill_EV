# Ioniq5N 프리팹 — 부품 이름 연결 · 매뉴얼 기준 누락 부품 (2026-11-17)

기준: 게임에서 생성되는 `Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N.prefab` (켜진 변형 `FBX_ElectricCar` 하위).
매뉴얼: 전기차_자료 PDF 51종(구성부품·부품위치, 서스펜션, 배터리 제어, 바디 등).
좌표: 앞 = −X, 왼쪽(운전석) = −Z, 위 = +Y. 바퀴 인덱스 `SM_Wheel_01~04` = 앞왼쪽·앞오른쪽·뒤왼쪽·뒤오른쪽.

## 0. 이번에 분리한 한 덩어리 메시 (CarMeshSplitter, 메뉴 `Dodrill > 차량 메시 분리`)
| 원본(비활성으로 보존) | 새 오브젝트 | 비고 |
|---|---|---|
| `SM_Breaksystem` (214조각, 41,076삼각형) | `SM_Brake_FL/FR/RL/RR` (각 약 9.4천 삼각형, 바퀴 코너마다 캘리퍼·디스크·방진판) + `SM_BrakeLines` (3.6천, 길게 늘어진 라인·마스터 실린더) | 코너별 분리 완료, 라인은 분리 대상 아님 |
| `SM_Seats` (106조각) | `SM_Seat_FL`(운전석) / `SM_Seat_FR`(동승석) / `SM_Seat_Rear`(뒷좌석 벤치) | 삼각형 합계 22,652 = 원본과 같음 |

| `SM_RearSuspension` (151조각, 21,866삼각형) | `SM_RearCrossmember`(프레임: 가로 막대 + S자 측면 레일, 7.1천) / `SM_RearShock_L·R`(스프링·댐퍼·탑마운트) / `SM_RearAxle_L·R`(너클·허브·스터드) / `SM_RearLowerArm_L·R` / `SM_RearTrailingArm_L·R` | 매뉴얼 10개 중 크로스 멤버·쇽·액슬·로어 암·트레일링 암 분리. 어퍼 암 프런트·리어·어시스트 암은 가는 링크가 다른 조각에 합쳐져 있어 따로 나누지 못함. 스태빌라이저는 생성 파츠 유지 |

메시 애셋은 `Ioniq5N_Split/` 폴더에 저장. 원본 오브젝트는 지우지 않고 꺼 둠(FBX 보존). 같은 방식으로 다른 덩어리도 분리 가능(§3).

### 0-1. 차체 외판 분리 (CarBodyPanelSplitter, 메뉴 `Dodrill > 차량 메시 분리 > 차체 외판(매뉴얼 구성부품)`)
`SM_Ioniq_Electric`은 도장·유리·안쪽 철판·트림 4개 서브메시에 차 외판 대부분이 들어 있는 한 덩어리였다. 조각(island) 단위로는 펜더와 쿼터 패널이 붙어 있어 못 나누므로, **평면으로 삼각형을 잘라(정점 보간) 경계를 직선으로** 만들었다. 장착 상태에선 정점·UV가 원본과 같아 이음매가 보이지 않는다.

| 새 노드 (부모) | 원본 | 분해 부위(`CarPartService`) | 선행 / 토크 |
|---|---|---|---|
| `SM_FenderFL·FR` (FBX_ElectricCar) | Electric 도장, x<−1.34·y<1.14·\|z\|>0.62 | `FenderFL·FR` | 앞 범퍼·헤드램프·해당 앞바퀴 / 0.8~1.2 |
| `SM_SideSillL·R` | Electric 도장, 앞뒤 휠 사이·y<0.28 | `SideSillL·R` | 같은 쪽 펜더 / M6 일반 |
| `SM_MirrorL·R` (각 앞 도어 Parent) | 앞 도어 메시에서 잘라 냄 | `MirrorL·R` (parent 도어) | 도어 / 0.7~1.1 |
| `SM_RearSpoiler` | Electric 도장, 테일게이트 위 | `RearSpoiler` (parent 테일게이트) | 0.5~0.8 |
| `SM_WiperFront` | Electric 도장 조각 | `WiperFront` | 2.3~2.7 |
| `SM_Tailgate_Upper·Glass·Inner` | Electric 도장·유리·안쪽 패널 | 기존 `Tailgate` objects에 추가 | 테일게이트를 떼면 차에 남던 윗부분·뒷유리 |
| `SM_Windshield` | Electric 유리(앞) | 없음 — 접착식이라 정비 실습 대상 아님. 노드만 분리 | |

- 뚫림 방지: 떼어 내는 부품과 남는 차체(`SM_Ioniq_Electric_Body`) 모두 뒷면 삼각형(법선 반대, 0.4mm 안쪽)을 붙였다. 부품을 들고 돌려도 안쪽이 비어 보이지 않고, 펜더를 떼면 차체 쪽 안쪽 면이 보인다. 유리는 제외.
- 범퍼·후드·프렁크·뒷도어·테일게이트·하부 커버·12V 커버·콕핏·시트도 같은 이유로 `<이름>_2S` 메시(양면)로 교체. 삼각형 수는 해당 부품만 약 2배.
- 백업: `D:/claude_tmp/body/Ioniq5N.prefab.bak_before_panels`. 프리팹 diff는 노드 추가·메시 참조 교체만 있음.
- 볼트 위치는 각 부품 메시 정점에서 0~2.5cm(사이드 실 가운데 두 개만 정점 간격이 넓어 3.7·5.9cm).
- 휠 하우스 정리: 앞 휠 하우스 안쪽 벽(|z|≈0.61) 바깥에 차 안쪽을 향한 대각선 삼각형이 좌우 3장씩 겹쳐 있었다. 차체를 양면으로 바꾸면서 이 면의 뒷면이 휠을 뺐을 때 검은 쐐기로 보였다 → `_Drop_WheelWell` 영역으로 버림(차체 77,070 → 77,058 삼각형, 뒤에 안쪽 벽이 있어 뚫리지 않음). 큰 평면 삼각형 나머지(바닥·안쪽 벽)는 정상 형상이라 그대로 둠. 뒤 휠 하우스 안쪽 벽이 대각선으로 끝나는 건 그 위로 리어 쇽·스프링이 보여야 해서 막지 않음.
  - 이 수정은 프리팹을 다시 적용하지 않고 `SM_Ioniq_Electric_Body.asset`만 덮어썼다(GUID 유지, 백업 `D:/claude_tmp/body/SM_Ioniq_Electric_Body.asset.bak_before_wheelwell`).
- 남은 문제: 휠 가드(펜더 인슐레이터)는 별도 메시가 없어 분리 안 함. 휠 하우스 앞뒤 끝에 수평 선반 면(y≈0.7)이 남아 있어 아래에서 보면 얇은 선처럼 보인다.

### 0-2. 차체 셸 분리 — `SM_Ioniq_Electric` 해체 (CarShellSplitter, 2026-11-17 2차)
외판 분리(§0-1) 뒤에도 `SM_Ioniq_Electric` 에는 루프·필라·카울·플로어·센터 콘솔·휠하우스가 **한 덩어리(조각 1개, 약 2.7만 삼각형)** 로 용접돼 남아 있었다. 조각 연결로는 못 나누므로 **모서리 각도(25°)로 삼각형을 '패치'로 쪼개고**(자르지 않아 틈이 없다), 패치의 위치·크기·방향으로 매뉴얼 구성부품에 배정했다. 패치가 다른 부품과 한 덩어리인 곳(콘솔 앞쪽 하부 벽)만 상자 평면으로 잘랐다. 메뉴 `Dodrill > 차량 메시 분리 > 차체 외판(…) 적용 + 부품 양면화` 한 번이면 §0-1 과 이 절이 같이 적용된다(FBX 원본에서 다시 계산). 백업: `D:/claude_tmp/body/Ioniq5N.prefab.bak_before_shell`.

| 새 노드 | 매뉴얼 구성부품 | 분해 부위 | 비고 |
|---|---|---|---|
| `SM_FloorConsole` | 플로어 콘솔 어셈블리(어퍼 커버·리어 커버·컵 홀더·암레스트) | `FloorConsole` (볼트 4, 0.4~0.6) | 센터 터널 위 상자(x −1.13~−0.015, 폭 ±0.205, 높이 0.44~0.93)로 잘라 낸 한 벌. 밑·잘린 면은 평평한 뚜껑으로 막음 |
| `SM_FloorCarpet` | 플로어 카펫 어셈블리 | `FloorCarpet` (M6, 시트·콘솔·스커프 선행) | 바닥 수평 패치 |
| `SM_VisionRoof` | 비전 루프 어셈블리(글라스·롤러 블라인드) | `VisionRoof` (0.8~1.2) | 루프 중앙 글라스·블라인드 |
| `SM_CowlTopCover` | 카울 탑 커버 어셈블리 | `CowlTopCover` (와이퍼 암 선행) | 윈드쉴드 아래 띠 |
| `SM_PillarTrimFrontL·R` | 프런트 필라 트림 | `PillarTrimFrontL·R` | A 필라 아래쪽 안쪽 |
| `SM_DoorScuffL·R` | 도어 스커프 트림 | `DoorScuffL·R` | 문턱 위 얇은 판(앞·뒤 도어를 이은 한 장) |
| `SM_InsideMirror` | 인사이드 미러 어셈블리 | `InsideMirror` | 윈드쉴드 위 가운데 |
| **`SM_BodyShell`** (옛 `SM_Ioniq_Electric`) | **차체 골격(바디 셸)** | 등록 안 함(탈거 불가) | 루프 패널·필라·루프 레일·사이드 실·플로어 팬·휠하우스·뒷선반·방화벽 등 **구조물 한 벌**. 매뉴얼에 구성부품이 없는 '차체 자체'라 한 노드로 두되 이름을 분명히 했다 |

- `SM_Ioniq_Electric` 이름은 프리팹에서 사라졌다(코드는 주석에서만 참조). `FindSrc` 가 두 이름을 같게 취급해서 도구를 다시 돌려도 된다. 옛 `SM_Ioniq_Electric_Body.asset` 은 삭제.
- 장착 상태 겉모습은 원래와 같다 — 정점·UV 를 그대로 쓰고 콘솔 상자 경계만 잘랐다(전체 차 렌더로 비교 확인).
- **뚫린 부분 메우기** — 열린 가장자리를 고리로 이어, 평평한 고리는 귀 자르기로, 휜 고리는 중심 부채꼴로 막는 `CloseLoops` 를 만들었다. 적용 대상은 **의자 3종·12V 배터리·12V 커버·콘솔·인사이드 미러·셸의 콘솔 자리** 뿐(그릴·창틀 같은 진짜 개구부는 막으면 안 되므로 `CloseNodes` 목록으로 한정). 12V 배터리 밑과 의자 밑을 마젠타 배경으로 렌더해 비쳐 보이는 곳이 없음을 확인했다.
- `Battery_12V_Cover` 는 이미 `_2S` 인 메시를 한 번 더 양면화하던 문제를 고쳤다(원본 `SM_12v_top` 으로 되돌아가 다시 만듦, 446 → 924 삼각형).
- **알려진 한계**: ① 콘솔을 뗀 뒤 위에서 보면 중앙 터널 바닥에 U자 틈이 남는다 — 모델 바닥에 알파 구멍 무늬가 있어서이고, 그 아래에 배터리 팩이 있어 차 전체로는 배경이 비치지 않는다. ② 콘솔 앞쪽 끝에 가는 평면 조각 두 개가 삐져나온다. ③ 센터/리어 필라 트림·카울 사이드 트림·오버헤드 콘솔·선바이저는 메시가 따로 없어 분리하지 못했다(§7). ④ 플레이·네트워크 동작 확인은 아직 못 했다(컴파일·프리팹 노드 이름 대조만).

## 1. 모델 노드 ↔ 부품 이름 ↔ 분해 시스템 (켜진 메시)
| 모델 노드 | 한글 부품 이름 | 자유 실습 탈거 |
|---|---|---|
| SM_Ioniq_Bumper | 앞 범퍼 | ○ FrontBumper |
| SM_Ioniq_glass + SM_Ioniq_Bumper_Lights | 헤드램프 | ○ Headlamp |
| Hood_Parent/Hood_Latch_Point | 후드 | ○ Hood |
| SM_Frunk.001 + SM_Frunk_lid | 프렁크(앞 트렁크) | ○ Frunk |
| SM_Gearbox + SM_ElectricEngine | 전륜 모터·감속기 | ○ FrontMotor |
| SM_FrontSuspension | 전륜 서브프레임·서스펜션(한 덩어리) | ○ FrontSuspension |
| SM_Bottom_01 / SM_Bottom_02 | 프런트 / 리어 언더 커버 | ○ |
| SM_RearElectricengine + SM_RearPowerDistribution | 후륜 모터·감속기(IDA) | ○ RearMotor |
| SM_RearCrossmember / SM_RearShock_L·R / SM_RearAxle_L·R / SM_RearLowerArm_L·R / SM_RearTrailingArm_L·R | 리어 크로스 멤버 / 쇽 업소버 / 액슬(너클·허브) / 로어 암 / 트레일링 암 (분리 완료) | ○ RearSuspension, RearShockL·R, RearAxleL·R, RearLowerArmL·R, RearTrailingArmL·R |
| SM_Ioniq_DoorFrontLeft/Right, DoorRearLeft/Right | 앞·뒤 도어 4종 | ○ |
| SM_Ioniq_Back | 테일게이트 | ○ Tailgate |
| SM_Ioniq_Back.001~.005 | 뒤 범퍼 | ○ RearBumper |
| SM_Ioniq_Cockpit | 크래시 패드(스티어링 휠 포함) | ○ Cockpit |
| SM_Seat_FL / FR / Rear | 앞 좌석 좌·우 / 뒷좌석 | ○ SeatFL/FR/Rear |
| SM_Brake_FL/FR/RL/RR | 브레이크(코너별) | ○ BrakeFL/FR/RL/RR (해당 휠을 떼야 접근) |
| SM_12v + Battery_12V_Cover | 12V 보조 배터리 / 커버 | ○ Battery12V, Battery12VCover |
| SM_Wheel_01~04 | 휠·타이어 | ○ TireService |
| ~~SM_Ioniq_Electric~~ → 아래 부품들 + `SM_BodyShell` | 차체 외판 일체를 §0-1·§0-2 로 해체: 펜더 · 미러 · 스포일러 · 와이퍼 · 테일게이트 위 · 사이드 실 · 플로어 콘솔 · 플로어 카펫 · 비전 루프 · 카울 탑 커버 · 프런트 필라 트림 · 도어 스커프 · 인사이드 미러 | ○ 분리 완료(남은 구조물 `SM_BodyShell`(차체 골격)은 탈거 불가) |
| SM_Ioniq_Back.006 | 뒤쪽 외장(트렁크 하단 추정) | ✕ |
| SM_BrakeLines | 브레이크 라인·마스터 실린더 | ✕ (분리 대상 아님) |
| BatteryPackPos/BatteryPack/* (SM_BatteryPack, SM_Cover, SM_BatteryCover, SM_BatteryParts1/2, SM_Module) | 고전압 배터리 팩·모듈 | 시나리오 전용(배터리 리프트) |
| MSD_Lever, SM_ServicePlug | 서비스 플러그(MSD) | 시나리오 전용 |
| Battery12VService/* (단자·센서·클램프) · CoolantService/* (리저버·캡·드레인 플러그) | 12V·냉각수 서비스 부품 | 시나리오 전용 |
| FBX_MetalPlate_low | 하부 금속 플레이트(배터리 하부 보호판 추정) | ✕ |

생성 파츠(모델에 메시가 없어 상자·원통으로 만든 것): 앞 범퍼 빔, 냉각 모듈, A/C 컴프레서, 전륜 인버터, ICCU, 고전압 정션 블록, 스티어링 기어박스, 프런트·리어 스태빌라이저, 프런트 스트럿 좌·우, HVAC·블로워.

## 2. 비활성(시나리오 연출용) 노드 — 켜지지 않는 변형
`SM_BMU`, `SM_BMU02`, `SM_Coolantline`·`01`·`2`, `SM_FrontConnector.001`, `SM_RearConnector`(`_02`), `Cables/*`(HV 케이블, ICCU 큐브, BMU, GroundConnection, CoolantInlet/OutletHose, 각종 커넥터)는 평소 꺼져 있고 시나리오·엑스레이에서만 켜집니다. 자유 실습 탈거는 이 변형을 쓰지 않습니다(경로에 `/Cables/`가 있으면 제외).

## 3. 아직 한 덩어리인데 분리 가능한 메시 (조각 분석 결과)
| 메시 | 삼각형 | 떨어진 조각 | 분리 가능성 |
|---|---|---|---|
| SM_FrontSuspension | 20,027 | 179 | 높음 — 서브프레임·좌우 암·너클·스프링 단위로 나눌 수 있음 |
| ~~SM_RearSuspension~~ | 21,866 | 151 | **분리 완료**(§0) |
| SM_Ioniq_Cockpit | 19,188 | 113 | 보통 — 스티어링 휠·클러스터·벤트 등 |
| SM_Gearbox / SM_ElectricEngine | 10,228 / 5,753 | 77 / 41 | 보통 — 감속기·모터 외 부속 |
| SM_Frunk.001 | 5,847 | 76 | 보통 |
| ~~SM_Ioniq_Electric(차체 외판)~~ | 54,762 | 277 (그중 한 조각이 28,746삼각형) | **해결(11/17)** — 평면 절단(§0-1)과 모서리 각도 패치 분할(§0-2)로 해체. 남은 구조물은 `SM_BodyShell` |

## 4. 매뉴얼 구성부품 대비 — Ioniq5N 모델에 있는가
범례: ● 있음(분해 반영) / ◐ 있음(분해 미반영·시나리오 전용·덩어리) / ◯ 모델 없음(생성 파츠로 대체) / ✕ 없음

### 바디 (내장·외장·전장)
| 매뉴얼 부품 | 상태 | 비고 / 다른 모델 |
|---|---|---|
| 후드, 프런트·리어 범퍼, 프런트·리어 도어, 테일게이트 | ● | |
| 프런트 펜더 | ● | §0-1 평면 분리(11/17) |
| 아웃사이드 미러 | ● | §0-1, 앞 도어에서 분리. 스토크(받침)는 도어에 남음 |
| 사이드 실 몰딩, 리어 스포일러, 프런트 와이퍼 암 | ● | §0-1 |
| 루프 트림, 비전 루프 | ✕ | 차체 일체(분리 안 함) |
| 윈드쉴드·테일게이트 글라스 | ◐ | 노드만 분리(§0-1). 윈드쉴드는 분해 부위 없음 |
| 충전 도어 / 충전구 | ✕ | **다른 모델에 있음**: `FBX_ElectricCar_Out.fbx`의 `SM_Ioniq_ChargingPort`(352삼각형), `Dummy/아이오닉._02blend.fbx`의 `02-ChargePort` |
| 프런트 시트 | ● | 좌·우 분리 완료 |
| 리어 시트 | ● | 벤치 한 덩어리(쿠션·등받이 L/R 미분리) |
| 크래시 패드(메인 패드·벤트·가니쉬·센터 패널·글로브 박스·스티어링 컬럼 쉬라우드·카울 크로스바) | ◐ | `SM_Ioniq_Cockpit` 한 덩어리(조각 113개) |
| 플로어 콘솔 | ✕ | 이 모델엔 고정 콘솔 없음(바닥 평면) |
| 인포테인먼트(클러스터·통합 모니터·ccNC·안테나·앰프·마이크) | ◐ | Cockpit에 포함, 개별 분리 안 됨 |
| 보안·시동(스마트키 안테나, 버저, BDC, PDC, CCU, DCU, DVRS 카메라) | ✕ | 전부 없음 |
| 운전자 보조(ADAS 레이더·카메라) | ✕ | 없음 |
| 헤드램프 | ● | 램프 모델이 단순 |
| 테일 램프 | ✕ | `SM_Ioniq_Back.00x` 안에 포함 추정 |

### 고전압 배터리·제어
| 매뉴얼 부품 | 상태 | 비고 / 다른 모델 |
|---|---|---|
| 배터리 시스템 어셈블리(BSA)·팩 케이스·커버 | ◐ | 시나리오 전용(리프트) |
| 배터리 모듈 어셈블리(BMA) / 서브 배터리 팩(Sub-BPA) | ◐ | `SM_Module` 한 덩어리(44,672삼각형) |
| 메인 퓨즈 / CMU | ✕ | 없음 |
| BMU | ◐ | 비활성 변형(`SM_BMU`, `SM_BMU02`)만 |
| 파워 릴레이 어셈블리(PRA) | ◐ | `FBX_ElectricCar.fbx`에 `SM_PRA`(1,669삼각형)가 있으나 Ioniq5N엔 없음. 시나리오는 별도 아이템(Old/New_PRA_Unit) 사용 |
| 고전압 프런트·리어 커넥터 | ◐ | 비활성 변형 |
| **ICCU** | ◯ | Ioniq5N엔 14cm 큐브(`Cables/ICCU`). **다른 모델에 있음**: `FBX_ElectricCar_In.fbx`의 `SM_Iccu`(2,212삼각형) → 이것으로 교체 가능 |
| **고전압 정션 블록** | ◯ | 없음. `FBX_ElectricCar_In.fbx`의 `SM_connection`(1,156삼각형)이 후보(미확인) |
| 인버터 | ◯ | 모터에 일체. 원본 `Dummy/아이오닉._02blend.fbx`의 `23-Electronics`(123,911삼각형)가 전장 묶음 |
| 모터·감속기 | ● | |
| 콤보 충전 인렛 | ✕ | 없음(ChargingPort 모델은 위 참조) |

### 서스펜션·조향·구동
| 매뉴얼 부품 | 상태 | 비고 / 다른 모델 |
|---|---|---|
| 프런트 서브 프레임 / 텐션 암 / 레터럴 암 / 액슬 | ◐ | `SM_FrontSuspension` 한 덩어리 |
| 스티어링 기어박스 | ◯ | 없음. 원본 `29-SteeringColumn`(70,374삼각형)이 가장 가까움(컬럼 포함) |
| 프런트 스태빌라이저 바 | ◯ | 없음 |
| 프런트 스트럿(쇽·스프링) | ◯ | 없음(리어는 `SM_RearSuspension`에 코일 포함) |
| 프런트 드라이브 샤프트 | ◐ | `SM_FrontSuspension`/`SM_Gearbox`에 포함 추정 |
| 리어 크로스 멤버 / 로어 암 / 트레일링 암 / 쇽 업소버 / 리어 액슬 | ● | 분리 완료 |
| 리어 어퍼 암 프런트·리어 / 어시스트 암 | ◐ | 모델에서 따로 떨어진 큰 조각이 없음(가는 링크가 다른 조각에 합쳐짐) |
| 리어 스태빌라이저 바 | ◯ | 없음 |
| 기능통합형 드라이브 액슬(IDA) | ● | `SM_RearElectricengine` + `SM_RearPowerDistribution` |
| 프런트·리어 언더 커버 | ● | |
| 모터 마운팅(LH/RH/RR) | ◐ | 모터에 일체 |

### 차량 제어·냉각·기타
| 매뉴얼 부품 | 상태 | 비고 |
|---|---|---|
| 12V 배터리 | ● | 배터리 트레이 ✕, 배터리 센서·단자·클램프는 시나리오 파츠 |
| 차량 제어 유닛(VCU) / N 액티브 사운드 스피커 | ✕ | 없음 |
| 냉각수 리저버·압력 캡 | ◐ | 시나리오 서비스 파츠(CoolantService) |
| 콘덴서·라디에이터·냉각 팬 | ◯ | 없음(생성 파츠). 원본 `22-CoolingClimat`(94,934삼각형) 그룹이 있음 |
| 전동식 A/C 컴프레서 | ◯ | 없음(생성 파츠) |
| 냉매·냉각 라인 | ◐ | 비활성 `SM_Coolantline*` |
| HVAC·블로워 | ◯ | 없음(생성 파츠). `CoolingClimat`에 포함 추정 |
| 휠·타이어·브레이크 | ● | |

## 5. 프로젝트 내 다른 차량 모델 (참고용 소스)
| 파일 | 내용 | 쓸모 |
|---|---|---|
| `3D_ElectricCar/…/FBX_ElectricCar.fbx`, `…V3.fbx` | Ioniq5N의 기본 모델과 같은 이름·구성(53개). 구버전은 `SM_PRA`가 있음 | PRA 모델 |
| `3D/ElectricCar/…/FBX_ElectricCarSS.fbx` | 외판·도장용 변형 | 외판 |
| `03_3D/Object/O_FBX/FBX_ElectricCar_In.fbx` | 내부 구동계 25개: **`SM_Iccu`**, `SM_connection`, `Cube.084`(BMU 서브), 나머지는 같은 이름 | **ICCU·접속부 실모델** |
| `03_3D/Object/O_FBX/FBX_ElectricCar_Out.fbx` | 외장 15개: **`SM_Ioniq_ChargingPort`**, 더 상세한 범퍼(18,827삼각형)·앞 외판(6,002) | 충전구·고해상 범퍼 |
| `Dummy/아이오닉._02blend.fbx` (68MB) | 원본 고해상도 35개 그룹: `CoolingClimat`, `Electronics`, `SteeringColumn`, `PowerDistributionGearbox`, `Battery1~4`, `ChassisCover` 등 | 인버터·냉각·조향 실모델의 원본(현재 Read/Write 꺼짐 — 조각 추출하려면 켜야 함) |

## 6. 권장 다음 작업
1. `SM_Iccu`(In.fbx)를 프리팹에 넣고 ICCU 생성 파츠를 실모델로 교체(가장 확실한 대체).
2. `SM_FrontSuspension`/`SM_RearSuspension`을 조각 분석으로 서브프레임·좌우 암·쇽으로 분리(`CarMeshSplitter`에 함수 추가).
3. `SM_Ioniq_Cockpit`에서 스티어링 휠·클러스터를 분리.
4. ~~차체 외판 분리~~ → §0-1에서 처리. 남은 것: 휠 하우스 안 깨진 큰 삼각형 정리, 휠 가드 메시 추가.
5. 원본 `아이오닉._02blend.fbx`의 Read/Write를 켜고 `CoolingClimat`·`Electronics`·`SteeringColumn`에서 콘덴서·라디에이터·컴프레서·인버터·스티어링 기어박스 후보 조각을 추출.

## 7. 매뉴얼 구성부품 기준 부품 구성안 (전기차_자료 · 바디 "구성부품 및 부품위치" 18종 정독, 2026-11-17)
출처: `전기차_자료` 폴더의 "바디-* 구성 부품 및 부품 위치" PDF(표가 그림 속 글자라 이미지로 읽음) + 바디 체결 토크표 + 플로어 콘솔 어셈블리 탈거 및 장착.
범례: ● 별도 노드·분해 부위 / ◐ 다른 부품에 포함 / ✕ 이 모델에 메시 없음.

| 계통 | 매뉴얼 구성품 | 상태 | 모델 노드 · 비고 |
|---|---|---|---|
| 플로어 콘솔 | 어퍼 커버 · 리어 커버 · 컵 홀더 어셈블리 · 어셈블리 · 암레스트 | ● 한 벌 | `SM_FloorConsole` — 탈거 절차가 암레스트·어퍼 커버를 먼저 떼는 한 작업이라 한 어셈블리로 둠 |
| 내장 트림 | 프런트 필라 · 센터 필라 어퍼/로어 · 카울 사이드 · 도어 스커프 · 리어 필라 | ● 일부 | 프런트 필라(`SM_PillarTrimFrontL·R`), 도어 스커프(`SM_DoorScuffL·R`). 센터·리어 필라, 카울 사이드는 구조물과 한 장이라 `SM_BodyShell` |
| 루프 트림 | 선바이져 · 리테이너 · 어시스트 핸들 · 오버헤드 콘솔 · 루프 트림 어셈블리 | ◐ | 별도 메시 없음 → `SM_BodyShell`(루프 안쪽) |
| 비전 루프 | 모터 · 메커니즘 레일 · 롤러 블라인드 어셈블리(+ 글라스) | ● 한 벌 | `SM_VisionRoof`(글라스·블라인드 평면). 모터·레일은 메시 없음 |
| 크래쉬 패드 | 사이드 커버 L/R · 에어 벤트 L/CTR/R · 가니쉬 · 센터 그릴 · 컬럼 쉬라우드 로어/어퍼 · 메인 패드 · 카울 크로스바 · 글로브 박스 · 센터 패널 · 키보드 | ◐ | `SM_Ioniq_Cockpit` 한 덩어리(113조각, §3). 카울 크로스바는 생성 파츠 |
| 프런트/리어 시트 | 백 커버 · 쿠션 커버 · 쉴드 아웃터/인너 · 열선·통풍 유닛 · 블로워 · 열선 / 리어: 쿠션 · 백 커버 L/R · 열선 | ● | `SM_Seat_FL/FR/Rear` — 의자 단위. 내부 열선·블로워는 메시 없음 |
| 도어 | 아웃사이드·인사이드 핸들 · 윈도우 글라스 · 트림 · 레귤레이터 · 스위치 · 랫치 | ◐ | 도어 4종 단위. 미러는 `SM_MirrorL·R` 분리 |
| 후드 | 어셈블리 · 랫치 · 리프터 | ● | `Hood_Latch_Point`, `SM_Frunk*` |
| 테일게이트 | 오픈·인너·아웃터 스위치 · 버저 · 드라이브 유닛 · 유닛 · 랫치 · 가니쉬 | ● 외판 | `SM_Ioniq_Back` + `SM_Tailgate_Upper/Glass/Inner` + `SM_RearSpoiler` |
| 펜더 · 몰딩 | 펜더 어셈블리 · 인슐레이터 · 휠 가드 · 사이드 씰 몰딩 | ● 펜더·실 | `SM_FenderFL·FR`, `SM_SideSillL·R`. 인슐레이터·휠 가드는 별도 메시 없음(휠하우스는 `SM_BodyShell`) |
| 범퍼 | 프런트 범퍼 · 에너지 업소버 · 리어 범퍼 · 리어 범퍼 빔 | ● | `SM_Ioniq_Bumper`, `SM_Ioniq_Back.001~.005`, 앞 범퍼 빔(생성) |
| 미러 | 아웃사이드 · 인사이드 · 전동식 미러 스위치 | ● | `SM_MirrorL·R`, `SM_InsideMirror` |
| 익스테리어 그린 하우스 | 워셔 리저버 탱크·모터 · 레벨 센서 · 레인 센서 · 리어 와이퍼 모터 · 프런트 와이퍼 암&블레이드 · 카울 탑 커버 · 프런트 와이퍼 모터 · 스위치 | ● 일부 | `SM_WiperFront`, `SM_CowlTopCover`, `SM_Windshield`. 모터·센서·탱크는 메시 없음 |
| 충전 도어 | 충전 도어 · 액추에이터 | ✕ | 다른 모델(`FBX_ElectricCar_Out` 의 `SM_Ioniq_ChargingPort`)에만 있음 |
| 인포테인먼트 | 마이크 · 클러스터 · 무선/통합 안테나 · 멀티미디어 단자 · ccNC · 키보드 · 통합 모니터 · 외장 앰프 | ◐ / ✕ | 클러스터·모니터는 Cockpit 에 포함, 나머지 메시 없음 |

체결 토크(kgf·m, 바디 체결 토크표): 프런트 시트 5.0~6.5 · 2·3열 시트 4.5~6.0 · 도어 힌지(바디) 2.4~3.2 · 펜더 0.8~1.2 · 아웃사이드 미러 너트 0.7~1.1 · 와이퍼 암 2.3~2.7 · 리어 스포일러 0.5~0.8 · 비전 루프 고정 0.8~1.2 · 후드 힌지 1.9~2.6 · 플로어 콘솔(탈거 6단계) 0.4~0.6. 클립·스크류만 쓰는 트림류는 표에 값이 없어 일반 조임 토크 M6 를 썼다.

다음 후보: 크래쉬 패드(`SM_Ioniq_Cockpit`)를 가니쉬·에어 벤트·글로브 박스·센터 패널 단위로 나누기(같은 패치 분할 도구로 가능), 휠 하우스 인슐레이터·휠 가드 분리, 센터/리어 필라 트림 분리(패치 규칙 추가).

## 8. 매뉴얼 전체 대조로 추가한 부품 38개 (2026-11-17 3차, `CarPartService.Defs` 84 → 122)
**방법**: `전기차_자료` 480종 중 구성부품·부품위치 표 약 45종(냉각 · 배터리 제어 · 서스펜션 · 드라이브 샤프트 · 브레이크 · 스티어링 · 공조)과 체결 토크표 6종(서스펜션 · 스티어링 · 브레이크 · 드라이브 샤프트 · 공조 · 바디)을 이미지로 읽고, 냉각수 관련 탈거 문서는 텍스트의 토크 값과 절차 그림을 확인했다. 표의 부품을 현재 `Defs`·프리팹과 하나씩 대조해 **정비 실습 대상이면서 빠진 것**만 골랐다. 메시가 없는 부품은 기존 방식대로 생성 파츠(상자·원통, 좌표 = 차 루트)로 만들었다.

| 계통 | 추가 부위(id) | 토크(kgf·m) · 근거 | 선행(requires/parent) |
|---|---|---|---|
| 냉각 | `ThreeWayValveMotor`·`ThreeWayValveBattery` (3웨이 밸브 2) | 0.9~1.4 · 3웨이 밸브 탈거 및 장착 | 프렁크·프런트 언더 커버 |
| | `PtcHeatPump` (PTC 히트 펌프, 고전압) | 0.8~1.2(접지 케이블 1.1~1.4) | 프렁크·언더 커버 |
| | `CoolantManifold` (냉각수 분배 파이프) | 2.0~2.4 | 언더 커버 |
| | `EwpMotor`·`EwpBattery1`·`EwpBattery2` (전자식 워터 펌프 3) | 1.8~2.2(클램프 0.20~0.25) | 프렁크(·언더 커버) |
| | `MotorOilCoolerFront`·`MotorOilCoolerRear` (모터 감속기 오일 쿨러 2) | 0.7~1.0 | 부모 = 전륜 모터 / 후륜 모터 |
| | `ActiveAirFlap` (액티브 에어 플랩) | 0.8~1.0 | 프런트 범퍼 |
| | `WaterCondenser`·`RefrigerantLines` (수냉 콘덴서·냉매 라인) | 0.9~1.4 · 0.8~1.2 (공조 체결토크) | 프렁크 / 컴프레서 |
| 고전압 충전 | `ComboChargeInlet` (콤보 충전 인렛, 고전압) | 0.5~0.8 (서비스 커버·커넥터 0.95~1.05) | 뒤 범퍼·리어 언더 커버 |
| 서스펜션·구동 | `FrontKnuckleL/R` (너클·허브) | 18~20 | 부모 = 전륜 서브 프레임 |
| | `FrontDriveShaftL/R` (프런트 IDA 샤프트) | 13~15 | 부모 = 전륜 서브 프레임 |
| | `TensionArmL/R` (텐션 암) | 16~18 | 부모 = 전륜 서브 프레임 |
| | `RearUpperArmFrontL/R`·`RearUpperArmRearL/R`·`RearAssistArmL/R` | 16~18 · 16~18 · 12~14 | 부모 = 리어 크로스 멤버 |
| | `EcsUnit` (전자 제어 서스펜션 컨트롤 유닛) | 0.45~0.74 | 뒤 범퍼 |
| 브레이크 | `Ieb` (통합형 전동 부스터) · `RemoteReservoir` · `BrakePedal` | 1.7~2.6 · 0.7~1.1 · 1.7~2.6 | 프렁크 / 프렁크 / 크래시 패드 |
| | `FrontWheelSpeedL/R`·`RearWheelSpeedL/R` (휠 속도 센서 4) | 0.8~1.2 · 0.9~1.4 | 부모 = 전륜 서브 프레임 / 리어 액슬 |
| | `BrakeLines` (차 모델의 `SM_BrakeLines` 노드 사용) | 0.9~1.4 | 4륜 브레이크를 먼저 뗌 |
| | `EpbActuatorL/R` (EPB 액추에이터) | 0.8~1.1 | 부모 = 리어 브레이크 |
| 스티어링 | `SteeringColumn` (컬럼 어셈블리) · `UniversalJoint` (유니버설 조인트) | 5.5~6.0 · 5.0~6.0 | 크래시 패드 / 컬럼 |

- 호버 이름도 38개를 `HoverNameFormatter` 에 추가했다. `requires`·`parent` 참조와 id 중복을 스크립트로 검사했다(오류 0). 새 생성 파츠 58조각을 차 렌더에 얹어 위치를 눈으로 확인했다(앞 모터 룸·뒤 서스펜션 — 일부는 기존 메시와 겹치지만 프렁크·언더 커버 밑이라 가려진다).
- **일부러 뺀 것(이유)**: 배터리 모듈(BMA)·서브 배터리 팩·CMU·전압·온도 센싱 와이어링·고전압 커넥터 — 배터리 팩 내부라 시나리오 전용 `BatteryPack` 프리팹이 다룬다. 쿨링팬 모터·슈라우드(`CoolingFan` 에 포함), 라디에이터·리저버·냉각수 온도 센서(`Cooling`·`CoolantService`). 에어컨 온도 센서 #1/#2·이베퍼레이터 센서·실내 콘덴서(HVAC 유닛 안쪽). TPMS 센서·BDC·휠·엑센트 캡(`TireService`). 스티어링 휠 구성(DAB·스위치·패들 시프터·히티드 — 크래시 패드 메시에 포함). 캘리퍼 보디·라이너·패드·디스크(브레이크 코너 단위). ESC OFF·스톱 램프 스위치(페달 부위에 포함). 충전 도어·액추에이터(모델 없음 — `FBX_ElectricCar_Out` 에만), 와이퍼 모터·워셔 탱크·레인 센서 등 소형 전장·인포테인먼트 모듈(이 모델에 위치 근거 없음).
- **한계**: 위치·크기는 모델 메시와 매뉴얼 사진을 보고 정한 추정치다(실차 치수 아님). 플레이·네트워크 동작(시작 시 `FindCar`, 서버·클라이언트 부위 번호 일치)은 아직 확인하지 못했다 — 서버 빌드를 새 코드로 다시 빌드해야 클라이언트와 부위 목록이 맞는다.

## 9. 애매한 조각 정리 — Back.001~006 병합 · 차체 셸 세분화 · 충전 도어 (2026-11-17 4차)
도구: `Editor/CarPartMerger.cs`(메뉴 `Dodrill > 차량 메시 분리 > 조각 병합 + 충전 도어`). **순서: `CarBodyPanelSplitter.ApplyAll()` → `CarPartMerger.MergeAll()`** — 병합 뒤에는 옛 노드가 없으므로 다시 돌릴 땐 프리팹을 병합 전 상태(백업 `D:/claude_tmp/body/Ioniq5N.prefab.bak_before_merge`)로 되돌린 뒤 같은 순서로 한다.

| 옛 노드 | 정체(색 구분 렌더로 확인) | 지금 |
|---|---|---|
| `SM_Ioniq_Back` | 테일게이트 외판(램프 띠 포함) | → `SM_Tailgate` |
| `SM_Ioniq_Back.002` | 램프 밑 테일게이트 하부 패널 | → `SM_Tailgate` (원래 어느 부위에도 안 들어 있었음) |
| `SM_Tailgate_Upper/Glass/Inner` | 테일게이트 위·유리·안쪽 | → `SM_Tailgate` (한 메시 5 재질, 22,524삼각형) |
| `SM_Ioniq_Back.001/.003/.004/.005` | 뒤 범퍼 위/아래/가는 띠 | → `SM_RearBumper` (3,564삼각형) |
| `SM_Ioniq_Back.006` | 뒤 모서리 쿼터 패널(좌우) | → `SM_RearQuarterL`·`SM_RearQuarterR` (차체) |
| `SM_BodyShell` | 차체 구조 한 덩어리 | → 아래 11개 구조 패널(남는 것 없음) |

- 병합 노드는 기준 노드(Back / Back.001)의 변환을 그대로 써서 `CarPartService` 의 테일게이트·뒤 범퍼 볼트 좌표가 유지된다. 두 부위의 `objects` 를 `SM_Tailgate`, `SM_RearBumper` 로 바꿨다.
- **차체 구조 패널(탈거 대상 아님)**: `SM_RoofPanel`(루프 + 리어 해치 프레임) · `SM_BodySideL/R`(A·B·C 필라·루프 레일·사이드 실) · `SM_FloorPan` · `SM_Firewall` · `SM_FrontApron` · `SM_WheelHouseFL/FR/RL/RR` · `SM_RearBody`(화물 바닥·뒷선반). 모든 패치를 위치로 이 중 하나에 배정하는 전함수 `StructName` 이라 `SM_BodyShell`/`SM_Ioniq_Electric` 이름은 프리팹에 남지 않는다(가장 큰 패널이 원본 노드를 이어받음).
- **충전 도어**: 다른 차량 FBX `FBX_ElectricCar_Out` 의 `SM_Ioniq_ChargingPort`(352삼각형)를 가져와 `SM_ChargingPort` 로 붙였다(메시는 FBX 것을 참조, 재질은 차체 도장 `M_Electric.002`). 좌표 변환은 Cockpit·Bumper·Door 로 대조한 `p = (−z − 0.27, y + 0.16, x)`, 회전 Y 270°. 왼쪽 뒤 쿼터에 맞게 붙은 것을 렌더로 확인. 분해 부위 `ChargingDoor`(스크류 3, M6) 추가 → 부위 123개.
- 같은 방식으로 가져올 수 있는 후보(이번엔 안 가져옴): `아이오닉._02blend.fbx` 의 `RearLights`(13k)·`FrontLights`(10k)·`Tunnel`(5.8k)·`SteeringColumn`(70k, 너무 무거움). 이 FBX 는 프리팹 좌표계와 같다(앞 −X). 뒤 램프는 지금 `SM_Tailgate` 의 램프 띠와 겹칠 수 있어 보류.
- 확인 못 한 것: 플레이·네트워크(시작 시 `FindCar`), 충전 도어 볼트 위치(메시를 못 읽어 도어 크기 기준으로 잡은 추정치).
