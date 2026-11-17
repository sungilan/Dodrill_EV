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
| SM_Ioniq_Electric | **차체 외판 일체**(펜더·루프·유리·미러·사이드 몰딩·충전구 자리) | ✕ (분리 불가) |
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
| **SM_Ioniq_Electric(차체 외판)** | 54,762 | 277 (그중 한 조각이 28,746삼각형) | **낮음** — 펜더·루프·유리·미러가 한 조각으로 용접돼 있어 연결 기준으로는 못 나눔. 모델러가 메시를 나눠야 함 |

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
