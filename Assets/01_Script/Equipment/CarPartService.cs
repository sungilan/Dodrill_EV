using System.Collections;
using System.Collections.Generic;
using EPOOutline;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  CarPartService.cs
//
//  정비소 차량의 차체·구동 부품을 실제로 떼고 다시 다는 자유 실습 (TireService 의 바퀴 탈거·장착을 부위별로 넓힌 것).
//  사고 차량 수리 영상(앞 범퍼 → 헤드램프 → … → 모터, 역순 장착)과 정비 매뉴얼의 체결 토크를 따른다.
//
//  · 부위마다 장착 볼트(Bolt + FreeWorkBolt)를 차 모델 위 실제 자리(메시에 레이캐스트로 잰 점)에 만든다
//    → 스패너·에어 임팩렌치(부위별 소켓)로 풀고 토크렌치로 규정값 확인. 토크렌치는 부위별 규정값(FreeWorkBolt.torqueMin/Max)으로 검사
//  · 볼트를 다 풀면 그 부위가 '떼어낼 수 있는 부품'(Equip_CarPart / CarPartItem, 네트워크 장비)으로 바뀐다 → 잡아서 빼낸다
//    떼어낸 부품은 차 모델의 메시를 그대로 복사해 만든다(부위마다 프리팹을 따로 두지 않는다)
//  · 부품을 원래 자리 가까이 가져가면 반투명 고스트, 놓으면 '착' 끼워지고 → 볼트가 덜 조인 상태로 나온다 → 다 조이면 끝
//  · 작업 순서: requires 에 적힌 부위가 먼저 떨어져 있어야 볼트가 나온다(예: 헤드램프는 앞 범퍼를 뗀 뒤).
//    장착은 그 반대 — 앞 범퍼는 헤드램프를 먼저 달아야 끼울 수 있다
//  · 진행도·상태는 서버가 들고(SyncList) 모든 플레이어에게 같은 모습으로 보인다
//  씬에 하나 — EquipmentSlot 이 Equip_CarPartService 프리팹을 Spawn 한다. 차는 런타임에 생기므로 1초마다 찾는다.
// ============================================================

public class CarPartService : NetworkBehaviour
{
    public static CarPartService Instance { get; private set; }

    public enum PartState { Tight = 0, Off = 2, Mounted = 3 }

    // ── 부위 정의 ──
    // 볼트 자리는 Ioniq5N 프리팹(FBX_ElectricCar)에서 부품 메시에 레이캐스트로 잰 값 — 기준 오브젝트(objects[0]) 로컬 좌표.
    // n = 공구가 들어오는 쪽(볼트 머리 방향).
    public struct F
    {
        public Vector3 p, n;
        public F(float x, float y, float z, float nx, float ny, float nz) { p = new Vector3(x, y, z); n = new Vector3(nx, ny, nz); }
    }

    public class PartDef
    {
        public string id, name, tool, hint;
        public string[] objects;
        public F[] bolts;
        public string[] requires = new string[0];
        public float tMin, tMax, boltScale = 1f;
        public int socket = 1;   // 에어 임팩렌치 소켓(1=10mm, 2=17mm) — 소켓 거치대에 있는 크기 중 볼트 머리에 맞는 것
        public string parent;        // 어셈블리 계층: 이 부위가 속한 큰 부품(예: 서브 프레임 → 기어박스). 큰 부품을 차에서 먼저 떼어내야 이 부위를 풀 수 있고, 큰 부품을 다시 달려면 이 부위가 먼저 달려 있어야 한다
        public int needsWheel2 = -1; // 두 번째 필수 휠(좌우에 걸친 부위: 스태빌라이저 바). -1 = 없음
        public int needsWheel = -1; // 이 휠(0=앞왼쪽 1=앞오른쪽 2=뒤왼쪽 3=뒤오른쪽)을 먼저 떼야 접근되는 부위(브레이크) — TireService 상태를 본다. -1 = 없음
        public bool hv;          // 고전압 부품(모터·컴프레서) — 매뉴얼: 고전압 차단 절차(보호구 → 12V(−) → 서비스 플러그 → 3분 방전 → 단자 전압 확인) 뒤에만 작업
        public string specPart, specSource;
        public G[] gen;          // 차 모델에 메시가 없는 부품 — 상자·원통으로 만든다(좌표는 차 루트 기준). objects 대신 쓴다
    }

    // 생성 파츠 조각: 차 루트 좌표의 중심·크기·회전·색
    public struct G
    {
        public bool cyl;
        public Vector3 c, s, e;
        public Color col;
        public float metal;
        public static G Box(float x, float y, float z, float sx, float sy, float sz, Color col, float metal = 0.6f, float ex = 0f, float ey = 0f, float ez = 0f)
            => new G { cyl = false, c = new Vector3(x, y, z), s = new Vector3(sx, sy, sz), e = new Vector3(ex, ey, ez), col = col, metal = metal };
        // 원통: 축 = 로컬 Y. 지름 d, 길이 len. 회전 e 로 축을 돌린다
        public static G Cyl(float x, float y, float z, float d, float len, Color col, float metal = 0.6f, float ex = 0f, float ey = 0f, float ez = 0f)
            => new G { cyl = true, c = new Vector3(x, y, z), s = new Vector3(d, len * 0.5f, d), e = new Vector3(ex, ey, ez), col = col, metal = metal };
    }

    private static readonly Color Alu = new Color(0.72f, 0.74f, 0.76f);
    private static readonly Color DarkAlu = new Color(0.42f, 0.44f, 0.46f);
    private static readonly Color Fin = new Color(0.20f, 0.21f, 0.22f);
    private static readonly Color Plastic = new Color(0.09f, 0.09f, 0.10f);
    private static readonly Color HvOrange = new Color(1f, 0.45f, 0.05f);

    // 생성 파츠의 기준점 — SM_Gearbox 의 차 루트 좌표(회전 0·크기 1). 생성 홀더 = 기어박스에서 이만큼 뺀 자리 = 차 루트와 같은 좌표계
    private const string GenRefName = "SM_Gearbox";
    private static readonly Vector3 GenRef = new Vector3(-1.897f, 0.496f, 0.004f);

    private const string SrcM6 = "일반 조임 토크 — 플랜지 볼트 M6·8T (부위별 값 미기재)";
    private const string SrcM8 = "일반 조임 토크 — 플랜지 볼트 M8·8T (부위별 값 미기재)";
    private const string SrcM10 = "일반 조임 토크 — 플랜지 볼트 M10·8T (부위별 값 미기재)";
    private const string SrcM12 = "일반 조임 토크 — 플랜지 볼트 M12·8T (부위별 값 미기재)";

    public static readonly PartDef[] Defs =
    {
        new PartDef {
            id = "FrontBumper", name = "앞 범퍼",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 펜더 가니쉬를 뗀 뒤 상단 볼트(0.4~0.7) → 하단 클립 → 사이드 스크류·클립 순으로 풀고, 범퍼 메인 커넥터를 분리해 앞으로 당겨 뺀다 (측면 걸림턱 파손 주의, 손상된 클립은 교환)",
            objects = new[] { "SM_Ioniq_Bumper" },
            bolts = new[] {
                new F(-0.4546f, -0.1471f, -0.2121f, 0f, -1f, 0f), new F(0.4452f, -0.148f, -0.2121f, 0f, -1f, 0f),
                new F(-0.9447f, 0.0055f, 0.1378f, -1f, 0f, 0f),   new F(0.9447f, 0.0055f, 0.1378f, 1f, 0f, 0f),
                new F(-0.4046f, 0.21f, -0.2621f, 0f, 1f, 0f),     new F(0.3952f, 0.2147f, -0.2621f, 0f, 1f, 0f) },
            tMin = 0.4f, tMax = 0.7f, specPart = "앞 범퍼 상단 볼트", specSource = "바디 — 프런트 범퍼 어셈블리 탈거 및 장착(상단 볼트 0.4~0.7)" },
        new PartDef {
            id = "Headlamp", name = "헤드램프",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 앞 범퍼를 뗀 뒤, 헤드 램프 메인 커넥터와 와이어링 클립을 분리하고 볼트·스크류를 풀어 빼낸다 (헤드 램프 제어기 ILCU는 램프를 뗀 뒤 스크류로 분리)",
            objects = new[] { "SM_Ioniq_glass", "SM_Ioniq_Bumper_Lights" },
            bolts = new[] {
                new F(-0.7845f, -0.2858f, 0.0378f, 0f, -1f, 0f), new F(0.7751f, -0.2837f, 0.0178f, 0f, -1f, 0f),
                new F(-0.8045f, -0.2045f, 0.0453f, 0f, 0f, -1f), new F(0.7751f, -0.2045f, -0.0119f, 0f, 0f, -1f) },
            requires = new[] { "FrontBumper" },
            tMin = 1.0f, tMax = 1.4f, specPart = "헤드램프 장착 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "Hood", name = "후드",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "후드를 열고 후드 리프터(양 끝 소켓 클립을 살짝 들어 올려 분리)를 뗀 뒤 힌지 볼트(1.9~2.6)를 풀어 후드를 탈거한다 (2인 1조로 받쳐 든다)",
            objects = new[] { "Hood_Latch_Point" },
            bolts = new[] {
                new F(-0.6247f, -0.0767f, 0.3317f, 0f, 1f, 0f), new F(-0.6247f, -0.0727f, 0.2717f, 0f, 1f, 0f),
                new F(0.6153f, -0.0767f, 0.3317f, 0f, 1f, 0f),  new F(0.6153f, -0.0727f, 0.2717f, 0f, 1f, 0f) },
            tMin = 1.9f, tMax = 2.6f, specPart = "후드 힌지 볼트", specSource = "바디 — 후드 어셈블리 탈거(힌지 볼트 1.9~2.6)" },
        new PartDef {
            id = "Frunk", name = "프렁크(앞 트렁크)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "후드를 열고 프렁크 테두리 볼트 4개를 풀어 들어낸다 — 아래 모터·감속기 작업 공간이 나온다",
            objects = new[] { "SM_Frunk.001", "SM_Frunk_lid" },
            bolts = new[] {
                new F(-0.6047f, -0.1328f, -0.3316f, 0f, -1f, 0f), new F(0.5953f, -0.1328f, -0.3316f, 0f, -1f, 0f),
                new F(-0.6047f, -0.1715f, 0.5684f, 0f, -1f, 0f),  new F(0.5953f, -0.1706f, 0.5684f, 0f, -1f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "프렁크 장착 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "FrontMotor", name = "전륜 모터·감속기",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2, hv = true,
            hint = "고전압 차단 → 냉각수 배출(리저버 캡 열고 드레인 플러그) → 프렁크·프런트 언더 커버를 뗀 뒤 특수공구(09200-AL100)·크레인 잭으로 받치고 LH·RH·RR 마운팅 볼트를 푼다",
            objects = new[] { "SM_Gearbox", "SM_ElectricEngine" },
            bolts = new[] {
                new F(-0.0035f, 0.0113f, -0.2045f, 0f, 1f, 0f), new F(-0.0035f, 0.1323f, 0.1955f, 0f, 1f, 0f),
                new F(0.0703f, 0.0037f, -0.0045f, 1f, 0f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover", "FrontInverter" },
            tMin = 11f, tMax = 13f, boltScale = 1.6f, specPart = "모터 마운팅 볼트", specSource = "전륜 모터 및 감속기 마운팅 탈거 및 장착" },
        // ── 차 모델에 메시가 없어 상자·원통으로 만든 부품 (사고차 수리 영상: 범퍼 → 백빔 → 냉각 모듈, 프렁크 아래 전동 컴프레서) ──
        new PartDef {
            id = "FrontBeam", name = "앞 범퍼 빔(백빔)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "앞 범퍼를 뗀 뒤 좌우 크래시박스 플랜지 볼트(2개씩)를 풀어 백빔을 앞으로 뺀다",
            gen = new[] {
                G.Box(-2.535f, 0.46f, 0f, 0.03f, 0.10f, 1.32f, Alu, 0.8f),
                G.Box(-2.46f, 0.46f, -0.60f, 0.12f, 0.08f, 0.08f, Alu, 0.8f), G.Box(-2.46f, 0.46f, 0.60f, 0.12f, 0.08f, 0.08f, Alu, 0.8f),
                G.Box(-2.394f, 0.46f, -0.60f, 0.012f, 0.20f, 0.14f, DarkAlu), G.Box(-2.394f, 0.46f, 0.60f, 0.012f, 0.20f, 0.14f, DarkAlu) },
            bolts = new[] {
                new F(-2.400f, 0.54f, -0.60f, -1f, 0f, 0f), new F(-2.400f, 0.38f, -0.60f, -1f, 0f, 0f),
                new F(-2.400f, 0.54f, 0.60f, -1f, 0f, 0f),  new F(-2.400f, 0.38f, 0.60f, -1f, 0f, 0f) },
            requires = new[] { "FrontBumper" },
            tMin = 4.9f, tMax = 6.7f, boltScale = 1.3f, specPart = "백빔 장착 볼트(M10)", specSource = "일반 조임 토크 — 플랜지 볼트 M10·8T (부위별 값 미기재)" },
        new PartDef {
            id = "Cooling", name = "냉각 모듈(콘덴서·라디에이터·팬)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → N 액티브 사운드 스피커·프런트 언더 커버를 뗀 뒤 리저버 캡을 열고 모터·배터리 냉각수 드레인 플러그를 열어 배출 → 쿨링팬 커넥터·호스를 분리하고 에어컨 파이프 마운팅 볼트(0.5~0.8)로 콘덴서를 라디에이터에서 떼어(냉매 라인은 분리하지 않는다) 상·하부 에어가드와 브래킷 볼트(0.8~1.2)를 풀어 뺀다. 쿨링 모듈은 콘덴서·쿨링팬을 단 채 어셈블리째 내려온다. 바닥에서 쿨링팬(0.5~0.8)과 콘덴서(0.5~0.8)를 분해한다",
            gen = new[] {
                G.Box(-2.435f, 0.53f, 0f, 0.03f, 0.38f, 1.0f, DarkAlu, 0.5f),                              // 라디에이터 코어
                G.Box(-2.435f, 0.53f, -0.515f, 0.05f, 0.40f, 0.03f, Plastic, 0f), G.Box(-2.435f, 0.53f, 0.515f, 0.05f, 0.40f, 0.03f, Plastic, 0f),   // 좌우 탱크
                G.Box(-2.395f, 0.53f, 0f, 0.02f, 0.38f, 0.96f, Plastic, 0f),                               // 팬 슈라우드
            },
            bolts = new[] {
                new F(-2.435f, 0.73f, -0.515f, 0f, 1f, 0f), new F(-2.435f, 0.73f, 0.515f, 0f, 1f, 0f),
                new F(-2.460f, 0.40f, -0.515f, -1f, 0f, 0f), new F(-2.460f, 0.40f, 0.515f, -1f, 0f, 0f) },
            requires = new[] { "FrontBeam", "FrontUnderCover" },
            tMin = 0.8f, tMax = 1.2f, specPart = "냉각 모듈 에어가드·브래킷 볼트", specSource = "냉각시스템 — 라디에이터 탈거 및 장착(에어가드·상부 브래킷 0.8~1.2)" },
        new PartDef {
            id = "AcCompressor", name = "전동식 A/C 컴프레서",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)", hv = true,
            hint = "고전압 차단 → 냉매 회수기로 냉매 회수 → N 액티브 사운드 스피커·배터리 트레이(12V)를 뗀 뒤 석션·디스차지 라인 볼트(1.7~2.6)와 컴프레서·고전압 커넥터를 분리하고 장착 볼트(2.0~2.4)를 푼다 — 매뉴얼은 냉각 모듈 탈거가 필수가 아니지만 이 모델은 컴프레서가 냉각 모듈 뒤에 있어 모듈을 먼저 뗀다. 라인은 분리 즉시 캡을 씌운다",
            gen = new[] {
                G.Cyl(-2.26f, 0.41f, 0.45f, 0.13f, 0.22f, Alu, 0.7f, 90f),                                 // 본체(축 = 좌우)
                G.Cyl(-2.26f, 0.49f, 0.50f, 0.035f, 0.04f, Alu, 0.8f), G.Cyl(-2.26f, 0.49f, 0.41f, 0.03f, 0.04f, Alu, 0.8f),   // 석션·디스차지 포트
                G.Box(-2.332f, 0.44f, 0.37f, 0.012f, 0.05f, 0.04f, DarkAlu), G.Box(-2.332f, 0.44f, 0.53f, 0.012f, 0.05f, 0.04f, DarkAlu),
                G.Box(-2.332f, 0.36f, 0.45f, 0.012f, 0.05f, 0.04f, DarkAlu) },                              // 앞쪽 마운팅 브래킷
            bolts = new[] {
                new F(-2.338f, 0.44f, 0.37f, -1f, 0f, 0f), new F(-2.338f, 0.44f, 0.53f, -1f, 0f, 0f), new F(-2.338f, 0.36f, 0.45f, -1f, 0f, 0f) },
            requires = new[] { "Cooling" },
            tMin = 2.0f, tMax = 2.4f, specPart = "컴프레서 장착 볼트", specSource = "히터 및 에어컨 장치 — 전동식 에어컨 컴프레서 탈거(장착 볼트 2.0~2.4)" },
        new PartDef {
            id = "DoorFL", name = "앞 도어(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 와이어링 커넥터와 도어 체커(도어 측 0.7~1.1)를 분리한 뒤 바디 측 힌지 볼트(2.4~3.2)를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorFrontLeft" },
            bolts = new[] { new F(0.0613f, -0.15f, -0.546f, 1f, 0f, 0f), new F(0.0265f, 0.28f, -0.546f, 1f, 0f, 0f), new F(0.1111f, 0.06f, -0.496f, 1f, 0f, 0f) },
            tMin = 2.4f, tMax = 3.2f, specPart = "도어 힌지 볼트(바디 측)", specSource = "바디 — 체결 토크(프런트/리어 도어, 바디에 도어 힌지 부착)" },
        new PartDef {
            id = "DoorFR", name = "앞 도어(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 와이어링 커넥터와 도어 체커(도어 측 0.7~1.1)를 분리한 뒤 바디 측 힌지 볼트(2.4~3.2)를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorFrontRight" },
            bolts = new[] { new F(0.6662f, 0.8697f, -0.1497f, -1f, 0f, 0f), new F(0.6784f, 0.8697f, 0.2803f, -1f, 0f, 0f), new F(0.6498f, 0.8197f, 0.0603f, -1f, 0f, 0f) },
            tMin = 2.4f, tMax = 3.2f, specPart = "도어 힌지 볼트(바디 측)", specSource = "바디 — 체결 토크(프런트/리어 도어, 바디에 도어 힌지 부착)" },
        new PartDef {
            id = "DoorRL", name = "뒤 도어(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 와이어링 커넥터와 도어 체커(도어 측 0.7~1.1)를 분리한 뒤 바디 측 힌지 볼트(2.4~3.2)를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorRearLeft" },
            bolts = new[] { new F(-0.0936f, -0.1438f, -0.5831f, 1f, 0f, 0f), new F(-0.1152f, 0.2861f, -0.5831f, 1f, 0f, 0f), new F(-0.0371f, 0.0661f, -0.5331f, 1f, 0f, 0f) },
            tMin = 2.4f, tMax = 3.2f, specPart = "도어 힌지 볼트(바디 측)", specSource = "바디 — 체결 토크(프런트/리어 도어, 바디에 도어 힌지 부착)" },
        new PartDef {
            id = "DoorRR", name = "뒤 도어(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 와이어링 커넥터와 도어 체커(도어 측 0.7~1.1)를 분리한 뒤 바디 측 힌지 볼트(2.4~3.2)를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorRearRight" },
            bolts = new[] { new F(0.0936f, 0.5677f, -0.1437f, -1f, 0f, 0f), new F(0.1152f, 0.5677f, 0.2863f, -1f, 0f, 0f), new F(-0.0196f, 0.5177f, 0.0663f, -1f, 0f, 0f) },
            tMin = 2.4f, tMax = 3.2f, specPart = "도어 힌지 볼트(바디 측)", specSource = "바디 — 체결 토크(프런트/리어 도어, 바디에 도어 힌지 부착)" },
        new PartDef {
            id = "Tailgate", name = "테일게이트",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 테일게이트 로어 트림을 뗀 뒤 와이어링 하네스 커넥터와 파워 테일게이트 드라이브 유닛을 분리하고 장착 볼트(2.2~2.7)를 푼다 (2인 이상 작업)",
            // 11/17: 위쪽 외판·뒷유리·안쪽 패널이 차체 메시(SM_Ioniq_Electric)에 붙어 있어 테일게이트를 떼도 차에 남았음 → CarBodyPanelSplitter 로 떼어 낸 노드를 같이 묶는다
            objects = new[] { "SM_Tailgate" },   // 11/17: SM_Ioniq_Back + .002 + Upper/Glass/Inner 를 한 메시로 합침(CarPartMerger, 노드 변환은 옛 SM_Ioniq_Back 그대로 — 볼트 좌표 유지)
            bolts = new[] {
                new F(-0.5047f, -0.6045f, 1.3825f, 0f, -1f, 0f), new F(0.4953f, -0.6061f, 1.3825f, 0f, -1f, 0f),
                new F(-0.5047f, -0.604f, 1.4625f, 0f, -1f, 0f),  new F(0.4953f, -0.5877f, 1.4625f, 0f, -1f, 0f) },
            tMin = 2.2f, tMax = 2.7f, specPart = "테일게이트 장착 볼트", specSource = "바디 — 테일게이트 어셈블리 탈거(장착 볼트 2.2~2.7)" },
        new PartDef {
            id = "RearBumper", name = "뒤 범퍼",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 쿼터 가니쉬·리어 콤비네이션 램프를 뗀 뒤 상단 클립·스크류, 리어 센터 언더 커버(클립), 하단 클립을 풀고 커넥터를 분리한다 — 하단 볼트(1.6~2.4)를 풀고 올리면서 뒤로 뺀다",
            objects = new[] { "SM_RearBumper" },   // 11/17: SM_Ioniq_Back.001·.003~.005 합침(변환은 옛 .001 그대로)
            bolts = new[] {
                new F(-0.5047f, 0.2669f, 2.2125f, 0f, -1f, 0f), new F(0.4953f, 0.2637f, 2.2125f, 0f, -1f, 0f),
                new F(-0.9242f, 0.4315f, 2.0125f, -1f, 0f, 0f), new F(0.9242f, 0.4315f, 2.0125f, 1f, 0f, 0f),
                new F(-0.4547f, 0.5976f, 2.1625f, 0f, 1f, 0f),  new F(0.4453f, 0.5985f, 2.1625f, 0f, 1f, 0f) },
            tMin = 1.6f, tMax = 2.4f, specPart = "뒤 범퍼 하단 볼트", specSource = "바디 — 리어 범퍼 어셈블리 탈거 및 장착(하단 볼트 1.6~2.4)" },

        // ══════════════════════════════════════════════════════════════
        //  11/16 추가 — 같은 방식으로 하부(언더 커버·서스펜션)·후륜 모터·실내(시트·대시보드·HVAC)까지 넓힘.
        //  근거: 정비 매뉴얼(고전압 차단 절차 8단계의 프런트·리어 언더 커버 탈거, 모터 마운팅, 서스펜션 구성부품, 크래쉬 패드·시트 구성부품)과
        //  Car&Man 사고 수리·블로워/컴프레서 영상(범퍼 → 하부 → 모터 → 실내 HVAC 순). 볼트 자리는 Ioniq5N 메시에 광선을 쏴 잰 값.
        //  차 모델에 이 이름의 메시가 없으면 FindCar 가 통째로 실패하므로 이름을 바꾸지 말 것.
        // ══════════════════════════════════════════════════════════════
        new PartDef {
            id = "FrontUnderCover", name = "프런트 언더 커버",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "리프트로 차를 올린 뒤(배터리 케이스는 리프트 포인트 금지) 차 밑에서 볼트를 푼다 — 고전압 차단 절차 8단계(인버터 단자 전압 확인) 전에 프런트·리어 언더 커버를 모두 뗀다",
            objects = new[] { "SM_Bottom_01" },
            bolts = new[] {
                new F(-0.6896f, -4.2370f, 0.0152f, 0f, 0f, -1f), new F(-0.2396f, -4.2370f, 0.0151f, 0f, 0f, -1f),
                new F(0.2604f, -4.2370f, 0.0152f, 0f, 0f, -1f),  new F(0.7104f, -4.1870f, 0.0143f, 0f, 0f, -1f),
                new F(-0.6896f, -3.0370f, 0.0257f, 0f, 0f, -1f), new F(-0.2396f, -3.1370f, 0.0191f, 0f, 0f, -1f),
                new F(0.2604f, -3.1370f, 0.0191f, 0f, 0f, -1f),  new F(0.7104f, -3.0370f, 0.0257f, 0f, 0f, -1f) },
            tMin = 0.8f, tMax = 1.2f, specPart = "프런트 언더 커버 볼트", specSource = "전륜 모터 및 감속기 시스템 — 프런트 언더 커버 탈거 및 장착" },
        new PartDef {
            id = "RearUnderCover", name = "리어 언더 커버",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "차 뒤쪽 밑에서 볼트를 푼다 — 고전압 차단 절차 8단계 전에 프런트·리어 언더 커버를 모두 뗀다",
            objects = new[] { "SM_Bottom_02" },
            bolts = new[] {
                new F(-0.6047f, -0.9372f, 0.0000f, 0f, 0f, -1f), new F(-0.6047f, -0.5872f, 0.0189f, 0f, 0f, -1f), new F(-0.5547f, -0.1872f, 0.0423f, 0f, 0f, -1f),
                new F(0.5953f, -0.9372f, 0.0000f, 0f, 0f, -1f),  new F(0.5953f, -0.5872f, 0.0190f, 0f, 0f, -1f),  new F(0.5953f, -0.1872f, 0.0420f, 0f, 0f, -1f) },
            tMin = 0.8f, tMax = 1.2f, specPart = "리어 언더 커버 볼트", specSource = "후륜 모터 및 감속기 시스템 — 리어 언더 커버 탈거 및 장착" },
        new PartDef {
            id = "FrontSuspension", name = "전륜 서브 프레임·서스펜션",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "전륜 모터·감속기를 뗀 뒤 앞 양쪽 휠을 떼고, 유니버설 조인트 볼트(재사용 금지)·타이로드 엔드·텐션/레터럴 암 볼 조인트(분할 핀 폐기, 풀러 09568-GI000)를 분리한 다음 스테이 너트와 서브 프레임 볼트(18~20)를 푼다 — 서브 프레임은 스티어링 기어박스·스태빌라이저 바·로어 암·프런트 스트럿·앞 브레이크를 단 채 어셈블리째 내려온다. 바닥에 내려놓고 그 위에서 세부 부품을 분해한다",
            objects = new[] { "SM_FrontSubframe" },
            bolts = new[] {
                new F(0.1086f, -0.0819f, -0.3500f, 0f, -1f, 0f), new F(0.1086f, -0.0819f, 0.3500f, 0f, -1f, 0f),
                new F(0.3086f, -0.1672f, -0.4000f, 0f, -1f, 0f), new F(0.3086f, -0.1672f, 0.4000f, 0f, -1f, 0f) },
             needsWheel = 0, needsWheel2 = 1,
            tMin = 18.0f, tMax = 20.0f, boltScale = 1.6f, specPart = "서브 프레임 장착 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크 — 프런트 서브 프레임" },
        new PartDef {
            id = "RearMotor", name = "후륜 모터·감속기(IDA)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2, hv = true,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 모터·감속기 마운팅 볼트를 푼다 (HV 커넥터·냉각 호스 분리, 오일 쿨러 호스 주의, 고전압 차단 선행)",
            objects = new[] { "SM_RearElectricengine", "SM_RearPowerDistribution" },
            bolts = new[] {
                new F(-0.1098f, -0.0760f, -0.0670f, 0f, -1f, 0f), new F(-0.1098f, -0.0289f, 0.1030f, 0f, -1f, 0f),
                new F(0.0902f, -0.0830f, 0.0030f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 11f, tMax = 13f, boltScale = 1.4f, specPart = "후륜 모터 마운팅 볼트", specSource = "전륜 모터 마운팅 규정 준용 (후륜 값 미기재)" },
        new PartDef {
            id = "RearShockL", name = "리어 쇽 업소버(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후륜 서스펜션 어셈블리를 내려놓은 뒤 그 위에서 로어 암 쪽 볼트·너트(18~20)와 차체 쪽 볼트(5.0~6.5)를 풀어 쇽을 분리한다 — 변속기 잭으로 무부하 상태에서, 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearShock_L" },
            bolts = new[] { new F(0.1704f, 0.2196f, -0.6000f, 0f, 1f, 0f), new F(0.2504f, 0.2225f, -0.5700f, 0f, 1f, 0f) },
            parent = "RearSuspension",
            tMin = 5.0f, tMax = 6.5f, specPart = "쇽 업소버 차체 장착 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 쇽 업소버" },
        new PartDef {
            id = "RearAxleL", name = "리어 액슬 어셈블리(왼쪽 너클·허브)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후륜 서스펜션 어셈블리를 내려놓은 뒤 그 위에서 너클·허브 연결 볼트를 푼다 — 캐리어 쪽 로어·트레일링·어시스트 암 볼트는 각 암 쪽에서 푼다 (브레이크 캘리퍼·디스크는 액슬에 붙어 있다)",
            objects = new[] { "SM_RearAxle_L" },
            bolts = new[] { new F(0.1404f, -0.0590f, -0.7656f, 0f, 0f, -1f), new F(0.2404f, -0.1590f, -0.8232f, 0f, 0f, -1f) },
            parent = "RearSuspension",
            tMin = 8.6f, tMax = 11.6f, boltScale = 1.4f, specPart = "액슬(너클) 연결 볼트(M12)", specSource = SrcM12 },
        new PartDef {
            id = "RearLowerArmL", name = "리어 로어 암(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 크로스 멤버 쪽 볼트·너트(11~12)를 푼다 — 변속기 잭으로 무부하 상태에서, 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearLowerArm_L" },
            bolts = new[] { new F(0.2404f, -0.1369f, -0.4500f, 0f, -1f, 0f), new F(0.2204f, -0.1857f, -0.1700f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 11.0f, tMax = 12.0f, boltScale = 1.4f, specPart = "로어 암 크로스 멤버 측 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 로어 암" },
        new PartDef {
            id = "RearTrailingArmL", name = "리어 트레일링 암(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 크로스 멤버 쪽 볼트·너트(12~14)를 푼다 — 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearTrailingArm_L" },
            bolts = new[] { new F(-0.3096f, -0.1195f, -0.6100f, 0f, -1f, 0f), new F(-0.0196f, -0.1867f, -0.6500f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 12.0f, tMax = 14.0f, boltScale = 1.4f, specPart = "트레일링 암 장착 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크 — 트레일링 암" },
        new PartDef {
            id = "RearShockR", name = "리어 쇽 업소버(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후륜 서스펜션 어셈블리를 내려놓은 뒤 그 위에서 로어 암 쪽 볼트·너트(18~20)와 차체 쪽 볼트(5.0~6.5)를 풀어 쇽을 분리한다 — 변속기 잭으로 무부하 상태에서, 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearShock_R" },
            bolts = new[] { new F(0.1704f, 0.2196f, 0.6000f, 0f, 1f, 0f), new F(0.2504f, 0.2225f, 0.5700f, 0f, 1f, 0f) },
            parent = "RearSuspension",
            tMin = 5.0f, tMax = 6.5f, specPart = "쇽 업소버 차체 장착 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 쇽 업소버" },
        new PartDef {
            id = "RearAxleR", name = "리어 액슬 어셈블리(오른쪽 너클·허브)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후륜 서스펜션 어셈블리를 내려놓은 뒤 그 위에서 너클·허브 연결 볼트를 푼다 — 캐리어 쪽 로어·트레일링·어시스트 암 볼트는 각 암 쪽에서 푼다 (브레이크 캘리퍼·디스크는 액슬에 붙어 있다)",
            objects = new[] { "SM_RearAxle_R" },
            bolts = new[] { new F(0.1404f, -0.0590f, 0.7656f, 0f, 0f, 1f), new F(0.2404f, -0.1590f, 0.8240f, 0f, 0f, 1f) },
            parent = "RearSuspension",
            tMin = 8.6f, tMax = 11.6f, boltScale = 1.4f, specPart = "액슬(너클) 연결 볼트(M12)", specSource = SrcM12 },
        new PartDef {
            id = "RearLowerArmR", name = "리어 로어 암(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 크로스 멤버 쪽 볼트·너트(11~12)를 푼다 — 변속기 잭으로 무부하 상태에서, 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearLowerArm_R" },
            bolts = new[] { new F(0.2404f, -0.1369f, 0.4500f, 0f, -1f, 0f), new F(0.2204f, -0.1857f, 0.1700f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 11.0f, tMax = 12.0f, boltScale = 1.4f, specPart = "로어 암 크로스 멤버 측 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 로어 암" },
        new PartDef {
            id = "RearTrailingArmR", name = "리어 트레일링 암(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 크로스 멤버 쪽 볼트·너트(12~14)를 푼다 — 볼트를 고정하고 너트를 푼다",
            objects = new[] { "SM_RearTrailingArm_R" },
            bolts = new[] { new F(-0.3096f, -0.1195f, 0.6100f, 0f, -1f, 0f), new F(-0.0196f, -0.1867f, 0.6500f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 12.0f, tMax = 14.0f, boltScale = 1.4f, specPart = "트레일링 암 장착 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크 — 트레일링 암" },
        new PartDef {
            id = "RearSuspension", name = "리어 크로스 멤버(서브 프레임)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "모터 냉각수를 배출하고 리어 언더 커버를 뗀 뒤 뒤 두 휠을 떼고, 크로스 멤버 스테이와 장착 볼트(18~20)를 풀어 리프트 테이블로 받쳐 내린다 — 후륜 서스펜션은 쇽 업소버·액슬(캐리어, 브레이크 포함)·로어 암·트레일링 암·스태빌라이저 바·후륜 모터·정션 블록을 단 채 어셈블리째 내려온다. 바닥에 내려놓고 그 위에서 세부 부품을 분해한다",
            objects = new[] { "SM_RearCrossmember" },
            bolts = new[] { new F(-0.0896f, -0.0467f, -0.4900f, 0f, -1f, 0f), new F(0.2304f, -0.0012f, -0.3300f, 0f, -1f, 0f), new F(-0.0896f, -0.0467f, 0.4900f, 0f, -1f, 0f), new F(0.2304f, -0.0012f, 0.3300f, 0f, -1f, 0f) },
            requires = new[] { "RearUnderCover" }, needsWheel = 2, needsWheel2 = 3,
            tMin = 18.0f, tMax = 20.0f, boltScale = 1.5f, specPart = "크로스 멤버 장착 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 크로스 멤버" },
        new PartDef {
            id = "SeatFL", name = "앞 좌석(왼쪽·운전석)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "도어를 열고 시트 밑 레일 볼트를 푼다 — 시트 하부 커넥터(에어백·열선·벨트 경고)를 분리하고, 에어백 커넥터는 배터리(−) 분리 뒤에 뺀다",
            objects = new[] { "SM_Seat_FL" },
            bolts = new[] { new F(-0.4256f, 0.4779f, -1.1085f, 0f, 1f, 0f), new F(-0.4256f, 0.4996f, -0.8086f, 0f, 1f, 0f) },
            tMin = 5.0f, tMax = 6.5f, specPart = "프런트 시트 장착 볼트", specSource = "바디 — 체결 토크(시트, 프런트 시트 장착 볼트)" },
        new PartDef {
            id = "SeatFR", name = "앞 좌석(오른쪽·동승석)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "도어를 열고 시트 밑 레일 볼트를 푼다 — 시트 하부 커넥터(에어백·열선·벨트 경고)를 분리하고, 에어백 커넥터는 배터리(−) 분리 뒤에 뺀다",
            objects = new[] { "SM_Seat_FR" },
            bolts = new[] { new F(0.3742f, 0.4552f, -1.1085f, 0f, 1f, 0f), new F(0.3742f, 0.4765f, -0.8086f, 0f, 1f, 0f) },
            tMin = 5.0f, tMax = 6.5f, specPart = "프런트 시트 장착 볼트", specSource = "바디 — 체결 토크(시트, 프런트 시트 장착 볼트)" },
        new PartDef {
            id = "SeatRear", name = "뒷좌석(시트 쿠션·등받이)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "쿠션 앞쪽을 들어 올려 고정 클립을 풀고 하부 볼트를 푼다 — 등받이는 쿠션을 뗀 뒤 힌지 볼트",
            objects = new[] { "SM_Seat_Rear" },
            bolts = new[] { new F(-0.4256f, 0.3358f, 0.1912f, 0f, 1f, 0f), new F(0.3742f, 0.3349f, 0.1912f, 0f, 1f, 0f) },
            tMin = 4.5f, tMax = 6.0f, specPart = "2열 시트 장착 볼트", specSource = "바디 — 체결 토크(시트, 2열 시트 장착 볼트)" },
        new PartDef {
            id = "Cockpit", name = "크래시 패드(대시보드)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 프런트 필라 트림·플로어 콘솔·글러브 박스·다기능 스위치·헤드업 디스플레이·포토 센서·센터 스피커를 뗀 뒤, 벤트 덕트 센서와 동승석 에어백 커넥터를 분리하고 볼트·너트(2.5~2.8)를 풀어 들어낸다 (HVAC·블로워는 그 안쪽)",
            objects = new[] { "SM_Ioniq_Cockpit" },
            bolts = new[] {
                new F(-0.2222f, -0.5406f, -0.1607f, 0f, 0f, -1f), new F(0.0278f, -0.5406f, -0.1389f, 0f, 0f, -1f),
                new F(-0.1222f, 0.0094f, -0.1519f, 0f, 0f, -1f),
                new F(-0.2222f, 0.5594f, -0.1604f, 0f, 0f, -1f),  new F(0.0278f, 0.5594f, -0.1363f, 0f, 0f, -1f) },
            tMin = 2.5f, tMax = 2.8f, specPart = "크래시 패드 장착 볼트·너트", specSource = "바디 — 메인 크래쉬 패드 어셈블리 탈거 및 장착(2.5~2.8)" },
        // 대시 안쪽 HVAC·블로워 — 차 모델에 없어 상자·원통으로 만든다 (영상: 블로워 모터 진동·냉매 라인 점검은 대시를 뜯어야 보인다)
        new PartDef {
            id = "Hvac", name = "HVAC·블로워 유닛",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "냉매를 회수하고 카울 크로스 바 절차로 뺀다 — 크래시 패드·와이퍼 모터·카울 사이드 트림·플로어 콘솔·앞뒤 시트·센터 필라 로어 트림을 떼고 스티어링 컬럼을 아래로 이격한 뒤, 히터 유닛 커넥터(액추에이터·센서·PTC)를 분리하고 장착 너트·볼트와 카울 크로스 바 볼트(1.8~2.4)를 푼다",
            gen = new[] {
                G.Box(-1.05f, 0.80f, 0f, 0.34f, 0.20f, 0.72f, Plastic, 0f),                                       // 케이스
                G.Box(-1.21f, 0.80f, -0.10f, 0.02f, 0.17f, 0.46f, Alu, 0.8f),                                      // 증발기 코어
                G.Cyl(-1.02f, 0.93f, 0.25f, 0.26f, 0.12f, Fin, 0.2f),                                              // 블로워 스크롤(축 = 위아래)
                G.Cyl(-1.02f, 0.99f, 0.25f, 0.10f, 0.05f, DarkAlu, 0.7f),                                          // 블로워 모터
                G.Cyl(-1.17f, 0.80f, 0.40f, 0.03f, 0.08f, Alu, 0.8f, 0f, 0f, 90f), G.Cyl(-1.17f, 0.80f, 0.32f, 0.025f, 0.08f, Alu, 0.8f, 0f, 0f, 90f) },   // 냉매 라인 포트
            bolts = new[] {
                new F(-1.17f, 0.905f, -0.28f, 0f, 1f, 0f), new F(-0.93f, 0.905f, -0.28f, 0f, 1f, 0f), new F(-1.05f, 0.905f, 0.02f, 0f, 1f, 0f) },
            requires = new[] { "Cockpit", "SeatFL", "SeatFR", "SeatRear" },
            tMin = 1.8f, tMax = 2.4f, specPart = "카울 크로스 바 장착 볼트", specSource = "바디 — 카울 크로스 바 어셈블리 탈거 및 장착(1.8~2.4)" },

        // ══════════════════════════════════════════════════════════════
        //  11/16 추가(2) — 차를 이루는 나머지 부품: 12V 배터리·브레이크(차 모델 메시) + 모델에 없는 하부·구동 내부 부품(생성 파츠).
        //  정비 매뉴얼 구성부품 목록(배터리 제어 시스템: 인버터·ICCU·고전압 정션 블록, 프런트/리어 서스펜션: 스티어링 기어박스·스태빌라이저 바·스트럿·쇽 업소버)을 따름.
        //  생성 파츠는 전부 외판 안쪽(프렁크·후드·언더 커버 밑)에 있어 바깥 부품을 떼기 전에는 보이지 않는다.
        //  ※ 펜더·사이드 실·미러·스포일러·와이퍼·테일게이트 위쪽은 11/17 에 CarBodyPanelSplitter 로 SM_Ioniq_Electric 에서 잘라 냈다(맨 아래 부위 참고).
        //    루프·쿼터 패널·윈드쉴드(접착식)는 차체에 남긴다.
        // ══════════════════════════════════════════════════════════════
        new PartDef {
            id = "Battery12VCover", name = "12V 배터리 커버",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "프렁크를 뗀 뒤 12V 배터리 위 커버 볼트를 풀고 들어낸다",
            objects = new[] { "Battery_12V_Cover" },
            bolts = new[] { new F(-0.0888f, 0.0354f, 0.0059f, 0f, 1f, 0f), new F(0.1112f, 0.0354f, 0.0059f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 1.0f, tMax = 1.4f, specPart = "12V 배터리 커버 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "Battery12V", name = "12V 보조 배터리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "Drive Ready OFF → (−) 단자 → (+) 단자 순서로 분리(단자 0.8~1.0) → 클램프 볼트(0.9~1.4)를 풀고 들어낸다. 장착은 같은 규격 배터리로, (+) → (−) 순서",
            objects = new[] { "SM_12v" },
            bolts = new[] { new F(0f, 0.04f, -0.10f, 0f, 1f, 0f), new F(0.10f, 0.03f, -0.10f, 0f, 1f, 0f), new F(-0.2383f, -0.0664f, 0.0898f, 0f, 1f, 0f) },
            requires = new[] { "Battery12VCover" },
            tMin = 0.9f, tMax = 1.4f, specPart = "12V 배터리 클램프 볼트", specSource = "차량 제어 시스템 — 12V 배터리 탈거 및 장착" },
        new PartDef {
            id = "BrakeFL", name = "브레이크(앞 왼쪽 캘리퍼·디스크)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 휠 속도 센서 클립·브레이크 호스 브래킷(0.9~1.4)을 분리하고 캘리퍼 볼트(10~12, 재사용 금지)를 푼다 — 캘리퍼는 호스를 분리하지 말고 케이블 타이로 매달아 둔다. 디스크 볼트는 0.5~0.6",
            objects = new[] { "SM_Brake_FL" },
            bolts = new[] { new F(-1.4847f, -0.1256f, -0.8892f, 0f, 0f, -1f), new F(-1.4847f, -0.0556f, -0.8892f, 0f, 0f, -1f) },
            parent = "FrontSuspension",
            tMin = 10.0f, tMax = 12.0f, boltScale = 1.4f, specPart = "프런트 캘리퍼 볼트(재사용 금지)", specSource = "브레이크 시스템 — 체결토크(프런트 캘리퍼 볼트)" },
        new PartDef {
            id = "BrakeFR", name = "브레이크(앞 오른쪽 캘리퍼·디스크)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 휠 속도 센서 클립·브레이크 호스 브래킷(0.9~1.4)을 분리하고 캘리퍼 볼트(10~12, 재사용 금지)를 푼다 — 캘리퍼는 호스를 분리하지 말고 케이블 타이로 매달아 둔다. 디스크 볼트는 0.5~0.6",
            objects = new[] { "SM_Brake_FR" },
            bolts = new[] { new F(-1.4847f, -0.1256f, 0.8963f, 0f, 0f, 1f), new F(-1.4847f, -0.0556f, 0.8963f, 0f, 0f, 1f) },
            parent = "FrontSuspension",
            tMin = 10.0f, tMax = 12.0f, boltScale = 1.4f, specPart = "프런트 캘리퍼 볼트(재사용 금지)", specSource = "브레이크 시스템 — 체결토크(프런트 캘리퍼 볼트)" },
        new PartDef {
            id = "BrakeRL", name = "브레이크(뒤 왼쪽 캘리퍼·디스크)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "액슬을 내려놓은 뒤 그 위에서 진단기로 주차 브레이크(EPB)를 해제하고 EPB 액추에이터 커넥터를 분리한다 — 캘리퍼 바디(2.2~3.2)와 패드를 뺀 다음 캐리어 볼트(8~10, 재사용 금지)를 푼다. 디스크 볼트는 0.5~0.6",
            objects = new[] { "SM_Brake_RL" },
            bolts = new[] { new F(1.5153f, -0.1256f, -0.8899f, 0f, 0f, -1f), new F(1.5153f, -0.0556f, -0.8899f, 0f, 0f, -1f) },
            parent = "RearAxleL",
            tMin = 8.0f, tMax = 10.0f, boltScale = 1.4f, specPart = "리어 캘리퍼 캐리어 볼트(재사용 금지)", specSource = "브레이크 시스템 — 체결토크(리어 캘리퍼 볼트)" },
        new PartDef {
            id = "BrakeRR", name = "브레이크(뒤 오른쪽 캘리퍼·디스크)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "액슬을 내려놓은 뒤 그 위에서 진단기로 주차 브레이크(EPB)를 해제하고 EPB 액추에이터 커넥터를 분리한다 — 캘리퍼 바디(2.2~3.2)와 패드를 뺀 다음 캐리어 볼트(8~10, 재사용 금지)를 푼다. 디스크 볼트는 0.5~0.6",
            objects = new[] { "SM_Brake_RR" },
            bolts = new[] { new F(1.5153f, -0.1256f, 0.8944f, 0f, 0f, 1f), new F(1.5153f, -0.0556f, 0.8944f, 0f, 0f, 1f) },
            parent = "RearAxleR",
            tMin = 8.0f, tMax = 10.0f, boltScale = 1.4f, specPart = "리어 캘리퍼 캐리어 볼트(재사용 금지)", specSource = "브레이크 시스템 — 체결토크(리어 캘리퍼 볼트)" },
        // ── 프렁크 아래 구동·고전압 부품 ──
        new PartDef {
            id = "FrontInverter", name = "전륜 인버터(EPCU)", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 후 프렁크를 뗀다 — 인버터 단자 전압(30V 이하)을 확인하고 HV 커넥터(오렌지)·냉각 호스를 분리한 뒤 윗면 볼트를 푼다. 모터보다 먼저 뗀다",
            gen = new[] {
                G.Box(-1.96f, 0.725f, -0.02f, 0.32f, 0.15f, 0.40f, DarkAlu, 0.6f),
                G.Box(-1.96f, 0.805f, -0.02f, 0.26f, 0.02f, 0.34f, Alu, 0.8f),
                G.Box(-2.14f, 0.74f, 0.12f, 0.04f, 0.05f, 0.08f, HvOrange, 0f), G.Box(-2.14f, 0.74f, -0.16f, 0.04f, 0.05f, 0.08f, HvOrange, 0f) },
            bolts = new[] {
                new F(-2.06f, 0.815f, -0.17f, 0f, 1f, 0f), new F(-1.86f, 0.815f, -0.17f, 0f, 1f, 0f),
                new F(-2.06f, 0.815f, 0.13f, 0f, 1f, 0f),  new F(-1.86f, 0.815f, 0.13f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 2.5f, tMax = 3.3f, specPart = "인버터 장착 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "Iccu", name = "통합 충전기·컨버터(ICCU)", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 모터 냉각수 배출 → 뒷좌석을 뗀 뒤 좌석 밑 ICCU의 접지(1.1~1.4)·AC·DC·신호 커넥터·LDC 플러스 케이블(0.7~1.0)·냉각수 퀵 커넥터를 분리하고 장착 볼트(2.0~2.4)를 푼다 (OBC·LDC 통합 유닛, 매뉴얼상 러기지 사이드 트림도 먼저 뗀다)",
            gen = new[] {
                G.Box(0.55f, 0.42f, -0.35f, 0.46f, 0.09f, 0.40f, Alu, 0.7f),
                G.Box(0.55f, 0.468f, -0.35f, 0.38f, 0.012f, 0.32f, DarkAlu, 0.6f),
                G.Box(0.30f, 0.43f, -0.46f, 0.04f, 0.05f, 0.07f, HvOrange, 0f), G.Box(0.30f, 0.43f, -0.24f, 0.04f, 0.05f, 0.07f, HvOrange, 0f) },
            bolts = new[] {
                new F(0.38f, 0.476f, -0.50f, 0f, 1f, 0f), new F(0.72f, 0.476f, -0.50f, 0f, 1f, 0f),
                new F(0.38f, 0.476f, -0.20f, 0f, 1f, 0f), new F(0.72f, 0.476f, -0.20f, 0f, 1f, 0f) },
            requires = new[] { "SeatRear" },
            tMin = 2.0f, tMax = 2.4f, specPart = "ICCU 장착 볼트", specSource = "배터리 제어 시스템 — 통합 충전기 및 컨버터 유닛(ICCU) 탈거" },
        new PartDef {
            id = "JunctionBlock", name = "고전압 정션 블록(프런트)", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 프런트 언더 커버를 뗀 뒤 HV 커넥터 커버(0.8~1.2)를 열고 배터리 프런트·컴프레서·PTC 커넥터를 분리한 다음 인버터 체결 볼트와 장착 볼트(0.7~1.0)를 푼다 (매뉴얼은 아래에서 접근 — 이 모델에서는 프렁크를 떼고 윗면 볼트를 푼다. 장착 뒤 전륜 모터·감속기 기밀 점검)",
            gen = new[] {
                G.Box(-1.62f, 0.63f, -0.12f, 0.22f, 0.10f, 0.28f, DarkAlu, 0.5f),
                G.Box(-1.49f, 0.64f, -0.20f, 0.03f, 0.04f, 0.06f, HvOrange, 0f), G.Box(-1.49f, 0.64f, -0.04f, 0.03f, 0.04f, 0.06f, HvOrange, 0f) },
            bolts = new[] {
                new F(-1.69f, 0.683f, -0.22f, 0f, 1f, 0f), new F(-1.55f, 0.683f, -0.22f, 0f, 1f, 0f),
                new F(-1.69f, 0.683f, -0.02f, 0f, 1f, 0f), new F(-1.55f, 0.683f, -0.02f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 0.7f, tMax = 1.0f, specPart = "정션 블록 장착 볼트", specSource = "배터리 제어 시스템 — 고전압 정션 블록 탈거 및 장착(프런트)" },

        new PartDef {
            id = "RearJunctionBlock", name = "고전압 정션 블록(리어)", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 와이어링 커넥터와 HV 케이블 볼트(2.0~2.4), 서비스 커버(0.4~0.6), 인버터 체결 볼트(0.7~1.0)를 풀고 정션 블록 장착 볼트·너트(2.0~2.4)를 푼다 — 장착 뒤 후륜 모터·감속기 기밀 점검",
            gen = new[] {
                G.Box(1.45f, 0.40f, 0f, 0.20f, 0.12f, 0.30f, DarkAlu, 0.5f),
                G.Box(1.45f, 0.465f, 0f, 0.16f, 0.012f, 0.26f, Alu, 0.7f),
                G.Box(1.45f, 0.41f, -0.19f, 0.05f, 0.05f, 0.04f, HvOrange, 0f), G.Box(1.45f, 0.41f, 0.19f, 0.05f, 0.05f, 0.04f, HvOrange, 0f) },
            bolts = new[] {
                new F(1.38f, 0.333f, -0.11f, 0f, -1f, 0f), new F(1.52f, 0.333f, -0.11f, 0f, -1f, 0f),
                new F(1.38f, 0.333f, 0.11f, 0f, -1f, 0f),  new F(1.52f, 0.333f, 0.11f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 2.0f, tMax = 2.4f, specPart = "리어 정션 블록 장착 볼트·너트", specSource = "배터리 제어 시스템 — 고전압 정션 블록 탈거 및 장착(리어)" },
        // ══ 세부 부위(어셈블리 안) · 추가 부위 ══
        new PartDef {
            id = "CoolingFan", name = "쿨링 팬 어셈블리", parent = "Cooling",
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "쿨링 팬 커넥터와 라디에이터 상부 호스 파스너를 분리하고 팬 고정 볼트(0.5~0.8)를 풀어 뺀다 (점검은 진단기 강제구동 저속·고속)",
            gen = new[] {
                G.Cyl(-2.375f, 0.53f, -0.24f, 0.32f, 0.03f, Plastic, 0f, 0f, 0f, 90f), G.Cyl(-2.375f, 0.53f, 0.24f, 0.32f, 0.03f, Plastic, 0f, 0f, 0f, 90f),
                G.Cyl(-2.355f, 0.53f, -0.24f, 0.10f, 0.04f, DarkAlu, 0.7f, 0f, 0f, 90f), G.Cyl(-2.355f, 0.53f, 0.24f, 0.10f, 0.04f, DarkAlu, 0.7f, 0f, 0f, 90f) },
            bolts = new[] { new F(-2.395f, 0.725f, -0.24f, 0f, 1f, 0f), new F(-2.395f, 0.725f, 0.24f, 0f, 1f, 0f) },
            tMin = 0.5f, tMax = 0.8f, specPart = "쿨링 팬 고정 볼트", specSource = "냉각시스템 — 쿨링팬 탈거 및 장착(0.5~0.8)" },
        new PartDef {
            id = "Condenser", name = "에어컨 콘덴서", parent = "Cooling",
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "에어컨 냉매 파이프 마운팅 볼트(0.5~0.8)를 풀고 라디에이터에서 콘덴서를 분리한다 — 냉매 라인은 분리하지 않는다 (핀이 막히거나 찌그러지지 않게 주의)",
            gen = new[] { G.Box(-2.47f, 0.53f, 0f, 0.02f, 0.38f, 1.0f, Fin, 0.3f) },
            bolts = new[] { new F(-2.484f, 0.66f, -0.45f, -1f, 0f, 0f), new F(-2.484f, 0.66f, 0.45f, -1f, 0f, 0f) },
            tMin = 0.5f, tMax = 0.8f, specPart = "에어컨 파이프 마운팅 볼트", specSource = "냉각시스템 — 라디에이터 탈거(냉매 파이프 마운팅 볼트 0.5~0.8)" },
        new PartDef {
            id = "AcInverter", name = "컴프레서 인버터", parent = "AcCompressor", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "(청정 환경) 인버터 커버 스크류(0.5~0.8)를 풀고 커버·가스켓·절연 시트를 뗀다 — 커버·스크류·가스켓·절연 시트는 재사용 금지, 3상 전원핀·IGBT가 휘지 않게 주의, 고전압 차단 선행",
            gen = new[] {
                G.Box(-2.26f, 0.41f, 0.30f, 0.12f, 0.12f, 0.08f, DarkAlu, 0.6f),
                G.Box(-2.26f, 0.49f, 0.30f, 0.05f, 0.04f, 0.05f, HvOrange, 0f) },
            bolts = new[] { new F(-2.30f, 0.475f, 0.30f, 0f, 1f, 0f), new F(-2.22f, 0.475f, 0.30f, 0f, 1f, 0f) },
            tMin = 0.5f, tMax = 0.8f, boltScale = 0.8f, specPart = "인버터 커버 스크류(재사용 금지)", specSource = "히터 및 에어컨 장치 — 전동식 에어컨 컴프레서 분해(인버터 커버 0.5~0.8)" },
        new PartDef {
            id = "Vcu", name = "차량 제어 유닛(VCU)",
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 터미널을 분리하고 N 액티브 사운드 스피커(프런트)를 뗀 뒤 VCU 커넥터를 분리하고 VCU 볼트(1.0~1.2)를 푼다",
            gen = new[] { G.Box(-1.60f, 0.64f, 0.42f, 0.18f, 0.04f, 0.12f, DarkAlu, 0.5f), G.Box(-1.50f, 0.65f, 0.42f, 0.03f, 0.03f, 0.08f, Plastic, 0f) },
            bolts = new[] { new F(-1.66f, 0.665f, 0.42f, 0f, 1f, 0f), new F(-1.54f, 0.665f, 0.42f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 1.0f, tMax = 1.2f, specPart = "VCU 장착 볼트", specSource = "차량 제어 시스템 — 차량 제어 유닛(VCU) 탈거 및 장착(1.0~1.2)" },
        new PartDef {
            id = "Qra", name = "급속 충전 릴레이(QRA)", parent = "RearJunctionBlock", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 정션 블록 어퍼 커버(0.8~1.2)를 열고 파워 케이블 버스바·QRA 커넥터를 분리한 뒤 QRA 트레이와 (+)·(−) 버스바 볼트·너트(0.8~1.2)를 풀어 QRA를 뺀다 — 장착 때 록타이트 잔여물·가스켓 주의, 장착 뒤 후륜 기밀 점검",
            gen = new[] { G.Box(1.45f, 0.50f, 0f, 0.10f, 0.06f, 0.16f, Alu, 0.7f), G.Box(1.45f, 0.535f, 0f, 0.05f, 0.012f, 0.12f, HvOrange, 0f) },
            bolts = new[] { new F(1.42f, 0.545f, 0f, 0f, 1f, 0f), new F(1.48f, 0.545f, 0f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.8f, specPart = "QRA 버스바 볼트·너트", specSource = "배터리 제어 시스템 — 급속 충전 릴레이 어셈블리(QRA) 탈거(0.8~1.2)" },
        // ── 배터리 팩 — 차 모델의 실제 메시를 쓴다 (BatteryPackPos/BatteryPack 아래 노드).
        //    계층: 하부 커버 → 팩 어셈블리(BSA) → 상부 케이스 → BMU · 메인 퓨즈 → PRA. 팩을 내려놓은 뒤 그 위에서 안쪽 부위를 푼다.
        //    BMU·메인 퓨즈·PRA 는 SM_BatteryParts1 의 조각 중 해당 자리의 것을 분리한 메시(SM_Bmu·SM_MainFuse·SM_Pra), 볼트 자리는 메시 표면에서 광선으로 잰 값(기준 노드 로컬 좌표).
        new PartDef {
            id = "BatteryPackCover", name = "배터리 팩 하부 커버", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 차를 리프트로 올린 뒤 하부 커버 볼트를 풀어 커버를 내린다 — 커버를 떼야 팩 테두리 장착 볼트가 보인다. 장착 뒤 배터리 기밀 점검",
            objects = new[] { "SM_BatteryCover" },
            bolts = new[] {
                new F(0.6970f, -0.9070f, 0.0191f, 0f, 0f, 1f), new F(0.6970f, -0.1070f, 0.0191f, 0f, 0f, 1f), new F(0.6970f, 0.6930f, 0.0191f, 0f, 0f, 1f),
                new F(-0.7030f, -0.9070f, 0.0191f, 0f, 0f, 1f), new F(-0.7030f, -0.1070f, 0.0191f, 0f, 0f, 1f), new F(-0.7030f, 0.6930f, 0.0191f, 0f, 0f, 1f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.9f, specPart = "배터리 팩 하부 커버 볼트", specSource = "모터 및 감속기 시스템 — 언더 커버 탈거 및 장착(0.8~1.2)" },
        new PartDef {
            id = "BatteryPack", name = "배터리 팩 어셈블리(BSA)", hv = true,
            tool = "에어 임팩렌치(17mm 소켓) · 리프트 테이블",
            socket = 2,
            hint = "고전압 차단 → 배터리 냉각수 배출 → 프런트·리어 고전압 커넥터·ICCU 고전압 커넥터를 분리하고, 팩을 리프트 테이블로 받친 뒤 팩 테두리 장착 볼트 16개를 풀어 내린다 — 팩은 무거우므로 리프트 테이블 없이 내리지 않는다. 내려놓은 팩 위에서 상부 케이스를 열고 BMU·메인 퓨즈·PRA를 분해한다. 장착 뒤 배터리 기밀 테스트(팩 장착 볼트 토크는 매뉴얼 미확보 — 일반 토크로 대체)",
            objects = new[] { "SM_BatteryPack", "SM_Module", "SM_BatteryParts2", "SM_BatteryParts1_Rest", "SM_FrontConnector" },
            bolts = new[] {
                new F(0.9400f, -0.1011f, 0.7709f, 0f, -1f, 0f), new F(0.7200f, -0.1011f, 0.7709f, 0f, -1f, 0f), new F(0.4940f, -0.1011f, 0.7709f, 0f, -1f, 0f), new F(0.2320f, -0.1011f, 0.7829f, 0f, -1f, 0f),
                new F(-0.0180f, -0.1011f, 0.7829f, 0f, -1f, 0f), new F(-0.2600f, -0.1011f, 0.7709f, 0f, -1f, 0f), new F(-0.6200f, -0.1011f, 0.7829f, 0f, -1f, 0f), new F(-0.8860f, -0.1011f, 0.7829f, 0f, -1f, 0f),
                new F(0.9640f, -0.1011f, -0.7791f, 0f, -1f, 0f), new F(0.7440f, -0.1011f, -0.7791f, 0f, -1f, 0f), new F(0.4700f, -0.1011f, -0.7791f, 0f, -1f, 0f), new F(0.2320f, -0.0983f, -0.7551f, 0f, -1f, 0f),
                new F(-0.0060f, -0.1011f, -0.7671f, 0f, -1f, 0f), new F(-0.2600f, -0.1011f, -0.7791f, 0f, -1f, 0f), new F(-0.6200f, -0.1011f, -0.7791f, 0f, -1f, 0f), new F(-0.8860f, -0.1011f, -0.7671f, 0f, -1f, 0f) },
            requires = new[] { "BatteryPackCover" },
            tMin = 5.0f, tMax = 6.5f, boltScale = 1.2f, specPart = "배터리 팩 장착 볼트(M10 추정)", specSource = SrcM10 },
        new PartDef {
            id = "BatteryTopCase", name = "배터리 팩 상부 케이스", parent = "BatteryPack", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "내려놓은 팩 위에서 상부 케이스 볼트를 풀어 케이스를 들어낸다 — 안쪽의 BMU·메인 퓨즈·PRA·배터리 모듈·센싱 와이어링이 드러난다. 장착 전 접촉면 이물질·유분·수분 제거(상부 케이스 볼트 토크는 매뉴얼 미확보 — 일반 토크로 대체)",
            objects = new[] { "SM_Cover" },
            bolts = new[] {
                new F(0.7912f, 0.0058f, 0.5700f, 0f, 1f, 0f), new F(0.3512f, 0.0058f, 0.5700f, 0f, 1f, 0f), new F(-0.0588f, 0.0058f, 0.5700f, 0f, 1f, 0f), new F(-0.3188f, 0.0058f, 0.5700f, 0f, 1f, 0f),
                new F(0.7912f, 0.0058f, -0.5500f, 0f, 1f, 0f), new F(0.3512f, 0.0058f, -0.5500f, 0f, 1f, 0f), new F(-0.0588f, 0.0058f, -0.5500f, 0f, 1f, 0f), new F(-0.3188f, 0.0058f, -0.5500f, 0f, 1f, 0f) },
            tMin = 1.0f, tMax = 1.4f, boltScale = 0.8f, specPart = "배터리 팩 상부 케이스 볼트(M6 추정)", specSource = SrcM6 },
        new PartDef {
            id = "Bmu", name = "배터리 매니지먼트 유닛(BMU)", parent = "BatteryTopCase", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "BMU 커넥터(이물질 주의)를 분리하고 BMU 볼트·너트를 1단계 0.9 → 2단계 1.1 순서로 푼다 — 볼트·너트·서비스 커버는 재사용 금지, 커버를 과하게 당기면 BMU·커넥터가 손상된다. 장착 뒤 기밀 점검·SOC 보정·SOH 초기화 (정상 상태에서는 차 밑 BMU 서비스 커버로 접근하지만, 이 모델은 팩을 열어 분해한다)",
            objects = new[] { "SM_Bmu" },
            bolts = new[] { new F(1.1036f, -0.0021f, -0.2007f, 0f, 1f, 0f), new F(0.9736f, 0.0267f, -0.0507f, 0f, 1f, 0f) },
            tMin = 0.9f, tMax = 1.1f, boltScale = 0.6f, specPart = "BMU 볼트·너트(1단계 0.9 → 2단계 1.1, 재사용 금지)", specSource = "배터리 제어 시스템 — 배터리 매니지먼트 유닛(BMU) 탈거(볼트·너트 0.9 → 1.1)" },
        new PartDef {
            id = "MainFuse", name = "메인 퓨즈 어셈블리(850V·700A)", parent = "BatteryTopCase", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "상부 케이스를 연 팩에서 퓨즈 박스 커버를 열고 버스바(0.8~1.2) → 메인 퓨즈(0.8~1.2) 순서로 푼다 — 점검은 저항 1.0Ω 이하(20℃), 장착 뒤 반드시 배터리 시스템 어셈블리 기밀 테스트 (절연이 정상이면 서비스 커버로 퓨즈 너트 1.6~1.7만 푼다)",
            objects = new[] { "SM_MainFuse" },
            bolts = new[] { new F(-1.0664f, 0.0473f, -0.1507f, 0f, 1f, 0f), new F(-1.0564f, 0.0116f, -0.3407f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.7f, specPart = "메인 퓨즈·버스바 볼트·너트", specSource = "배터리 제어 시스템 — 메인 퓨즈 탈거 및 장착(절연 파괴 시 버스바·퓨즈 0.8~1.2)" },
        new PartDef {
            id = "Pra", name = "파워 릴레이 어셈블리(PRA)", parent = "BatteryTopCase", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "BSA 탈거 → 상부 케이스 → 퓨즈 박스 커버 → 버스바·메인 퓨즈 → ICCU (−) 케이블·(+) 커넥터 → PRA 와이어링 커넥터를 분리한 뒤 PRA 너트(0.8~1.2)를 푼다 — PRA 버스바는 수공구로 1.4 이하로 푼다. 융착 점검은 진단기 'BMS 융착 상태' 또는 멀티미터",
            objects = new[] { "SM_Pra" },
            requires = new[] { "MainFuse" },
            bolts = new[] { new F(-1.0164f, 0.0182f, 0.3193f, 0f, 1f, 0f), new F(-1.0664f, 0.0489f, 0.1893f, 0f, 1f, 0f), new F(-1.0264f, 0.0376f, 0.0993f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.7f, specPart = "PRA 너트·버스바", specSource = "배터리 제어 시스템 — 파워 릴레이 어셈블리(PRA) 탈거 및 장착(0.8~1.2)" },
        // ── 냉각수 리저버 탱크(모터용·고전압 배터리용) — 프렁크 메시(SM_Frunk.001)에서 분리한 모델 탱크·뚜껑 ──
        //    매뉴얼(냉각 시스템 > 리저버 탱크): 12V (−) → N 액티브 사운드 스피커 → 모터·배터리 냉각수 배출 → EWP·3웨이 밸브 → 호스·커넥터 → 리저버 볼트·너트 0.8~1.2
        //    볼트 자리는 탱크 윗면 위에 정한 것(탱크 노드 로컬 좌표: 노드가 (0,90,180) 으로 돌아 있어 월드 (x,y,z) → 로컬 (z,−y,x)). 에디터에서 표면 검증은 못 했다.
        new PartDef {
            id = "CoolantTankMotor", name = "모터 냉각수 리저버 탱크",
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 모터 냉각수 배출 → EWP·3웨이 밸브·저온 라디에이터 상부 호스(퀵 커넥터 클램프를 위로 당겨 분리)·EWP 인렛 호스·커넥터를 떼고 볼트·너트(0.8~1.2)를 푼다 — 장착 뒤 냉각수 주입과 EWP 구동 에어빼기 필요. 약 16.6L 계통, 부동액+물 55~60% 혼합액",
            objects = new[] { "SM_CoolantTank_Motor", "SM_CoolantCap_Motor" },
            bolts = new[] { new F(0.390f, -0.0875f, -0.032f, 0f, -1f, 0f), new F(0.460f, -0.0875f, -0.032f, 0f, -1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.8f, specPart = "리저버 탱크 볼트·너트", specSource = "냉각 시스템 — 리저버 탱크 탈거 및 장착(0.8~1.2)" },
        new PartDef {
            id = "CoolantTankBattery", name = "고전압 배터리 냉각수 리저버 탱크", hv = true,
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 → 12V (−) 분리 → 배터리 냉각수(저전도, 물에 희석 금지) 배출 → 배터리 EWP·3웨이 밸브·호스를 떼고 볼트·너트(0.8~1.2)를 푼다 — 저전도 냉각수 압력 캡은 정비사만 탈거, 장착 뒤 VCU 강제구동 '배터리 EWP구동'으로 공기를 뺀다",
            objects = new[] { "SM_CoolantTank_Battery", "SM_CoolantCap_Battery" },
            bolts = new[] { new F(0.390f, -0.0875f, 0.358f, 0f, -1f, 0f), new F(0.460f, -0.0875f, 0.358f, 0f, -1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, boltScale = 0.8f, specPart = "리저버 탱크 볼트·너트", specSource = "냉각 시스템 — 리저버 탱크 탈거 및 장착(0.8~1.2)" },
        new PartDef {
            id = "ShockInsulatorL", name = "쇽 인슐레이터·록 너트(왼쪽)", parent = "RearShockL",
            tool = "쇽 록 너트 리무버(09546-3X100) · 에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "인슐레이터 캡을 떼고 특수공구 09546-3X100으로 록 너트(2.0~2.5)를 푼다 — 그 뒤 인슐레이터·범퍼 러버·더스트 커버를 빼면 쇽 본체만 남는다 (리어 코일 스프링은 쇽이 아니라 로어 암 쪽 부품)",
            gen = new[] { G.Cyl(1.27f, 0.81f, -0.58f, 0.05f, 0.04f, Plastic, 0f) },
            bolts = new[] { new F(1.27f, 0.832f, -0.58f, 0f, 1f, 0f) },
            tMin = 2.0f, tMax = 2.5f, boltScale = 0.9f, specPart = "쇽 록 너트", specSource = "서스펜션 시스템 — 리어 쇽 업소버 분해 및 조립(록 너트 2.0~2.5)" },
        new PartDef {
            id = "ShockInsulatorR", name = "쇽 인슐레이터·록 너트(오른쪽)", parent = "RearShockR",
            tool = "쇽 록 너트 리무버(09546-3X100) · 에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "인슐레이터 캡을 떼고 특수공구 09546-3X100으로 록 너트(2.0~2.5)를 푼다 — 그 뒤 인슐레이터·범퍼 러버·더스트 커버를 빼면 쇽 본체만 남는다 (리어 코일 스프링은 쇽이 아니라 로어 암 쪽 부품)",
            gen = new[] { G.Cyl(1.27f, 0.81f, 0.58f, 0.05f, 0.04f, Plastic, 0f) },
            bolts = new[] { new F(1.27f, 0.832f, 0.58f, 0f, 1f, 0f) },
            tMin = 2.0f, tMax = 2.5f, boltScale = 0.9f, specPart = "쇽 록 너트", specSource = "서스펜션 시스템 — 리어 쇽 업소버 분해 및 조립(록 너트 2.0~2.5)" },

        // ── 서스펜션·조향 ──
        new PartDef {
            id = "SteeringRack", name = "스티어링 기어박스",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 기어박스 장착 볼트(11~13)를 푼다 — 볼트는 모두 가체결한 다음 규정 토크까지 완체결(유격 최소화), 유니버셜 조인트 볼트(5~6)는 재사용 금지, 장착 후 얼라인먼트 점검",
            gen = new[] {
                G.Cyl(-1.33f, 0.42f, 0f, 0.07f, 0.85f, Alu, 0.7f, 90f, 0f, 0f),
                G.Cyl(-1.33f, 0.42f, -0.50f, 0.09f, 0.18f, Plastic, 0f, 90f, 0f, 0f), G.Cyl(-1.33f, 0.42f, 0.50f, 0.09f, 0.18f, Plastic, 0f, 90f, 0f, 0f) },
            bolts = new[] { new F(-1.33f, 0.382f, -0.20f, 0f, -1f, 0f), new F(-1.33f, 0.382f, 0.20f, 0f, -1f, 0f) },
            parent = "FrontSuspension",
            tMin = 11.0f, tMax = 13.0f, specPart = "스티어링 기어박스 장착 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 프런트 서브 프레임(스티어링 기어 박스와 서브 프레임 볼트)" },
        new PartDef {
            id = "TieRodEndL", name = "타이로드 엔드(왼쪽)", parent = "SteeringRack",
            tool = "에어 임팩렌치(17mm 소켓) · 볼 조인트 풀러(09568-GI000)", socket = 2,
            hint = "분할 핀(재사용 금지)을 뽑고 캐슬 너트(10~12)를 푼 뒤 볼 조인트 풀러로 타이로드 엔드를 분리한다 (고무 부트 손상 주의, 장착 후 토우 조정·로크 너트 5.0~5.5)",
            gen = new[] { G.Cyl(-1.33f, 0.42f, -0.62f, 0.028f, 0.10f, Alu, 0.7f), G.Cyl(-1.33f, 0.42f, -0.55f, 0.04f, 0.06f, Plastic, 0f) },
            bolts = new[] { new F(-1.33f, 0.475f, -0.62f, 0f, 1f, 0f) },
            tMin = 10.0f, tMax = 12.0f, boltScale = 0.9f, specPart = "타이로드 엔드 캐슬 너트(분할 핀 폐기)", specSource = "스티어링 시스템 — 프런트 서브 프레임(타이로드 엔드–너클 10.0~12.0)" },
        new PartDef {
            id = "TieRodEndR", name = "타이로드 엔드(오른쪽)", parent = "SteeringRack",
            tool = "에어 임팩렌치(17mm 소켓) · 볼 조인트 풀러(09568-GI000)", socket = 2,
            hint = "분할 핀(재사용 금지)을 뽑고 캐슬 너트(10~12)를 푼 뒤 볼 조인트 풀러로 타이로드 엔드를 분리한다 (고무 부트 손상 주의, 장착 후 토우 조정·로크 너트 5.0~5.5)",
            gen = new[] { G.Cyl(-1.33f, 0.42f, 0.62f, 0.028f, 0.10f, Alu, 0.7f), G.Cyl(-1.33f, 0.42f, 0.55f, 0.04f, 0.06f, Plastic, 0f) },
            bolts = new[] { new F(-1.33f, 0.475f, 0.62f, 0f, 1f, 0f) },
            tMin = 10.0f, tMax = 12.0f, boltScale = 0.9f, specPart = "타이로드 엔드 캐슬 너트(분할 핀 폐기)", specSource = "스티어링 시스템 — 프런트 서브 프레임(타이로드 엔드–너클 10.0~12.0)" },
        new PartDef {
            id = "SteeringPowerPack", name = "MDPS 파워팩", parent = "SteeringRack",
            tool = "에어 임팩렌치(10mm 소켓)",
            hint = "파워팩 커넥터를 분리하고 파워팩 볼트(0.9~1.5)를 푼다 — 기어박스 하우징을 이격할 때는 볼 너트가 빠지지 않게 고정 와셔(M8x30x1.5)를 쓴다. 장착 때 벨트 장력 조정, 교체 뒤 SAS·조향감 토크 영점 설정과 OTA 업데이트",
            gen = new[] { G.Box(-1.33f, 0.47f, 0.15f, 0.10f, 0.08f, 0.12f, DarkAlu, 0.6f) },
            bolts = new[] { new F(-1.37f, 0.515f, 0.15f, 0f, 1f, 0f), new F(-1.29f, 0.515f, 0.15f, 0f, 1f, 0f) },
            tMin = 0.9f, tMax = 1.5f, specPart = "MDPS 파워팩 볼트", specSource = "스티어링 시스템 — 체결 토크(MDPS 파워팩 볼트 0.9~1.5)" },
        new PartDef {
            id = "FrontStabilizer", name = "프런트 스태빌라이저 바",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 스태빌라이저 링크 너트(10~12, 링크 헥사를 잡고)와 바 고정 볼트(4.5~5.5)를 푼다",
            objects = new[] { "SM_FrontStabilizer" },
            bolts = new[] {
                new F(0.2086f, -0.0747f, -0.3000f, 0f, -1f, 0f), new F(0.2686f, -0.0766f, -0.3000f, 0f, -1f, 0f),
                new F(0.2086f, -0.0747f, 0.3000f, 0f, -1f, 0f), new F(0.2686f, -0.0766f, 0.3000f, 0f, -1f, 0f) },
            parent = "FrontSuspension",
            tMin = 4.5f, tMax = 5.5f, specPart = "프런트 스태빌라이저 바 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 프런트 스태빌라이저 바" },
        new PartDef {
            id = "FrontLowerArmL", name = "프런트 로어 암(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 서브 프레임 쪽 볼트·너트(16~18)를 푼다 — 모델에서는 텐션 암·레터럴 암이 한 판이라 한 부위로 다룬다",
            objects = new[] { "SM_FrontLowerArm_L" },
            bolts = new[] { new F(0.1086f, -0.1397f, -0.4000f, 0f, -1f, 0f), new F(0.2186f, -0.1335f, -0.4000f, 0f, -1f, 0f) },
            parent = "FrontSuspension",
            tMin = 16.0f, tMax = 18.0f, boltScale = 1.4f, specPart = "로어 암 서브 프레임 측 볼트·너트", specSource = "서스펜션 시스템 — 텐션 암·레터럴 암 (서브 프레임 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "FrontLowerArmR", name = "프런트 로어 암(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 서브 프레임 쪽 볼트·너트(16~18)를 푼다 — 모델에서는 텐션 암·레터럴 암이 한 판이라 한 부위로 다룬다",
            objects = new[] { "SM_FrontLowerArm_R" },
            bolts = new[] { new F(0.1086f, -0.1397f, 0.4000f, 0f, -1f, 0f), new F(0.2186f, -0.1335f, 0.4000f, 0f, -1f, 0f) },
            parent = "FrontSuspension",
            tMin = 16.0f, tMax = 18.0f, boltScale = 1.4f, specPart = "로어 암 서브 프레임 측 볼트·너트", specSource = "서스펜션 시스템 — 텐션 암·레터럴 암 (서브 프레임 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "FrontStrutL", name = "프런트 스트럿(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 상단 너트(5.5~7.0)와 너클 볼트(18~20)·스태빌라이저 링크 너트(10~12)를 푼다 — 스트럿은 인슐레이터를 단 채 분리된다. 바닥에서 록 너트(12~13, 재사용 금지, 특수공구 0K546-F6100)로 인슐레이터를 푼다 (코일 스프링은 압축 상태 — 스프링 압축기·전용 공구 없이 분해 금지)",
            gen = new[] {
                G.Cyl(-1.75f, 0.62f, -0.60f, 0.075f, 0.50f, Alu, 0.7f), G.Cyl(-1.75f, 0.70f, -0.60f, 0.12f, 0.24f, Plastic, 0f),
                G.Box(-1.75f, 0.875f, -0.60f, 0.12f, 0.02f, 0.12f, DarkAlu, 0.6f) },
            bolts = new[] { new F(-1.80f, 0.886f, -0.60f, 0f, 1f, 0f), new F(-1.70f, 0.886f, -0.60f, 0f, 1f, 0f) },
           
            parent = "FrontSuspension",
            tMin = 5.5f, tMax = 7.0f, specPart = "스트럿 상단(어퍼 마운트) 너트", specSource = "서스펜션 시스템 — 체결 토크 — 프런트 스트럿 어셈블리" },
        new PartDef {
            id = "FrontStrutR", name = "프런트 스트럿(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "서브 프레임 어셈블리를 내려놓은 뒤 그 위에서 상단 너트(5.5~7.0)와 너클 볼트(18~20)·스태빌라이저 링크 너트(10~12)를 푼다 — 스트럿은 인슐레이터를 단 채 분리된다. 바닥에서 록 너트(12~13, 재사용 금지, 특수공구 0K546-F6100)로 인슐레이터를 푼다 (코일 스프링은 압축 상태 — 스프링 압축기·전용 공구 없이 분해 금지)",
            gen = new[] {
                G.Cyl(-1.75f, 0.62f, 0.60f, 0.075f, 0.50f, Alu, 0.7f), G.Cyl(-1.75f, 0.70f, 0.60f, 0.12f, 0.24f, Plastic, 0f),
                G.Box(-1.75f, 0.875f, 0.60f, 0.12f, 0.02f, 0.12f, DarkAlu, 0.6f) },
            bolts = new[] { new F(-1.80f, 0.886f, 0.60f, 0f, 1f, 0f), new F(-1.70f, 0.886f, 0.60f, 0f, 1f, 0f) },
           
            parent = "FrontSuspension",
            tMin = 5.5f, tMax = 7.0f, specPart = "스트럿 상단(어퍼 마운트) 너트", specSource = "서스펜션 시스템 — 체결 토크 — 프런트 스트럿 어셈블리" },
        new PartDef {
            id = "StrutInsulatorL", name = "스트럿 인슐레이터·록 너트(왼쪽)", parent = "FrontStrutL",
            tool = "스트럿 너트 공구(0K546-F6100) · 에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "스프링 압축기로 코일 스프링을 압축한 뒤(도장 보호용 호스를 덧댄다) 인슐레이터 캡을 떼고 특수공구 0K546-F6100으로 록 너트(12~13)를 푼다 — 록 너트는 재사용 금지, 코일 스프링은 압축 상태라 전용 공구 없이 분해 금지",
            gen = new[] { G.Cyl(-1.75f, 0.91f, -0.60f, 0.06f, 0.05f, Plastic, 0f) },
            bolts = new[] { new F(-1.75f, 0.94f, -0.60f, 0f, 1f, 0f) },
            tMin = 12.0f, tMax = 13.0f, boltScale = 0.9f, specPart = "스트럿 록 너트(재사용 금지)", specSource = "서스펜션 시스템 — 프런트 스트럿 어셈블리(록 너트 12.0~13.0)" },
        new PartDef {
            id = "StrutInsulatorR", name = "스트럿 인슐레이터·록 너트(오른쪽)", parent = "FrontStrutR",
            tool = "스트럿 너트 공구(0K546-F6100) · 에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "스프링 압축기로 코일 스프링을 압축한 뒤(도장 보호용 호스를 덧댄다) 인슐레이터 캡을 떼고 특수공구 0K546-F6100으로 록 너트(12~13)를 푼다 — 록 너트는 재사용 금지, 코일 스프링은 압축 상태라 전용 공구 없이 분해 금지",
            gen = new[] { G.Cyl(-1.75f, 0.91f, 0.60f, 0.06f, 0.05f, Plastic, 0f) },
            bolts = new[] { new F(-1.75f, 0.94f, 0.60f, 0f, 1f, 0f) },
            tMin = 12.0f, tMax = 13.0f, boltScale = 0.9f, specPart = "스트럿 록 너트(재사용 금지)", specSource = "서스펜션 시스템 — 프런트 스트럿 어셈블리(록 너트 12.0~13.0)" },
        new PartDef {
            id = "RearStabilizer", name = "리어 스태빌라이저 바",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 그 위에서 와이어링 브래킷(2.0~2.4)과 스태빌라이저 바 고정 볼트(5.0~6.5)를 푼다",
            gen = new[] {
                G.Cyl(1.48f, 0.34f, 0f, 0.02f, 1.10f, DarkAlu, 0.7f, 90f, 0f, 0f),
                G.Box(1.48f, 0.34f, -0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f), G.Box(1.48f, 0.34f, 0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f) },
            bolts = new[] { new F(1.48f, 0.315f, -0.30f, 0f, -1f, 0f), new F(1.48f, 0.315f, 0.30f, 0f, -1f, 0f) },
            parent = "RearSuspension",
            tMin = 5.0f, tMax = 6.5f, specPart = "리어 스태빌라이저 바 볼트", specSource = "서스펜션 시스템 — 체결 토크 — 리어 스태빌라이저 바" },

        // ══════════════════════════════════════════════════════════════
        //  11/17 추가 — 차체 외판(SM_Ioniq_Electric 한 덩어리)을 정비지침서 구성부품 경계로 잘라 낸 부품 (Editor/CarBodyPanelSplitter)
        //  노드 변환이 원본 차체/도어와 같으므로 볼트 좌표도 그 로컬 기준. 토크는 바디 — 체결 토크표.
        // ══════════════════════════════════════════════════════════════
        new PartDef {
            id = "FenderFL", name = "프런트 펜더(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 헤드 램프 → 프런트 범퍼(마운팅 브라켓) → 앞 왼쪽 휠을 떼고 휠 가드·펜더 인슐레이터를 분리한 뒤, 후드 쪽 펜더 볼트(0.8~1.2)와 도어 쪽 펜더 너트(0.8~1.1)를 푼다",
            objects = new[] { "SM_FenderFL" },
            bolts = new[] {
                new F(-0.9202f, 1.8613f, -0.5993f, 0f, 0f, -1f), new F(-0.7091f, 1.5002f, -0.8227f, 0f, 0f, -1f),
                new F(-0.7131f, 1.1924f, -0.9122f, 0f, 0f, -1f), new F(-0.9572f, 1.0695f, -0.3512f, 0f, -1f, 0f) },
            requires = new[] { "FrontBumper", "Headlamp" }, needsWheel = 0,
            tMin = 0.8f, tMax = 1.2f, specPart = "펜더 볼트", specSource = "바디 — 체결 토크(펜더 볼트 0.8~1.2, 도어 측 펜더 너트 0.8~1.1) · 펜더 탈거 절차" },
        new PartDef {
            id = "FenderFR", name = "프런트 펜더(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 헤드 램프 → 프런트 범퍼(마운팅 브라켓) → 앞 오른쪽 휠을 떼고 휠 가드·펜더 인슐레이터를 분리한 뒤, 후드 쪽 펜더 볼트(0.8~1.2)와 도어 쪽 펜더 너트(0.8~1.1)를 푼다",
            objects = new[] { "SM_FenderFR" },
            bolts = new[] {
                new F(0.9202f, 1.8613f, -0.5993f, 0f, 0f, -1f), new F(0.7091f, 1.5002f, -0.8227f, 0f, 0f, -1f),
                new F(0.7131f, 1.1924f, -0.9122f, 0f, 0f, -1f), new F(0.9572f, 1.0695f, -0.3512f, 0f, -1f, 0f) },
            requires = new[] { "FrontBumper", "Headlamp" }, needsWheel = 1,
            tMin = 0.8f, tMax = 1.2f, specPart = "펜더 볼트", specSource = "바디 — 체결 토크(펜더 볼트 0.8~1.2, 도어 측 펜더 너트 0.8~1.1) · 펜더 탈거 절차" },
        new PartDef {
            id = "SideSillL", name = "사이드 실 몰딩(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "펜더(펜더 가니쉬)를 먼저 떼고 도어를 연 상태에서 아래쪽 스크류를 풀고 클립을 빼 떼어 낸다 (손상된 클립은 교환)",
            objects = new[] { "SM_SideSillL" },
            bolts = new[] { new F(-0.9155f, 0.6804f, -0.0786f, -1f, 0f, 0f), new F(-0.9093f, -0.2691f, -0.0799f, -1f, 0f, 0f), new F(-0.8772f, -1.0924f, -0.0487f, -1f, 0f, 0f) },
            requires = new[] { "FenderFL" },
            tMin = 1.0f, tMax = 1.4f, specPart = "사이드 실 몰딩 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "SideSillR", name = "사이드 실 몰딩(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "펜더(펜더 가니쉬)를 먼저 떼고 도어를 연 상태에서 아래쪽 스크류를 풀고 클립을 빼 떼어 낸다 (손상된 클립은 교환)",
            objects = new[] { "SM_SideSillR" },
            bolts = new[] { new F(0.9155f, 0.6804f, -0.0786f, 1f, 0f, 0f), new F(0.9093f, -0.2691f, -0.0799f, 1f, 0f, 0f), new F(0.8772f, -1.0924f, -0.0487f, 1f, 0f, 0f) },
            requires = new[] { "FenderFR" },
            tMin = 1.0f, tMax = 1.4f, specPart = "사이드 실 몰딩 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "MirrorL", name = "아웃사이드 미러(왼쪽)", parent = "DoorFL",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "실차는 도어를 단 채 12V (−) → 프런트 도어 트림 → 미러 커넥터 분리 → 장착 너트(0.7~1.1)를 푼다. 실습에선 떼어 낸 도어를 내려놓고 도어 안쪽에서 너트를 푼다",
            objects = new[] { "SM_MirrorL" },
            bolts = new[] { new F(-0.0229f, -0.2235f, -0.4441f, 1f, 0f, 0f), new F(-0.0229f, -0.2435f, -0.3841f, 1f, 0f, 0f), new F(-0.0229f, -0.2035f, -0.3841f, 1f, 0f, 0f) },
            tMin = 0.7f, tMax = 1.1f, specPart = "아웃사이드 미러 장착 너트", specSource = "바디 — 체결 토크(아웃사이드 미러 너트 0.7~1.1)" },
        new PartDef {
            id = "MirrorR", name = "아웃사이드 미러(오른쪽)", parent = "DoorFR",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "실차는 도어를 단 채 12V (−) → 프런트 도어 트림 → 미러 커넥터 분리 → 장착 너트(0.7~1.1)를 푼다. 실습에선 떼어 낸 도어를 내려놓고 도어 안쪽에서 너트를 푼다",
            objects = new[] { "SM_MirrorR" },
            bolts = new[] { new F(0.7762f, 0.7544f, -0.2133f, -1f, 0f, 0f), new F(0.7762f, 0.6944f, -0.2333f, -1f, 0f, 0f), new F(0.7762f, 0.6944f, -0.1933f, -1f, 0f, 0f) },
            tMin = 0.7f, tMax = 1.1f, specPart = "아웃사이드 미러 장착 너트", specSource = "바디 — 체결 토크(아웃사이드 미러 너트 0.7~1.1)" },
        new PartDef {
            id = "RearSpoiler", name = "리어 스포일러", parent = "Tailgate",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "테일게이트 사이드 트림·플러그 홀을 열고 보조 정지등 커넥터를 분리한 뒤 안쪽 너트(0.5~0.8)를 풀어 스포일러를 떼어 낸다",
            objects = new[] { "SM_RearSpoiler" },
            bolts = new[] { new F(-0.3172f, -1.5316f, -1.3087f, 0f, 0f, 1f), new F(0.3172f, -1.5316f, -1.3087f, 0f, 0f, 1f) },
            tMin = 0.5f, tMax = 0.8f, specPart = "리어 스포일러 너트", specSource = "바디 — 체결 토크(리어 스포일러 너트 0.5~0.8)" },
        new PartDef {
            id = "WiperFront", name = "프런트 와이퍼 암",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "와이퍼 암 너트 캡을 열고 너트(2.3~2.7)를 풀어 암을 뺀다. 카울 탑 커버·와이퍼 모터&링크(0.7~1.1) 작업의 선행 단계",
            objects = new[] { "SM_WiperFront" },
            bolts = new[] { new F(-0.30f, 1.307f, -0.94f, 0f, 0f, -1f), new F(0.30f, 1.307f, -0.94f, 0f, 0f, -1f) },
            tMin = 2.3f, tMax = 2.7f, specPart = "프런트 와이퍼 암 너트", specSource = "바디 — 체결 토크(와이퍼 암 앞 2.3~2.7)" },

        // ══════════════════════════════════════════════════════════════
        //  11/17 추가(2) — SM_Ioniq_Electric 에 남아 있던 내장·구조물을 정비지침서 구성부품으로 나눈 부품 (Editor/CarShellSplitter).
        //  내장 트림(필라·도어 스커프)·플로어 콘솔·플로어 카펫·크래쉬 패드 카울 탑 커버·비전 루프·인사이드 미러. 남은 구조물은 SM_BodyShell(차체 골격, 탈거 불가).
        //  볼트 좌표는 각 부품 노드(SM_Ioniq_Electric 과 같은 변환) 로컬 기준 — 부품 메시에 레이캐스트로 잰 값. 토크: 플로어 콘솔 = 탈거 및 장착 6단계, 비전 루프 = 체결 토크표, 나머지 클립·스크류 = 일반 조임 토크 M6.
        // ══════════════════════════════════════════════════════════════
        new PartDef {
            id = "FloorConsole", name = "플로어 콘솔 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 플로어 콘솔 어퍼 커버 → 암레스트 어셈블리를 떼고 콘솔 어셈블리 커넥터를 분리한 뒤, 클립으로 사이드 커버를 빼고 볼트(0.4~0.6)를 풀어 들어낸다 (손상 방지: 장갑·트림 리무버 사용)",
            objects = new[] { "SM_FloorConsole" },
            bolts = new[] {
                new F(-0.0797f, 0.6298f, -0.5956f, 0f, 0f, -1f), new F(0.0703f, 0.6298f, -0.5807f, 0f, 0f, -1f),
                new F(-0.1032f, 0.1798f, -0.4191f, -1f, 0f, 0f), new F(0.1032f, 0.1798f, -0.4191f, 1f, 0f, 0f) },
            tMin = 0.4f, tMax = 0.6f, specPart = "플로어 콘솔 어셈블리 장착 볼트", specSource = "바디 — 플로어 콘솔 어셈블리 탈거 및 장착(체결토크 0.4~0.6)" },
        new PartDef {
            id = "DoorScuffL", name = "도어 스커프 트림(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 스커프 트림의 클립·스크류를 풀어 문턱 위 트림을 떼어 낸다 (플로어 카펫·필라 트림 작업의 선행 단계)",
            objects = new[] { "SM_DoorScuffL" },
            bolts = new[] { new F(-0.8647f, 0.2298f, -0.1380f, 0f, 0f, -1f), new F(-0.8647f, -0.6702f, -0.1427f, 0f, 0f, -1f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "도어 스커프 트림 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "DoorScuffR", name = "도어 스커프 트림(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 스커프 트림의 클립·스크류를 풀어 문턱 위 트림을 떼어 낸다 (플로어 카펫·필라 트림 작업의 선행 단계)",
            objects = new[] { "SM_DoorScuffR" },
            bolts = new[] { new F(0.8553f, 0.2298f, -0.1436f, 0f, 0f, -1f), new F(0.8553f, -0.6702f, -0.1472f, 0f, 0f, -1f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "도어 스커프 트림 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "FloorCarpet", name = "플로어 카펫 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "앞·뒤 시트, 플로어 콘솔, 양쪽 도어 스커프 트림을 먼저 뗀 뒤 카펫 고정 스크류·클립을 풀고 앞에서 뒤로 걷어 낸다",
            objects = new[] { "SM_FloorCarpet" },
            bolts = new[] {
                new F(-0.4547f, 0.4298f, -0.2447f, 0f, 0f, -1f), new F(0.4453f, 0.4298f, -0.2447f, 0f, 0f, -1f),
                new F(-0.4547f, -0.6202f, -0.3102f, 0f, 0f, -1f), new F(0.4453f, -0.6202f, -0.3102f, 0f, 0f, -1f) },
            requires = new[] { "FloorConsole", "SeatFL", "SeatFR", "SeatRear", "DoorScuffL", "DoorScuffR" },
            tMin = 1.0f, tMax = 1.4f, specPart = "플로어 카펫 고정 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "PillarTrimFrontL", name = "프런트 필라 트림(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 A 필라 트림의 클립·스크류를 풀어 당겨 뺀다 (커튼 에어백 쪽 클립은 손상 주의)",
            objects = new[] { "SM_PillarTrimFrontL" },
            bolts = new[] { new F(-0.8541f, 0.9798f, -0.3891f, 1f, 0f, 0f), new F(-0.8172f, 0.9798f, -0.7891f, 1f, 0f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "프런트 필라 트림 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "PillarTrimFrontR", name = "프런트 필라 트림(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 A 필라 트림의 클립·스크류를 풀어 당겨 뺀다 (커튼 에어백 쪽 클립은 손상 주의)",
            objects = new[] { "SM_PillarTrimFrontR" },
            bolts = new[] { new F(0.8541f, 0.9798f, -0.3891f, -1f, 0f, 0f), new F(0.8172f, 0.9798f, -0.7891f, -1f, 0f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "프런트 필라 트림 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "CowlTopCover", name = "카울 탑 커버",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "프런트 와이퍼 암을 먼저 떼고 카울 탑 커버 클립·스크류를 풀어 윈드쉴드 아래 커버를 들어낸다 (와이퍼 모터&링크 작업의 선행 단계)",
            objects = new[] { "SM_CowlTopCover" },
            bolts = new[] { new F(-0.4047f, 1.2498f, -0.9077f, 0f, 0f, -1f), new F(0.3953f, 1.2498f, -0.9092f, 0f, 0f, -1f) },
            requires = new[] { "WiperFront" },
            tMin = 1.0f, tMax = 1.4f, specPart = "카울 탑 커버 스크류(M6)", specSource = SrcM6 },
        new PartDef {
            id = "VisionRoof", name = "비전 루프 어셈블리(글라스·롤러 블라인드)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "루프 트림 쪽에서 비전 루프 모터 커넥터를 분리하고 롤러 블라인드·메커니즘 레일을 정리한 뒤 고정 너트·볼트(0.8~1.2)를 풀어 어셈블리를 들어낸다 (2인 이상 작업, 글라스 파손 주의)",
            objects = new[] { "SM_VisionRoof" },
            bolts = new[] {
                new F(-0.4547f, 0.1798f, -1.4068f, 0f, 0f, -1f), new F(0.4453f, 0.1798f, -1.4081f, 0f, 0f, -1f),
                new F(-0.4547f, -0.9202f, -1.4332f, 0f, 0f, -1f), new F(0.4453f, -0.9202f, -1.4341f, 0f, 0f, -1f) },
            tMin = 0.8f, tMax = 1.2f, specPart = "비전 루프 고정 너트·볼트", specSource = "바디 — 체결 토크(비전루프 고정 너트 0.8~1.2, 고정 볼트 0.8~1.2)" },
        new PartDef {
            id = "InsideMirror", name = "인사이드 미러 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "미러 커넥터를 분리하고 윈드쉴드 마운트 커버를 열어 고정 스크류를 풀고 미러를 위로 밀어 뺀다",
            objects = new[] { "SM_InsideMirror" },
            bolts = new[] { new F(-0.1047f, 0.5098f, -1.2080f, 0f, 0f, 1f), new F(0.0953f, 0.5098f, -1.2063f, 0f, 0f, 1f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "인사이드 미러 고정 스크류(M6)", specSource = SrcM6 },

        // ══════════════════════════════════════════════════════════════
        //  11/17 추가(3) — 정비지침서 전체(냉각·구동·서스펜션·브레이크·스티어링·공조·고전압 충전)의 구성부품 표와 체결 토크표를 대조해 빠진 부품 38개를 보탬.
        //  이 모델에 메시가 없는 부품은 생성 파츠(상자·원통, 좌표 = 차 루트)로 만들었고(프렁크·언더 커버 밑이라 바깥 부품을 뗄 때까지 안 보인다), 토크·선행 작업은 해당 탈거 및 장착 문서를 따랐다.
        //  브레이크 라인만 차 모델의 SM_BrakeLines 노드를 쓴다(볼트 좌표 = 그 노드 로컬).
        // ══════════════════════════════════════════════════════════════
        new PartDef {
            id = "ThreeWayValveMotor", name = "3웨이 밸브(모터)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 냉각수 배출 → 3웨이 밸브 호스·커넥터를 분리하고 장착 너트(0.9~1.4)를 풀어 탈거한다. 장착 뒤 전장 및 모터 냉각수를 주입하고 진단 장비로 공기빼기를 한다",
            gen = new[] {
                G.Box(-2.02f, 0.6f, 0.33f, 0.07f, 0.09f, 0.07f, Plastic, 0f),
                G.Cyl(-2.02f, 0.66f, 0.33f, 0.03f, 0.04f, DarkAlu, 0.7f),
            },
            bolts = new[] { new F(-2.045f, 0.648f, 0.33f, 0f, 1f, 0f), new F(-1.995f, 0.648f, 0.33f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 0.9f, tMax = 1.4f, specPart = "3웨이 밸브 장착 너트", specSource = "냉각시스템 — 3웨이 밸브 탈거 및 장착(0.9~1.4)" },
        new PartDef {
            id = "ThreeWayValveBattery", name = "3웨이 밸브(배터리)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 액티브 사운드 스피커 → 프런트 언더 커버를 떼고 고전압 배터리 냉각수를 배출한 뒤, 밸브 아웃렛·인렛 호스와 커넥터를 분리하고 장착 너트(0.9~1.4)를 푼다. 장착 뒤 진단 기기로 전자식 워터 펌프를 강제 구동해 공기를 뺀다",
            gen = new[] {
                G.Box(-2.02f, 0.6f, -0.33f, 0.07f, 0.09f, 0.07f, Plastic, 0f),
                G.Cyl(-2.02f, 0.66f, -0.33f, 0.03f, 0.04f, DarkAlu, 0.7f),
            },
            bolts = new[] { new F(-2.045f, 0.648f, -0.33f, 0f, 1f, 0f), new F(-1.995f, 0.648f, -0.33f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 0.9f, tMax = 1.4f, specPart = "3웨이 밸브 장착 너트", specSource = "냉각시스템 — 3웨이 밸브 탈거 및 장착(0.9~1.4)" },
        new PartDef {
            id = "PtcHeatPump", name = "배터리 PTC 히트 펌프",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)", hv = true,
            hint = "고전압 차단 → 액티브 사운드 스피커·프런트 언더 커버를 떼고 배터리 라디에이터 드레인 플러그로 냉각수를 배출한다. 프런트 고전압 정션 박스에서 PTC 커넥터를 분리하고 냉각수 인렛·아웃렛 호스, 블로워 유닛, 접지 케이블(1.1~1.4)을 분리한 뒤 장착 볼트(0.8~1.2)를 푼다",
            gen = new[] {
                G.Box(-1.5f, 0.78f, -0.28f, 0.18f, 0.1f, 0.14f, DarkAlu, 0.5f),
                G.Box(-1.5f, 0.84f, -0.28f, 0.06f, 0.02f, 0.06f, HvOrange, 0f),
            },
            bolts = new[] { new F(-1.57f, 0.833f, -0.34f, 0f, 1f, 0f), new F(-1.43f, 0.833f, -0.22f, 0f, 1f, 0f), new F(-1.57f, 0.833f, -0.22f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 0.8f, tMax = 1.2f, specPart = "PTC 히트 펌프 장착 볼트", specSource = "냉각시스템 — PTC 히트 펌프 탈거 및 장착(접지 케이블 1.1~1.4, 장착 0.8~1.2)" },
        new PartDef {
            id = "CoolantManifold", name = "냉각수 분배 파이프",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "냉각수를 배출한 뒤 분배 파이프에 연결된 호스·커넥터를 분리하고 장착 볼트(2.0~2.4)를 풀어 탈거한다",
            gen = new[] {
                G.Cyl(-2.2f, 0.3f, 0.0f, 0.035f, 0.6f, DarkAlu, 0.7f, 90f, 0f, 0f),
                G.Box(-2.2f, 0.3f, -0.31f, 0.05f, 0.05f, 0.05f, Plastic, 0f),
                G.Box(-2.2f, 0.3f, 0.31f, 0.05f, 0.05f, 0.05f, Plastic, 0f),
            },
            bolts = new[] { new F(-2.2f, 0.328f, -0.31f, 0f, 1f, 0f), new F(-2.2f, 0.328f, 0.31f, 0f, 1f, 0f) },
            requires = new[] { "FrontUnderCover" },
            tMin = 2.0f, tMax = 2.4f, specPart = "냉각수 분배 파이프 장착 볼트", specSource = "냉각시스템 — 냉각수 분배 파이프 탈거 및 장착(2.0~2.4)" },
        new PartDef {
            id = "EwpMotor", name = "전자식 워터 펌프 EWP(모터)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "냉각수를 배출하고 커넥터·냉각수 호스를 분리한 뒤 브래킷 볼트(1.8~2.2)로 모터 냉각용 워터 펌프를 탈거한다 (호스 클램프 0.20~0.25)",
            gen = new[] {
                G.Cyl(-2.12f, 0.38f, 0.22f, 0.1f, 0.12f, DarkAlu, 0.6f),
                G.Cyl(-2.12f, 0.45f, 0.22f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(-2.1550000000000002f, 0.443f, 0.22f, 0f, 1f, 0f), new F(-2.085f, 0.443f, 0.22f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 1.8f, tMax = 2.2f, specPart = "EWP 브래킷 볼트", specSource = "냉각시스템 — 전자식 워터 펌프(EWP) 탈거 및 장착(브래킷 1.8~2.2)" },
        new PartDef {
            id = "EwpBattery1", name = "전자식 워터 펌프 EWP(배터리 #1)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 배터리 냉각수를 배출하고 커넥터·호스를 분리한 뒤 브래킷 볼트(1.8~2.2)로 배터리 측 워터 펌프를 탈거한다",
            gen = new[] {
                G.Cyl(-2.12f, 0.38f, -0.22f, 0.1f, 0.12f, DarkAlu, 0.6f),
                G.Cyl(-2.12f, 0.45f, -0.22f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(-2.1550000000000002f, 0.443f, -0.22f, 0f, 1f, 0f), new F(-2.085f, 0.443f, -0.22f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 1.8f, tMax = 2.2f, specPart = "EWP 브래킷 볼트", specSource = "냉각시스템 — 전자식 워터 펌프(EWP) 탈거 및 장착(브래킷 1.8~2.2)" },
        new PartDef {
            id = "EwpBattery2", name = "전자식 워터 펌프 EWP(배터리 #2·리저버 측)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "냉각수를 배출하고 리저버 쪽 EWP 커넥터·호스를 분리한 뒤 브래킷 볼트(1.8~2.2)를 풀어 탈거한다",
            gen = new[] {
                G.Cyl(-1.78f, 0.78f, 0.3f, 0.09f, 0.1f, DarkAlu, 0.6f),
                G.Cyl(-1.78f, 0.84f, 0.3f, 0.045f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(-1.815f, 0.833f, 0.3f, 0f, 1f, 0f), new F(-1.745f, 0.833f, 0.3f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 1.8f, tMax = 2.2f, specPart = "EWP 브래킷 볼트", specSource = "냉각시스템 — 전자식 워터 펌프(EWP) 탈거 및 장착(브래킷 1.8~2.2)" },
        new PartDef {
            id = "MotorOilCoolerFront", name = "모터 감속기 오일 쿨러(전륜)", parent = "FrontMotor",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "프런트 모터·감속기에 붙은 오일 쿨러의 냉각수 호스를 분리하고 장착 볼트(0.7~1.0)를 풀어 탈거한다 (모터·감속기를 차에서 내린 뒤 작업)",
            gen = new[] {
                G.Box(-1.78f, 0.36f, 0.2f, 0.12f, 0.07f, 0.12f, Alu, 0.6f),
            },
            bolts = new[] { new F(-1.81f, 0.398f, 0.2f, 0f, 1f, 0f), new F(-1.75f, 0.398f, 0.2f, 0f, 1f, 0f) },
            tMin = 0.7f, tMax = 1.0f, specPart = "오일 쿨러 장착 볼트", specSource = "냉각시스템 — 모터 감속기 오일 쿨러 탈거 및 장착(0.7~1.0)" },
        new PartDef {
            id = "MotorOilCoolerRear", name = "모터 감속기 오일 쿨러(후륜)", parent = "RearMotor",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "리어 모터·감속기(IDA)에 붙은 오일 쿨러의 냉각수 호스를 분리하고 장착 볼트(0.7~1.0)를 풀어 탈거한다 (IDA를 크로스 멤버에서 내린 뒤 작업)",
            gen = new[] {
                G.Box(1.22f, 0.34f, 0.25f, 0.12f, 0.07f, 0.12f, Alu, 0.6f),
            },
            bolts = new[] { new F(1.19f, 0.378f, 0.25f, 0f, 1f, 0f), new F(1.25f, 0.378f, 0.25f, 0f, 1f, 0f) },
            tMin = 0.7f, tMax = 1.0f, specPart = "오일 쿨러 장착 볼트", specSource = "냉각시스템 — 모터 감속기 오일 쿨러 탈거 및 장착(0.7~1.0)" },
        new PartDef {
            id = "ActiveAirFlap", name = "액티브 에어 플랩(AAF)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "프런트 범퍼를 떼고 AAF 커넥터를 분리한 뒤 장착 볼트(0.8~1.0)를 풀어 탈거한다 (냉각 모듈 앞쪽 그릴 안에 있다)",
            gen = new[] {
                G.Box(-2.515f, 0.64f, 0.0f, 0.015f, 0.1f, 0.8f, Plastic, 0f),
                G.Box(-2.52f, 0.64f, 0.0f, 0.01f, 0.08f, 0.76f, Fin, 0.2f),
            },
            bolts = new[] { new F(-2.515f, 0.693f, -0.3f, 0f, 1f, 0f), new F(-2.515f, 0.693f, 0.3f, 0f, 1f, 0f) },
            requires = new[] { "FrontBumper" },
            tMin = 0.8f, tMax = 1.0f, specPart = "AAF 장착 볼트", specSource = "냉각시스템 — 액티브 에어 플랩(AAF) 탈거 및 장착(0.8~1.0)" },
        new PartDef {
            id = "WaterCondenser", name = "수냉 콘덴서",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단·냉매 회수 후 냉각수를 배출하고 수냉 콘덴서의 냉매 튜브(0.9~1.4)와 냉각수 호스를 분리해 장착 볼트를 풀어 탈거한다. 냉매 라인은 분리 즉시 캡을 씌운다",
            gen = new[] {
                G.Box(-1.7f, 0.4f, 0.32f, 0.12f, 0.1f, 0.14f, Alu, 0.7f),
            },
            bolts = new[] { new F(-1.73f, 0.453f, 0.32f, 0f, 1f, 0f), new F(-1.67f, 0.453f, 0.32f, 0f, 1f, 0f) },
            requires = new[] { "Frunk", "FrontUnderCover" },
            tMin = 0.9f, tMax = 1.4f, specPart = "수냉 콘덴서 튜브·장착 볼트", specSource = "히터 및 에어컨 장치 — 체결토크(칠러·팽창 밸브 0.9~1.4)" },
        new PartDef {
            id = "RefrigerantLines", name = "냉매 라인 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "냉매 회수기로 냉매를 회수한 뒤 콘덴서·컴프레서 쪽 호스 볼트(0.8~1.2)를 풀어 라인 어셈블리를 뗀다. 분리한 포트는 즉시 캡을 씌우고, 장착 후 진공·누설 검사를 한다",
            gen = new[] {
                G.Cyl(-2.22f, 0.5f, 0.2f, 0.02f, 0.5f, Alu, 0.8f, 90f, 0f, 0f),
                G.Cyl(-2.22f, 0.56f, 0.46f, 0.02f, 0.12f, Alu, 0.8f),
            },
            bolts = new[] { new F(-2.22f, 0.5f, -0.07f, 0f, 0f, -1f), new F(-2.22f, 0.5f, 0.47f, 0f, 0f, 1f) },
            requires = new[] { "AcCompressor" },
            tMin = 0.8f, tMax = 1.2f, specPart = "냉매 라인 호스 볼트", specSource = "히터 및 에어컨 장치 — 체결토크(콘덴서 디스차지 호스·리퀴드 튜브 0.8~1.2)" },
        new PartDef {
            id = "ComboChargeInlet", name = "콤보 충전 인렛 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)", hv = true,
            hint = "고전압 차단 → 러기지 사이드 트림 → 급속 충전 커넥터 서비스 커버(0.5~0.8) → 커넥터 분리(0.95~1.05) → 리어 언더 커버를 떼고 급속 충전 커넥터 장착 너트(0.5~0.8)를 푼다. ICCU AC 커넥터·VCMS 커넥터·접지 케이블·인렛 커넥터를 분리해 어셈블리를 뺀다",
            gen = new[] {
                G.Box(1.7f, 0.62f, 0.55f, 0.14f, 0.16f, 0.12f, DarkAlu, 0.5f),
                G.Cyl(1.7f, 0.62f, 0.62f, 0.05f, 0.03f, HvOrange, 0f, 90f, 0f, 0f),
            },
            bolts = new[] { new F(1.65f, 0.705f, 0.55f, 0f, 1f, 0f), new F(1.75f, 0.705f, 0.55f, 0f, 1f, 0f) },
            requires = new[] { "RearBumper", "RearUnderCover" },
            tMin = 0.5f, tMax = 0.8f, specPart = "콤보 충전 인렛 장착 너트", specSource = "배터리 제어 시스템 — 콤보 충전 인렛 어셈블리 탈거 및 장착(0.5~0.8)" },
        new PartDef {
            id = "FrontKnuckleL", name = "프런트 너클·허브(왼쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "스트럿·텐션 암·레터럴 암·타이로드 엔드를 분리하고(분할 핀 폐기) 허브 너트(18.0~20.0)와 스트럿 볼트·너트(18.0~20.0)를 풀어 너클을 뺀다 — 서브 프레임을 내려놓은 뒤 바닥에서 분해한다",
            gen = new[] {
                G.Box(-1.75f, 0.45f, -0.66f, 0.12f, 0.3f, 0.07f, DarkAlu, 0.6f),
                G.Cyl(-1.75f, 0.45f, -0.7050000000000001f, 0.1f, 0.05f, Alu, 0.7f, 90f, 0f, 0f),
            },
            bolts = new[] { new F(-1.8f, 0.56f, -0.698f, 0f, 0f, -1f), new F(-1.7f, 0.56f, -0.698f, 0f, 0f, -1f) },
            tMin = 18.0f, tMax = 20.0f, specPart = "프런트 너클 스트럿 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(프런트 스트럿과 프런트 너클 볼트 및 너트 18.0~20.0)" },
        new PartDef {
            id = "FrontDriveShaftL", name = "프런트 드라이브 샤프트 IDA(왼쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "서브 프레임을 내려놓은 뒤 감속기 쪽 조인트와 너클 쪽 볼트(13.0~15.0)를 풀어 샤프트를 감속기에서 빼낸다 — 인터미디어트 부트·씰을 손상시키지 않도록 주의",
            gen = new[] {
                G.Cyl(-1.88f, 0.42f, -0.43f, 0.04f, 0.4f, DarkAlu, 0.8f, 90f, 0f, 0f),
                G.Cyl(-1.88f, 0.42f, -0.62f, 0.07f, 0.06f, Plastic, 0f, 90f, 0f, 0f),
            },
            bolts = new[] { new F(-1.905f, 0.42f, -0.655f, 0f, 0f, -1f), new F(-1.855f, 0.42f, -0.655f, 0f, 0f, -1f) },
            tMin = 13.0f, tMax = 15.0f, specPart = "프런트 IDA·너클 볼트", specSource = "드라이브 샤프트 및 액슬 — 체결토크(프런트 IDA와 프런트 너클 볼트 13.0~15.0)" },
        new PartDef {
            id = "TensionArmL", name = "프런트 텐션 암(왼쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "서브 프레임을 내려놓은 뒤 너클 쪽 너트(10.0~12.0, 분할 핀 폐기·풀러 사용)와 서브 프레임 볼트·너트(16.0~18.0)를 풀어 텐션 암을 뺀다",
            gen = new[] {
                G.Box(-1.98f, 0.36f, -0.52f, 0.05f, 0.03f, 0.34f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(-1.98f, 0.378f, -0.37f, 0f, 1f, 0f), new F(-1.98f, 0.378f, -0.67f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "텐션 암 서브 프레임 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(텐션 암과 서브 프레임 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearUpperArmFrontL", name = "리어 어퍼 암 프런트(왼쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어퍼 암 프런트와 크로스 멤버·캐리어 볼트·너트(16.0~18.0)를 풀어 뺀다 (휠 속도 센서 브라켓 볼트 1.3~1.7 선분리)",
            gen = new[] {
                G.Box(1.1f, 0.56f, -0.52f, 0.04f, 0.03f, 0.36f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.1f, 0.578f, -0.36f, 0f, 1f, 0f), new F(1.1f, 0.578f, -0.68f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "리어 어퍼 암 프런트 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어퍼 암-프런트 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearUpperArmRearL", name = "리어 어퍼 암 리어(왼쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어퍼 암 리어와 크로스 멤버·캐리어 볼트·너트(16.0~18.0)를 풀어 뺀다",
            gen = new[] {
                G.Box(1.34f, 0.58f, -0.52f, 0.04f, 0.03f, 0.36f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.34f, 0.598f, -0.36f, 0f, 1f, 0f), new F(1.34f, 0.598f, -0.68f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "리어 어퍼 암 리어 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어퍼 암-리어 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearAssistArmL", name = "리어 어시스트 암(왼쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어시스트 암과 캐리어 볼트·너트(12.0~14.0), 크로스 멤버 볼트·너트(11.0~12.0)를 풀어 뺀다",
            gen = new[] {
                G.Box(1.36f, 0.5f, -0.52f, 0.04f, 0.025f, 0.32f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.36f, 0.5175f, -0.38f, 0f, 1f, 0f), new F(1.36f, 0.5175f, -0.66f, 0f, 1f, 0f) },
            tMin = 12.0f, tMax = 14.0f, specPart = "리어 어시스트 암 캐리어 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어시스트 암과 리어 캐리어 볼트 및 너트 12.0~14.0)" },
        new PartDef {
            id = "FrontKnuckleR", name = "프런트 너클·허브(오른쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "스트럿·텐션 암·레터럴 암·타이로드 엔드를 분리하고(분할 핀 폐기) 허브 너트(18.0~20.0)와 스트럿 볼트·너트(18.0~20.0)를 풀어 너클을 뺀다 — 서브 프레임을 내려놓은 뒤 바닥에서 분해한다",
            gen = new[] {
                G.Box(-1.75f, 0.45f, 0.66f, 0.12f, 0.3f, 0.07f, DarkAlu, 0.6f),
                G.Cyl(-1.75f, 0.45f, 0.7050000000000001f, 0.1f, 0.05f, Alu, 0.7f, 90f, 0f, 0f),
            },
            bolts = new[] { new F(-1.8f, 0.56f, 0.698f, 0f, 0f, 1f), new F(-1.7f, 0.56f, 0.698f, 0f, 0f, 1f) },
            tMin = 18.0f, tMax = 20.0f, specPart = "프런트 너클 스트럿 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(프런트 스트럿과 프런트 너클 볼트 및 너트 18.0~20.0)" },
        new PartDef {
            id = "FrontDriveShaftR", name = "프런트 드라이브 샤프트 IDA(오른쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "서브 프레임을 내려놓은 뒤 감속기 쪽 조인트와 너클 쪽 볼트(13.0~15.0)를 풀어 샤프트를 감속기에서 빼낸다 — 인터미디어트 부트·씰을 손상시키지 않도록 주의",
            gen = new[] {
                G.Cyl(-1.88f, 0.42f, 0.43f, 0.04f, 0.4f, DarkAlu, 0.8f, 90f, 0f, 0f),
                G.Cyl(-1.88f, 0.42f, 0.62f, 0.07f, 0.06f, Plastic, 0f, 90f, 0f, 0f),
            },
            bolts = new[] { new F(-1.905f, 0.42f, 0.655f, 0f, 0f, 1f), new F(-1.855f, 0.42f, 0.655f, 0f, 0f, 1f) },
            tMin = 13.0f, tMax = 15.0f, specPart = "프런트 IDA·너클 볼트", specSource = "드라이브 샤프트 및 액슬 — 체결토크(프런트 IDA와 프런트 너클 볼트 13.0~15.0)" },
        new PartDef {
            id = "TensionArmR", name = "프런트 텐션 암(오른쪽)", parent = "FrontSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "서브 프레임을 내려놓은 뒤 너클 쪽 너트(10.0~12.0, 분할 핀 폐기·풀러 사용)와 서브 프레임 볼트·너트(16.0~18.0)를 풀어 텐션 암을 뺀다",
            gen = new[] {
                G.Box(-1.98f, 0.36f, 0.52f, 0.05f, 0.03f, 0.34f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(-1.98f, 0.378f, 0.37f, 0f, 1f, 0f), new F(-1.98f, 0.378f, 0.67f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "텐션 암 서브 프레임 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(텐션 암과 서브 프레임 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearUpperArmFrontR", name = "리어 어퍼 암 프런트(오른쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어퍼 암 프런트와 크로스 멤버·캐리어 볼트·너트(16.0~18.0)를 풀어 뺀다 (휠 속도 센서 브라켓 볼트 1.3~1.7 선분리)",
            gen = new[] {
                G.Box(1.1f, 0.56f, 0.52f, 0.04f, 0.03f, 0.36f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.1f, 0.578f, 0.36f, 0f, 1f, 0f), new F(1.1f, 0.578f, 0.68f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "리어 어퍼 암 프런트 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어퍼 암-프런트 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearUpperArmRearR", name = "리어 어퍼 암 리어(오른쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어퍼 암 리어와 크로스 멤버·캐리어 볼트·너트(16.0~18.0)를 풀어 뺀다",
            gen = new[] {
                G.Box(1.34f, 0.58f, 0.52f, 0.04f, 0.03f, 0.36f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.34f, 0.598f, 0.36f, 0f, 1f, 0f), new F(1.34f, 0.598f, 0.68f, 0f, 1f, 0f) },
            tMin = 16.0f, tMax = 18.0f, specPart = "리어 어퍼 암 리어 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어퍼 암-리어 볼트 및 너트 16.0~18.0)" },
        new PartDef {
            id = "RearAssistArmR", name = "리어 어시스트 암(오른쪽)", parent = "RearSuspension",
            tool = "에어 임팩렌치(27mm 소켓)", socket = 3,
            hint = "크로스 멤버 어셈블리를 내려놓은 뒤 어시스트 암과 캐리어 볼트·너트(12.0~14.0), 크로스 멤버 볼트·너트(11.0~12.0)를 풀어 뺀다",
            gen = new[] {
                G.Box(1.36f, 0.5f, 0.52f, 0.04f, 0.025f, 0.32f, DarkAlu, 0.6f),
            },
            bolts = new[] { new F(1.36f, 0.5175f, 0.38f, 0f, 1f, 0f), new F(1.36f, 0.5175f, 0.66f, 0f, 1f, 0f) },
            tMin = 12.0f, tMax = 14.0f, specPart = "리어 어시스트 암 캐리어 볼트·너트", specSource = "서스펜션 시스템 — 체결 토크(리어 어시스트 암과 리어 캐리어 볼트 및 너트 12.0~14.0)" },
        new PartDef {
            id = "EcsUnit", name = "전자 제어 서스펜션(ECS) 컨트롤 유닛",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 후 뒤 휠하우스 트림을 열고 ECS 유닛 커넥터를 분리한 뒤 컨트롤 유닛 너트(0.45~0.74)를 풀어 뺀다 (연속 가변 댐퍼는 쇽·스트럿에 포함)",
            gen = new[] {
                G.Box(1.52f, 0.72f, -0.6f, 0.08f, 0.04f, 0.1f, Plastic, 0f),
                G.Box(1.52f, 0.745f, -0.6f, 0.03f, 0.012f, 0.03f, Alu, 0.6f),
            },
            bolts = new[] { new F(1.495f, 0.744f, -0.6f, 0f, 1f, 0f), new F(1.545f, 0.744f, -0.6f, 0f, 1f, 0f) },
            requires = new[] { "RearBumper" },
            tMin = 0.45f, tMax = 0.74f, specPart = "ECS 컨트롤 유닛 너트", specSource = "서스펜션 시스템 — 체결 토크(ECS 컨트롤 유닛 너트 0.45~0.74)" },
        new PartDef {
            id = "Ieb", name = "통합형 전동 부스터(IEB)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 브레이크 튜브 플레어 너트(1.4~1.7)와 리저버 호스·커넥터를 분리하고 푸쉬 로드를 페달에서 뺀 뒤, 페달 멤버 쪽 장착 너트(1.7~2.6)를 풀어 부스터(ECU 포함)를 뺀다. 장착 후 브레이크 블리딩이 필요하다",
            gen = new[] {
                G.Box(-1.4f, 0.72f, 0.02f, 0.22f, 0.16f, 0.18f, DarkAlu, 0.6f),
                G.Cyl(-1.28f, 0.72f, 0.02f, 0.02f, 0.14f, Alu, 0.8f, 0f, 0f, 90f),
            },
            bolts = new[] { new F(-1.46f, 0.803f, -0.05f, 0f, 1f, 0f), new F(-1.34f, 0.803f, 0.09f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 1.7f, tMax = 2.6f, specPart = "IEB 장착 너트", specSource = "브레이크 시스템 — 체결토크(브레이크 페달 멤버 어셈블리 너트 1.7~2.6)" },
        new PartDef {
            id = "RemoteReservoir", name = "리모트 리저버 탱크",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "브레이크 오일 레벨 센서 커넥터와 리저버 호스를 분리하고 탱크 너트(0.7~1.1)를 풀어 뺀다 (오일이 도장면에 닿지 않게 주의)",
            gen = new[] {
                G.Cyl(-1.42f, 0.9f, 0.38f, 0.09f, 0.12f, Plastic, 0f),
                G.Cyl(-1.42f, 0.97f, 0.38f, 0.05f, 0.03f, Alu, 0.6f),
            },
            bolts = new[] { new F(-1.46f, 0.963f, 0.38f, 0f, 1f, 0f), new F(-1.38f, 0.963f, 0.38f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 0.7f, tMax = 1.1f, specPart = "리모트 리저버 탱크 너트", specSource = "브레이크 시스템 — 체결토크(리모트 리저버 탱크 너트 0.7~1.1)" },
        new PartDef {
            id = "BrakePedal", name = "브레이크 페달 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "크래시 패드를 뗀 뒤 스톱 램프 스위치·페달 스트로크 센서(0.2~0.4) 커넥터를 분리하고 페달 멤버 어셈블리 너트(1.7~2.6)를 풀어 뺀다 (페달 암과 멤버 볼트 2.5~3.5)",
            gen = new[] {
                G.Box(-1.18f, 0.58f, -0.32f, 0.08f, 0.22f, 0.1f, Plastic, 0f),
                G.Box(-1.14f, 0.5f, -0.32f, 0.04f, 0.1f, 0.08f, Fin, 0.1f),
            },
            bolts = new[] { new F(-1.2f, 0.703f, -0.37f, 0f, 1f, 0f), new F(-1.2f, 0.703f, -0.27f, 0f, 1f, 0f) },
            requires = new[] { "Cockpit" },
            tMin = 1.7f, tMax = 2.6f, specPart = "브레이크 페달 멤버 어셈블리 너트", specSource = "브레이크 시스템 — 체결토크(브레이크 페달 멤버 어셈블리 너트 1.7~2.6)" },
        new PartDef {
            id = "FrontWheelSpeedL", name = "앞 휠 속도 센서(왼쪽)", parent = "FrontSuspension",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "해당 휠을 떼고 센서 커넥터·와이어링 클립을 분리한 뒤 센서 볼트(0.8~1.2)를 풀어 뺀다 (센서 끝과 톤 휠에 이물이 묻지 않게 주의)",
            gen = new[] {
                G.Box(-1.66f, 0.56f, -0.63f, 0.03f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(-1.66f, 0.588f, -0.63f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, specPart = "앞 휠 속도 센서 볼트", specSource = "브레이크 시스템 — 체결토크(프런트 휠 속도 센서 볼트 0.8~1.2)" },
        new PartDef {
            id = "FrontWheelSpeedR", name = "앞 휠 속도 센서(오른쪽)", parent = "FrontSuspension",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "해당 휠을 떼고 센서 커넥터·와이어링 클립을 분리한 뒤 센서 볼트(0.8~1.2)를 풀어 뺀다 (센서 끝과 톤 휠에 이물이 묻지 않게 주의)",
            gen = new[] {
                G.Box(-1.66f, 0.56f, 0.63f, 0.03f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(-1.66f, 0.588f, 0.63f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.2f, specPart = "앞 휠 속도 센서 볼트", specSource = "브레이크 시스템 — 체결토크(프런트 휠 속도 센서 볼트 0.8~1.2)" },
        new PartDef {
            id = "RearWheelSpeedL", name = "뒤 휠 속도 센서(왼쪽)", parent = "RearAxleL",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "해당 휠을 떼고 센서 커넥터·와이어링 클립을 분리한 뒤 센서 볼트(0.9~1.4)를 풀어 뺀다 (센서 끝과 톤 휠에 이물이 묻지 않게 주의)",
            gen = new[] {
                G.Box(1.3f, 0.56f, -0.63f, 0.03f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(1.3f, 0.588f, -0.63f, 0f, 1f, 0f) },
            tMin = 0.9f, tMax = 1.4f, specPart = "뒤 휠 속도 센서 볼트", specSource = "브레이크 시스템 — 체결토크(리어 휠 속도 센서 볼트 0.9~1.4)" },
        new PartDef {
            id = "RearWheelSpeedR", name = "뒤 휠 속도 센서(오른쪽)", parent = "RearAxleR",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "해당 휠을 떼고 센서 커넥터·와이어링 클립을 분리한 뒤 센서 볼트(0.9~1.4)를 풀어 뺀다 (센서 끝과 톤 휠에 이물이 묻지 않게 주의)",
            gen = new[] {
                G.Box(1.3f, 0.56f, 0.63f, 0.03f, 0.05f, 0.03f, Plastic, 0f),
            },
            bolts = new[] { new F(1.3f, 0.588f, 0.63f, 0f, 1f, 0f) },
            tMin = 0.9f, tMax = 1.4f, specPart = "뒤 휠 속도 센서 볼트", specSource = "브레이크 시스템 — 체결토크(리어 휠 속도 센서 볼트 0.9~1.4)" },
        new PartDef {
            id = "BrakeLines", name = "브레이크 라인(튜브·호스)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "앞·뒤 캘리퍼 호스 볼트(2.5~3.5)와 IEB 쪽 플레어 너트(1.4~1.7)를 풀고 차체 브라켓 볼트(0.9~1.4)를 풀어 라인을 뗀다 — 오일이 흐르므로 받이를 대고, 장착 후 블리딩한다",
            objects = new[] { "SM_BrakeLines" },
            bolts = new[] { new F(-0.94f, -0.1449f, -0.0291f, 0f, -1f, 0f), new F(0.0348f, -0.1433f, -0.012f, 0f, -1f, 0f), new F(0.5509f, -0.1445f, 0.0187f, 0f, -1f, 0f), new F(1.2929f, -0.1508f, -0.0144f, 0f, -1f, 0f) },
            requires = new[] { "BrakeFL", "BrakeFR", "BrakeRL", "BrakeRR" },
            tMin = 0.9f, tMax = 1.4f, specPart = "브레이크 라인 브라켓 볼트(차체)", specSource = "브레이크 시스템 — 체결토크(브레이크 라인 브라켓과 차체 볼트 0.9~1.4)" },
        new PartDef {
            id = "EpbActuatorL", name = "EPB 액추에이터(왼쪽)", parent = "BrakeRL",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "리어 캘리퍼에서 EPB 액추에이터 커넥터를 분리하고 액추에이터 볼트(0.8~1.1)를 풀어 뺀다 — 교환 후 EPB 캘리브레이션(진단 장비)이 필요하다",
            gen = new[] {
                G.Box(1.24f, 0.52f, -0.7f, 0.06f, 0.08f, 0.07f, Plastic, 0f),
            },
            bolts = new[] { new F(1.22f, 0.563f, -0.7f, 0f, 1f, 0f), new F(1.26f, 0.563f, -0.7f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.1f, specPart = "EPB 액추에이터 볼트", specSource = "브레이크 시스템 — 체결토크(EPB 액추에이터 볼트 0.8~1.1)" },
        new PartDef {
            id = "EpbActuatorR", name = "EPB 액추에이터(오른쪽)", parent = "BrakeRR",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "리어 캘리퍼에서 EPB 액추에이터 커넥터를 분리하고 액추에이터 볼트(0.8~1.1)를 풀어 뺀다 — 교환 후 EPB 캘리브레이션(진단 장비)이 필요하다",
            gen = new[] {
                G.Box(1.24f, 0.52f, 0.7f, 0.06f, 0.08f, 0.07f, Plastic, 0f),
            },
            bolts = new[] { new F(1.22f, 0.563f, 0.7f, 0f, 1f, 0f), new F(1.26f, 0.563f, 0.7f, 0f, 1f, 0f) },
            tMin = 0.8f, tMax = 1.1f, specPart = "EPB 액추에이터 볼트", specSource = "브레이크 시스템 — 체결토크(EPB 액추에이터 볼트 0.8~1.1)" },
        new PartDef {
            id = "SteeringColumn", name = "스티어링 컬럼 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "크래시 패드(컬럼 쉬라우드 패널)를 떼고 스티어링 휠·클럭 스프링을 분리한 뒤, 컬럼 더스트 커버 너트(1.3~1.8)·컬럼 어셈블리 볼트(5.5~6.0)·너트(2.5~3.0)를 풀어 컬럼을 뺀다 (에어백 모듈은 정전기·단락에 주의)",
            gen = new[] {
                G.Cyl(-1.15f, 0.78f, -0.31f, 0.05f, 0.45f, DarkAlu, 0.7f, 0f, 0f, -50f),
                G.Box(-1.32f, 0.635f, -0.31f, 0.06f, 0.04f, 0.1f, Plastic, 0f),
            },
            bolts = new[] { new F(-1.335f, 0.655f, -0.345f, -0.77f, -0.64f, 0f), new F(-1.335f, 0.655f, -0.275f, -0.77f, -0.64f, 0f) },
            requires = new[] { "Cockpit" },
            tMin = 5.5f, tMax = 6.0f, specPart = "스티어링 컬럼 어셈블리 볼트", specSource = "스티어링 시스템 — 체결 토크(스티어링 컬럼 어셈블리 볼트 5.5~6.0)" },
        new PartDef {
            id = "UniversalJoint", name = "유니버설 조인트 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "컬럼을 뗀 뒤 컬럼 쪽 조인트 볼트와 요크(기어박스 쪽) 볼트(5.0~6.0)를 풀어 조인트를 뺀다 — 볼트는 재사용 금지, 장착 시 중립 위치를 맞춘다",
            gen = new[] {
                G.Cyl(-1.38f, 0.56f, -0.31f, 0.045f, 0.14f, DarkAlu, 0.8f, 0f, 0f, -30f),
                G.Box(-1.405f, 0.525f, -0.31f, 0.05f, 0.05f, 0.07f, Plastic, 0f),
            },
            bolts = new[] { new F(-1.43f, 0.555f, -0.345f, -0.5f, -0.86f, 0f), new F(-1.43f, 0.555f, -0.275f, -0.5f, -0.86f, 0f) },
            requires = new[] { "SteeringColumn" },
            tMin = 5.0f, tMax = 6.0f, specPart = "유니버설 조인트 볼트(재사용 금지)", specSource = "스티어링 시스템 — 체결 토크(스티어링 컬럼과 유니버설 조인트·요크 어셈블리 볼트 5.0~6.0)" },

        // 11/17(4) — 다른 차량 FBX(FBX_ElectricCar_Out)의 SM_Ioniq_ChargingPort 메시를 가져와 만든 충전 도어(왼쪽 뒤 쿼터). 볼트 좌표 = 그 노드 로컬(얇은 축 = x, 바깥 = −x).
        new PartDef {
            id = "ChargingDoor", name = "충전 도어 어셈블리",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 분리 → 러기지 사이드 트림을 열고 충전 도어 컨트롤 유닛·해제 액추에이터 커넥터를 분리한 뒤, 도어 어셈블리 고정 스크류를 풀어 바깥으로 뺀다 (도장면 보호: 트림 리무버 사용)",
            objects = new[] { "SM_ChargingPort" },
            bolts = new[] { new F(-0.056f, 0.06f, -0.08f, -1f, 0f, 0f), new F(-0.056f, 0.06f, 0.08f, -1f, 0f, 0f), new F(-0.056f, -0.06f, 0f, -1f, 0f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "충전 도어 고정 스크류(M6)", specSource = SrcM6 },
    };

    /// <summary>이 볼트가 속한 부위의 한글 이름(차체 부품 탈거·장착 볼트가 아니면 null).</summary>
    public static string NameOfBolt(Bolt b)
    {
        var s = Instance;
        if (s == null || s._parts == null || b == null) return null;
        foreach (var pt in s._parts)
            if (pt != null && pt.frame != null && b.transform.IsChildOf(pt.frame)) return pt.def.name;
        return null;
    }

    public static int PartCount => Defs.Length;
    public static int IndexOf(string id) { for (int i = 0; i < Defs.Length; i++) if (Defs[i].id == id) return i; return -1; }

    private static int[] s_off;   // 부위별 볼트 시작 번호
    private static int s_total;
    private static void EnsureOffsets()
    {
        if (s_off != null) return;
        s_off = new int[Defs.Length];
        int k = 0;
        for (int i = 0; i < Defs.Length; i++) { s_off[i] = k; k += Defs[i].bolts.Length; }
        s_total = k;
        s_lubeOff = new int[Defs.Length];
        int lk = 0;
        for (int i = 0; i < Defs.Length; i++) { s_lubeOff[i] = lk; lk += CarPartLube.SpotCount(Defs[i]); }
        s_lubeTotal = lk;
    }
    private static int[] s_lubeOff;   // 부위별 도포 지점 시작 번호(CarPartLube)
    private static int s_lubeTotal;

    [Tooltip("떼어낸 부품으로 Spawn 할 장비(CarPartItem)")] public NetworkObject itemPrefab;
    [Tooltip("부품을 놓았을 때 제자리에 장착되는 거리(m) — VR")] public float mountDistVR = 0.35f;
    [Tooltip("부품을 놓았을 때 제자리에 장착되는 거리(m) — PC·모바일")] public float mountDistPC = 1.6f;
    [Tooltip("고스트가 보이기 시작하는 거리(m) — VR")] public float ghostDistVR = 1.2f;
    [Tooltip("고스트가 보이기 시작하는 거리(m) — PC·모바일")] public float ghostDistPC = 3.5f;
    [Tooltip("볼트 표시(아웃라인·링·카드)가 나타나는 거리(m)")] public float markerDist = 2.2f;
    [Tooltip("장착 스냅 애니메이션 시간(초)")] public float snapSeconds = 0.35f;
    [Tooltip("볼트를 메시 바깥쪽으로 내미는 거리(m)")] public float boltProtrude = 0.004f;

    private readonly SyncList<float> _prog = new SyncList<float>();
    private readonly SyncList<float> _lube = new SyncList<float>();   // 도포량(지점별, 0~1.8) — 부품을 떼거나 끼울 때 0
    private readonly SyncList<int> _state = new SyncList<int>();
    private readonly SyncList<int> _item = new SyncList<int>();   // 떼어낸 부품의 NetworkObject.ObjectId (-1 = 없음)

    // ── 로컬 ──
    private class Ring { public LineRenderer track, arc; }

    private class Card
    {
        public GameObject root;
        public TextMeshProUGUI txt;
        public string text;
    }

    private class Part
    {
        public int index;
        public PartDef def;
        public Transform anchor;
        public Transform[] objs;
        public Renderer[] rends;
        public Collider[] cols;
        public bool[] colOn;
        public Vector3 centerLocal;      // 기준 오브젝트 로컬 — 부품 메시 전체의 중심
        public Transform frame;          // 기준 오브젝트 자세를 따라가는 빈 오브젝트(크기 1) — 볼트의 부모
        public Bolt[] bolts;
        public Outlinable[] outlines;
        public int[] outState;
        public Ring[] rings;
        public LineRenderer[] leads;     // 카드 → 볼트 지시선 (어느 볼트가 이 부품 것인지)
        public int[] shown;              // 볼트 표시 상태 캐시(-1 모름, 0 숨김, 1 보임)
        public Card card;
        public int builtKey = -999;
        public float cardUntil, flashUntil;
        public string flashText;
        public Coroutine snap;
        public GameObject snapObj;
        public bool visible = true;
        public float nextEnforce;
        public Bounds wb;                // 부품 메시 전체의 월드 범위(0.3초마다 갱신) — 시선이 어느 부품을 보는지 판정
        public bool wbOk;
        public bool yieldToScenario;     // 시나리오가 지금 이 부위의 볼트를 다루는 중 — 자유 실습 표시는 양보한다
        public GameObject[] lubeGo;      // 도포 점(CarPartLube) — 제자리에 끼운 상태에서만 보인다
        public Material[] lubeMat;
        // 어셈블리 계층: 큰 부품을 떼어내면 그 안의 세부 부위(실제 메시)가 떼어낸 부품(아이템)을 따라다닌다
        public Transform carrier;        // 따라다니는 대상(떼어낸 어셈블리 아이템의 겉모습). null = 차 위 제자리
        public Vector3[] relPos; public Quaternion[] relRot;
        public Vector3[] origLocalPos; public Quaternion[] origLocalRot; public bool[] origColOn;
    }

    private Part[] _parts;
    [System.NonSerialized] private bool _found;
    private float _nextFind, _wheelPollAt;
    private float[] _sendAt, _pending, _activeAt, _allDoneSince, _resendAt;

    // 11/18: 체결 완료 직후 볼트 하나가 바로 풀려 '볼트 풀림 1/3' 이 되던 문제 (TireService 휠 너트와 같은 원인·같은 처리)
    //  · 마지막 볼트를 조이는 동안 보낸 진행도가 체결(Mounted→Tight) 뒤에 도착하면, 서버는 그것을 '풀기' 진행도로 받아 새 볼트 하나를 풀어 버렸다
    //    → 진행도에 조이기/풀기 구분을 실어 보내고, 지금 상태와 다르면 버린다
    //  · 공구 버튼을 누른 채로 있으면 새로 생긴 볼트를 곧바로 풀기 시작했다 → 체결 직후 잠깐(TightenLock) 볼트를 잡을 수 없게 한다
    private const float TightenLock = 1.5f;
    private bool[] _pendingAsm;        // 보낼 진행도가 조이기(true)·풀기(false) 중 무엇이었나
    private float[] _lockUntil;        // 서버: 이 시각까지 그 부위 볼트 진행도 무시
    private float[] _clientLockUntil;  // 클라이언트: 이 시각까지 새 볼트를 못 잡고 원격 진행도도 안 받는다

    private static readonly Color IdleOutline = new Color(0.92f, 0.96f, 1f, 1f);
    private static readonly Color WorkOutline = new Color(0.25f, 1f, 0.35f, 1f);
    private static bool IsVRPlatform => Utils.GetPlatformType() == PlatformType.VR;

    // ══════════════════════════════════════════
    //  수명
    // ══════════════════════════════════════════

    private void Awake()
    {
        Instance = this;
        EnsureOffsets();
        _sendAt = new float[s_total]; _pending = new float[s_total]; _activeAt = new float[s_total];
        for (int i = 0; i < s_total; i++) { _pending[i] = -1f; _activeAt[i] = -100f; }
        _allDoneSince = new float[Defs.Length]; _resendAt = new float[Defs.Length];
        _pendingAsm = new bool[s_total]; _lockUntil = new float[Defs.Length]; _clientLockUntil = new float[Defs.Length];
        CarPartRules.EnsureRegistered();   // 냉각 회로 공기 상태(자유 실습)
        if (GetComponent<BrakeInspection>() == null) gameObject.AddComponent<BrakeInspection>();   // 휠을 뗀 브레이크 마모 점검
        if (GetComponent<TireInspection>() == null) gameObject.AddComponent<TireInspection>();     // 타이어 트레드·공기압 점검
        if (GetComponent<HvVoltageCheck>() == null) gameObject.AddComponent<HvVoltageCheck>();   // 고전압 단자 잔류 전압 확인
        if (GetComponent<ManualInspection>() == null) gameObject.AddComponent<ManualInspection>();   // 휠 속도 센서·스티어링·A/C·BMU·PRA 매뉴얼 측정
        if (GetComponent<SuspensionInspection>() == null) gameObject.AddComponent<SuspensionInspection>();   // 서스펜션 결함 찾기
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ResetLocal(true);
        if (_ghost != null) Destroy(_ghost);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_prog.Count == 0) for (int i = 0; i < s_total; i++) _prog.Add(0f);
        if (_lube.Count == 0) for (int i = 0; i < s_lubeTotal; i++) _lube.Add(0f);
        if (_state.Count == 0) for (int i = 0; i < Defs.Length; i++) _state.Add((int)PartState.Tight);
        if (_item.Count == 0) for (int i = 0; i < Defs.Length; i++) _item.Add(-1);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _prog.OnChange += OnProgChanged;
        _state.OnChange += OnStateChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _prog.OnChange -= OnProgChanged;
        _state.OnChange -= OnStateChanged;
    }

    private void OnProgChanged(SyncListOperation op, int index, float oldItem, float newItem, bool asServer)
    {
        if (asServer || !_found) return;
        if (op != SyncListOperation.Set && op != SyncListOperation.Add) return;
        ApplyProgress(index, newItem);
    }

    private void OnStateChanged(SyncListOperation op, int index, int oldItem, int newItem, bool asServer)
    {
        if (asServer || !_found) return;
        RebuildAll();   // 한 부위의 상태가 바뀌면 다른 부위의 '먼저 할 일'도 바뀐다
        CheckAllDone();
    }

    // 하나라도 떼었다가 전부 다시 체결되면 '정비 완료' 한 번
    private bool _disturbed;
    private void CheckAllDone()
    {
        bool allTight = _state.Count >= Defs.Length;
        for (int i = 0; i < Defs.Length && allTight; i++) if (StateOf(i) != (int)PartState.Tight) allTight = false;
        if (!allTight) { _disturbed = true; return; }
        if (!_disturbed) return;
        _disturbed = false;
        EquipFx.Notice("carpart_done", "정비 완료", "떼어낸 부품을 모두 제자리에 장착하고 규정 토크로 체결했습니다", EquipFx.Info, 4f, 1f);
    }

    /// <summary>차를 찾아 부위 표가 만들어졌는가 — 부위 목록 패널이 쓴다.</summary>
    public bool Ready => _found && _parts != null;

    /// <summary>이 부위를 풀기 전에 먼저 떼어야 하는 것(부위·휠·어셈블리) 이름들.</summary>
    public string RemoveBlockersOf(int p) => RemoveBlockers(p);

    /// <summary>이 부위를 풀기 위한 선행 항목 전부(어셈블리·먼저 뗄 부위·휠)와 지금 충족됐는지(떼어 놓았는지) — 부위 목록 패널이 '먼저:' 를 초록·주황으로 칠한다.</summary>
    public List<KeyValuePair<string, bool>> RemovePrereqs(int p)
    {
        var l = new List<KeyValuePair<string, bool>>();
        if (p < 0 || p >= Defs.Length) return l;
        int par = ParentIndex(Defs[p]);
        if (par >= 0) l.Add(new KeyValuePair<string, bool>(Defs[par].name, StateOf(par) == (int)PartState.Off));
        foreach (var r in Defs[p].requires) { int q = IndexOf(r); if (q >= 0) l.Add(new KeyValuePair<string, bool>(Defs[q].name, StateOf(q) == (int)PartState.Off)); }
        if (Defs[p].needsWheel >= 0) l.Add(new KeyValuePair<string, bool>(WheelNames[Defs[p].needsWheel], WheelOffAt(Defs[p].needsWheel)));
        if (Defs[p].needsWheel2 >= 0) l.Add(new KeyValuePair<string, bool>(WheelNames[Defs[p].needsWheel2], WheelOffAt(Defs[p].needsWheel2)));
        return l;
    }

    /// <summary>지금 조여 있는 볼트 수 — 체결 상태에서는 아직 다 풀리지 않은 볼트, 장착(조이는 중) 상태에서는 규정 토크까지 조인 볼트, 뗀 상태는 0.</summary>
    public int BoltsTightened(int p)
    {
        if (p < 0 || p >= Defs.Length) return 0;
        int st = StateOf(p);
        if (st == (int)PartState.Off) return 0;
        int k = Defs[p].bolts.Length, c = 0;
        for (int n = 0; n < k; n++)
        {
            int idx = s_off[p] + n;
            float v = idx < _prog.Count ? _prog[idx] : 0f;
            if (st == (int)PartState.Tight) { if (v < 1f) c++; }
            else if (v >= 1f) c++;
        }
        return c;
    }

    public int StateOf(int p) => p >= 0 && p < _state.Count ? _state[p] : (int)PartState.Tight;

    /// <summary>이 부위를 풀 수 있는가 — requires 의 부위가 모두 떨어져 있어야 한다.</summary>
    public bool CanRemove(int p)
    {
        int par = ParentIndex(Defs[p]);
        if (par >= 0 && StateOf(par) != (int)PartState.Off) return false;   // 어셈블리를 먼저 차에서 떼어내야 세부 부위를 푼다
        foreach (var r in Defs[p].requires)
        {
            int q = IndexOf(r);
            if (q >= 0 && StateOf(q) != (int)PartState.Off) return false;
        }
        if (Defs[p].needsWheel >= 0 && !WheelOffAt(Defs[p].needsWheel)) return false;
        if (Defs[p].needsWheel2 >= 0 && !WheelOffAt(Defs[p].needsWheel2)) return false;
        return true;
    }

    // 그 바퀴를 떼어 둔 상태 — 브레이크(캘리퍼·디스크)는 휠 안쪽이라 해당 휠을 떼야 접근된다
    private static bool WheelOffAt(int w) => TireService.Instance != null && TireService.Instance.IsWheelOff(w);
    private static readonly string[] WheelNames = { "앞 왼쪽 휠", "앞 오른쪽 휠", "뒤 왼쪽 휠", "뒤 오른쪽 휠" };

    /// <summary>이 부위를 다시 끼울 수 있는가 — 이 부위를 먼저 떼야 하는(requires 로 가진) 부위가 모두 달려 있어야 한다.</summary>
    public bool CanMount(int p)
    {
        string id = Defs[p].id;
        for (int q = 0; q < Defs.Length; q++)
            if (Defs[q].parent == id && StateOf(q) == (int)PartState.Off) return false;   // 세부 부위가 다 달려야 어셈블리를 차에 단다
        for (int q = 0; q < Defs.Length; q++)
        {
            if (System.Array.IndexOf(Defs[q].requires, id) < 0) continue;
            if (StateOf(q) == (int)PartState.Off) return false;
        }
        return true;
    }

    private static string NamesOf(IEnumerable<string> ids)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var id in ids) { int q = IndexOf(id); if (q < 0) continue; if (sb.Length > 0) sb.Append(", "); sb.Append(Defs[q].name); }
        return sb.ToString();
    }

    private string MountBlockers(int p)
    {
        var list = new List<string>();
        string id = Defs[p].id;
        for (int q = 0; q < Defs.Length; q++)
            if ((System.Array.IndexOf(Defs[q].requires, id) >= 0 || Defs[q].parent == id) && StateOf(q) == (int)PartState.Off) list.Add(Defs[q].id);
        return NamesOf(list);
    }

    private string RemoveBlockers(int p)
    {
        var list = new List<string>();
        foreach (var r in Defs[p].requires) { int q = IndexOf(r); if (q >= 0 && StateOf(q) != (int)PartState.Off) list.Add(r); }
        { int par = ParentIndex(Defs[p]); if (par >= 0 && StateOf(par) != (int)PartState.Off) list.Add(Defs[par].id); }   // 어셈블리 먼저
        string s = NamesOf(list);
        if (Defs[p].needsWheel >= 0 && !WheelOffAt(Defs[p].needsWheel)) { string wn = WheelNames[Defs[p].needsWheel]; s = s.Length > 0 ? s + ", " + wn : wn; }
        if (Defs[p].needsWheel2 >= 0 && !WheelOffAt(Defs[p].needsWheel2)) { string wn = WheelNames[Defs[p].needsWheel2]; s = s.Length > 0 ? s + ", " + wn : wn; }
        return s;
    }

    // ══════════════════════════════════════════
    //  매 프레임
    // ══════════════════════════════════════════

    private void Update()
    {
        if (!IsClientStarted && !IsServerStarted) return;
        if (!_found)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (!FindCar()) return;
            _found = true;
            RebuildAll();
            return;
        }

        // 차가 사라졌으면(씬 전환·재생성) 다시 찾는다
        foreach (var pt in _parts) if (pt == null || pt.anchor == null) { ResetLocal(false); return; }
        if (IsServerStarted) ServerWatch();
        if (!IsClientStarted) return;

        // 휠을 떼고 달 때 '휠 먼저' 잠금이 바뀐다 — 서비스 상태가 안 바뀌어도 볼트 표시를 다시 맞춘다(키가 같으면 바로 끝나서 가볍다)
        if (Time.unscaledTime >= _wheelPollAt)
        {
            _wheelPollAt = Time.unscaledTime + 0.5f;
            for (int q = 0; q < _parts.Length; q++) if (_parts[q] != null && (Defs[q].needsWheel >= 0 || Defs[q].needsWheel2 >= 0)) Rebuild(q);
        }

        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            ResendIfStuck(p, pt);
            if (pt.bolts == null) continue;
            for (int n = 0; n < pt.bolts.Length; n++)
            {
                var b = pt.bolts[n];
                if (b == null) continue;
                if (!b.isAssembleMode && b.isloosened && b.gameObject.activeSelf) TireService.DropNut(b);
                int idx = s_off[p] + n;
                if (_pending[idx] >= 0f && (Time.unscaledTime >= _sendAt[idx] || _pending[idx] >= 1f))
                {
                    ProgressServerRpc(idx, _pending[idx], _pendingAsm[idx]);
                    _pending[idx] = -1f;
                    _sendAt[idx] = Time.unscaledTime + 0.1f;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (_found && _parts == null) _found = false;   // 플레이 중 스크립트 재컴파일(도메인 리로드) 뒤 — 다시 찾는다
        if (!_found) { HideGhost(); return; }
        SyncCarriers();
        FollowCarriers();
        PlaceFrames();
        EnforceHidden();
        if (!IsClientStarted) return;
        UpdateVisuals();
        UpdateGhost();
        UpdateLubeSpots();
    }

    private void PlaceFrames()
    {
        foreach (var pt in _parts)
            if (pt != null && pt.anchor != null && pt.frame != null)
                pt.frame.SetPositionAndRotation(pt.anchor.position, pt.anchor.rotation);
    }

    // 다른 스크립트(하이라이트·시나리오 연출)가 렌더러를 다시 켜도 떼어낸 부위는 계속 숨긴다
    private void EnforceHidden()
    {
        foreach (var pt in _parts)
        {
            if (pt == null || pt.visible || Time.unscaledTime < pt.nextEnforce) continue;
            pt.nextEnforce = Time.unscaledTime + 0.5f;
            ApplyVisible(pt, false);
        }
    }

    // ══════════════════════════════════════════
    //  어셈블리 계층 — 큰 부품 → 세부 부품
    //  큰 부품(서브 프레임·크로스 멤버·스트럿 …)을 차에서 떼어내면 그 안의 세부 부위(실제 메시·볼트)가 떼어낸 아이템을 따라다닌다.
    //  아이템을 바닥에 내려놓고 거기서 세부 부위를 풀어 낸다. 큰 부품을 다시 차에 달려면 세부 부위가 먼저 모두 달려 있어야 한다.
    //  (부모를 바꾸지 않고 매 프레임 자세만 맞춘다 — 아이템이 디스폰돼도 차 메시가 같이 사라지지 않는다)
    // ══════════════════════════════════════════
    private readonly Dictionary<int, Transform> _itemVisual = new Dictionary<int, Transform>();
    private float _carrierAt;

    /// <summary>CarPartItem 이 겉모습을 만들면 알린다 — 세부 부위가 이 겉모습을 따라다닌다.</summary>
    public void RegisterItemVisual(int p, Transform v) { if (v != null) _itemVisual[p] = v; }
    public void UnregisterItemVisual(int p, Transform v) { if (_itemVisual.TryGetValue(p, out var cur) && cur == v) _itemVisual.Remove(p); }

    private static int ParentIndex(PartDef d) => string.IsNullOrEmpty(d.parent) ? -1 : IndexOf(d.parent);

    /// <summary>이 부위를 따라다니게 할 대상 — 가장 가까운 '떼어낸 조상'의 아이템. pending = 조상은 떼어냈는데 아이템이 아직 없다(지금 자리 유지).</summary>
    private Transform FindCarrier(int c, out bool pending)
    {
        pending = false;
        int a = ParentIndex(Defs[c]);
        for (int guard = 0; a >= 0 && guard < 8; guard++)
        {
            if (StateOf(a) == (int)PartState.Off)
            {
                if (_itemVisual.TryGetValue(a, out var v) && v != null) return v;
                pending = true; return null;
            }
            a = ParentIndex(Defs[a]);
        }
        return null;
    }

    private void SyncCarriers()
    {
        if (_parts == null || Time.unscaledTime < _carrierAt) return;
        _carrierAt = Time.unscaledTime + 0.15f;
        for (int c = 0; c < _parts.Length; c++)
        {
            var pt = _parts[c];
            if (pt == null || string.IsNullOrEmpty(pt.def.parent) || pt.anchor == null) continue;
            var want = FindCarrier(c, out bool pending);
            if (pending || want == pt.carrier) continue;
            if (want != null) AttachFollow(pt, want); else DetachFollow(pt);
        }
    }

    private static void AttachFollow(Part pt, Transform carrier)
    {
        int n = pt.objs.Length;
        if (pt.carrier == null)
        {
            // 처음 따라붙는다 — 차 위 원래 자세(부모 기준)와 콜라이더 상태를 저장하고, 콜라이더는 끈다(떨어지는 아이템과 부딪히지 않게)
            pt.origLocalPos = new Vector3[n]; pt.origLocalRot = new Quaternion[n]; pt.origColOn = (bool[])pt.colOn.Clone();
            for (int i = 0; i < n; i++) { pt.origLocalPos[i] = pt.objs[i].localPosition; pt.origLocalRot[i] = pt.objs[i].localRotation; }
            for (int k = 0; k < pt.cols.Length; k++) { pt.colOn[k] = false; if (pt.cols[k] != null) pt.cols[k].enabled = false; }
        }
        pt.carrier = carrier;
        pt.relPos = new Vector3[n]; pt.relRot = new Quaternion[n];
        Quaternion inv = Quaternion.Inverse(carrier.rotation);
        for (int i = 0; i < n; i++) { pt.relPos[i] = carrier.InverseTransformPoint(pt.objs[i].position); pt.relRot[i] = inv * pt.objs[i].rotation; }
    }

    private static void DetachFollow(Part pt)
    {
        if (pt.origLocalPos != null)
            for (int i = 0; i < pt.objs.Length && i < pt.origLocalPos.Length; i++)
                if (pt.objs[i] != null) { pt.objs[i].localPosition = pt.origLocalPos[i]; pt.objs[i].localRotation = pt.origLocalRot[i]; }
        if (pt.origColOn != null)
            for (int k = 0; k < pt.cols.Length && k < pt.origColOn.Length; k++)
            {
                pt.colOn[k] = pt.origColOn[k];
                if (pt.cols[k] != null) pt.cols[k].enabled = pt.visible && pt.colOn[k];
            }
        pt.carrier = null; pt.relPos = null; pt.relRot = null; pt.origLocalPos = null; pt.origLocalRot = null; pt.origColOn = null;
    }

    // 아이템이 움직인 뒤(FixedUpdate·네트워크 보간 이후)에 세부 부위를 따라 움직인다 — LateUpdate 에서 부른다
    private void FollowCarriers()
    {
        if (_parts == null) return;
        foreach (var pt in _parts)
        {
            if (pt == null || pt.relPos == null) continue;
            if (pt.carrier == null) { DetachFollow(pt); continue; }   // 아이템이 사라졌다(장착·디스폰) — 차 위 제자리로
            for (int i = 0; i < pt.objs.Length && i < pt.relPos.Length; i++)
                if (pt.objs[i] != null) pt.objs[i].SetPositionAndRotation(pt.carrier.TransformPoint(pt.relPos[i]), pt.carrier.rotation * pt.relRot[i]);
            if (pt.anchor != null && pt.frame != null) pt.frame.SetPositionAndRotation(pt.anchor.position, pt.anchor.rotation);
        }
    }

    private void ResetLocal(bool restore)
    {
        if (_parts != null) foreach (var dp in _parts) if (dp != null && dp.relPos != null) DetachFollow(dp);
        _found = false;
        HideGhost();
        DestroyHighlights();
        if (_parts == null) return;
        foreach (var pt in _parts)
        {
            if (pt == null) continue;
            StopSnap(pt);
            ClearBolts(pt, false);
            if (pt.card != null && pt.card.root != null) Destroy(pt.card.root);
            if (restore && pt.anchor != null) ApplyVisible(pt, true);
            if (pt.frame != null) Destroy(pt.frame.gameObject);
        }
        _parts = null;
    }

    // ══════════════════════════════════════════
    //  차 부위 찾기 — 클라이언트·서버 공용
    // ══════════════════════════════════════════

    private static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    /// <summary>
    /// 차 모델이 여러 벌(LOD·변형) 겹쳐 있으므로, 켜져 있는 차(VehiclePartsManager) 안에서 이름으로 찾는다.
    /// 같은 이름이 여럿이면 경로가 가장 앞선 것 — 클라이언트·서버가 같은 것을 고른다.
    /// </summary>
    private static Transform FindIn(Transform[] pool, string name)
    {
        Transform best = null; string bestPath = null;
        foreach (var t in pool)
        {
            if (t == null || t.name != name || !t.gameObject.activeInHierarchy) continue;
            if (t.GetComponentInChildren<Renderer>(true) == null) continue;
            string path = PathOf(t);
            // 고전압 케이블 보기용 복제본(…/Cables/SM_*)은 이름이 같고 경로가 더 앞서서, 켜져 있으면 진짜 부위 대신 잡혔다 — 새 부위(서스펜션·모터 등)는 복제본이 있다
            if (path.Contains("/Cables/")) continue;
            if (best == null || string.CompareOrdinal(path, bestPath) < 0) { best = t; bestPath = path; }
        }
        return best;
    }

    private bool FindCar()
    {
        // 차가 여러 대면 기준 부위(앞 범퍼) 경로가 가장 앞선 차 — 클라이언트·서버가 같은 차를 고른다
        Transform[] pool = null; string poolKey = null;
        foreach (var vm in FindObjectsByType<VehiclePartsManager>(FindObjectsSortMode.None))
        {
            if (vm == null || !vm.gameObject.activeInHierarchy) continue;
            var all = vm.GetComponentsInChildren<Transform>(false);
            var key = FindIn(all, Defs[0].objects[0]);
            if (key == null) continue;
            string kp = PathOf(key);
            if (pool == null || string.CompareOrdinal(kp, poolKey) < 0) { pool = all; poolKey = kp; }
        }
        if (pool == null) return false;

        var parts = new Part[Defs.Length];
        for (int i = 0; i < Defs.Length; i++)
        {
            var d = Defs[i];
            var objs = new List<Transform>();
            if (d.gen != null)
            {
                var h = EnsureGen(pool, d);
                if (h == null) { Debug.LogWarning($"[CarPartService] '{d.name}' 생성 기준({GenRefName})을 차에서 못 찾았습니다"); return false; }
                objs.Add(h);
            }
            else foreach (var n in d.objects) { var t = FindIn(pool, n); if (t != null) objs.Add(t); }
            if (objs.Count == 0 || (d.gen == null && objs[0].name != d.objects[0]))
            {
                Debug.LogWarning($"[CarPartService] '{d.name}' 부위({d.objects[0]})를 차에서 못 찾았습니다");
                return false;
            }

            var pt = new Part { index = i, def = d, anchor = objs[0], objs = objs.ToArray() };
            var rl = new List<Renderer>(); var cl = new List<Collider>();
            foreach (var o in pt.objs)
            {
                foreach (var r in o.GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer || r is SkinnedMeshRenderer) rl.Add(r);
                cl.AddRange(o.GetComponentsInChildren<Collider>(true));
            }
            pt.rends = rl.ToArray();
            pt.cols = cl.ToArray();
            pt.colOn = new bool[pt.cols.Length];
            for (int c = 0; c < pt.cols.Length; c++) pt.colOn[c] = pt.cols[c].enabled;

            // 부품 메시 전체의 중심(기준 오브젝트 로컬) — 떼어낸 부품의 잡는 자리·장착 거리 기준
            bool any = false; Bounds wb = default;
            foreach (var r in pt.rends) { if (!any) { wb = r.bounds; any = true; } else wb.Encapsulate(r.bounds); }
            pt.centerLocal = any ? pt.anchor.InverseTransformPoint(wb.center) : Vector3.zero;

            var fr = new GameObject($"[CarPart {d.id}]");
            fr.transform.SetParent(transform, false);
            pt.frame = fr.transform;
            parts[i] = pt;
        }
        _parts = parts;
        PlaceFrames();
        return true;
    }

    /// <summary>
    /// 차 모델에 없는 부품(def.gen)을 상자·원통으로 만든다. 홀더는 SM_Gearbox 의 부모 아래, 차 루트와 같은 좌표계 자리.
    /// 이미 있으면(차를 다시 찾을 때) 그대로 쓴다. 다른 부위 메시 아래에 두지 않는다 — 그 부위를 숨기면 같이 숨겨지므로.
    /// </summary>
    private static Transform EnsureGen(Transform[] pool, PartDef d)
    {
        var refT = FindIn(pool, GenRefName);
        if (refT == null || refT.parent == null) return null;
        string hn = $"[Gen_{d.id}]";
        var exist = refT.parent.Find(hn);
        if (exist != null) return exist;

        var h = new GameObject(hn).transform;
        h.gameObject.layer = refT.gameObject.layer;
        h.SetParent(refT.parent, false);
        h.localRotation = refT.localRotation;
        h.localScale = refT.localScale;
        h.position = refT.TransformPoint(-GenRef);

        var srcR = refT.GetComponentInChildren<MeshRenderer>(true);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var g in d.gen)
        {
            var go = GameObject.CreatePrimitive(g.cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.layer = h.gameObject.layer;
            go.transform.SetParent(h, false);
            go.transform.localPosition = g.c;
            go.transform.localRotation = Quaternion.Euler(g.e);
            go.transform.localScale = g.s;
            var mr = go.GetComponent<MeshRenderer>();
            Material m = lit != null ? new Material(lit) : (srcR != null ? new Material(srcR.sharedMaterial) : new Material(mr.sharedMaterial));
            m.mainTexture = null;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", g.col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", g.col);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", g.metal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f + g.metal * 0.3f);
            mr.sharedMaterial = m;
        }
        return h;
    }

    /// <summary>부위의 기준 오브젝트(앵커)·그 로컬 중심·레이어 — 부위에 붙는 클릭 대상(점검 등)이 쓴다</summary>
    public bool TryGetPartAnchor(int p, out Transform anchor, out Vector3 centerLocal, out int layer)
    {
        var pt = _parts != null && p >= 0 && p < _parts.Length ? _parts[p] : null;
        if (pt == null || pt.anchor == null) { anchor = null; centerLocal = Vector3.zero; layer = 0; return false; }
        anchor = pt.anchor; centerLocal = pt.centerLocal; layer = pt.anchor.gameObject.layer;
        return true;
    }

    public Vector3 PartCenter(int p)
    {
        var pt = _parts != null && p >= 0 && p < _parts.Length ? _parts[p] : null;
        return pt != null && pt.anchor != null ? pt.anchor.TransformPoint(pt.centerLocal) : Vector3.zero;
    }

    public Quaternion PartRotation(int p)
    {
        var pt = _parts != null && p >= 0 && p < _parts.Length ? _parts[p] : null;
        return pt != null && pt.anchor != null ? pt.anchor.rotation : Quaternion.identity;
    }

    private Vector3 BoltWorld(Part pt, int n) => pt.anchor.TransformPoint(pt.def.bolts[n].p);
    private Vector3 BoltOut(Part pt, int n) => pt.anchor.TransformDirection(pt.def.bolts[n].n).normalized;

    // 카드·안내 위치: 볼트들의 가운데에서 바깥쪽으로 조금
    private Vector3 WorkPoint(Part pt, out Vector3 outward)
    {
        Vector3 c = Vector3.zero, o = Vector3.zero;
        int k = pt.def.bolts.Length;
        for (int n = 0; n < k; n++) { c += BoltWorld(pt, n); o += BoltOut(pt, n); }
        c /= Mathf.Max(1, k);
        outward = o.sqrMagnitude > 1e-4f ? o.normalized : Vector3.up;
        return c;
    }

    private static void ApplyVisible(Part pt, bool on)
    {
        foreach (var r in pt.rends) if (r != null) r.enabled = on;
        for (int c = 0; c < pt.cols.Length; c++) if (pt.cols[c] != null) pt.cols[c].enabled = on && pt.colOn[c];
    }

    private void SetVisible(Part pt, bool on)
    {
        pt.visible = on;
        ApplyVisible(pt, on);
    }

    // ══════════════════════════════════════════
    //  볼트
    // ══════════════════════════════════════════

    private Bolt MakeBolt(Part pt, int n)
    {
        var d = pt.def;
        var f = d.bolts[n];
        // Bolt 는 '부모'의 Y 축을 따라 빠져나오고(Bolt.AnimateBoltPosition), 렌치도 부모의 -Y 를 볼트 축으로 보고 정렬한다(ImpactWrench.UpdateBoltSnap).
        // 그래서 볼트마다 피벗(-Y = 바깥)을 하나 두고 그 아래에 볼트를 단다 — 볼트 자체를 돌려 두면 옆면 볼트에서 드릴이 세로로 서고 볼트가 엉뚱한 쪽으로 빠졌다.
        Vector3 lossy = pt.anchor.lossyScale;
        Vector3 nrm = f.n.normalized;
        var piv = new GameObject($"[BoltPivot {d.id}-{n + 1}]");
        piv.transform.SetParent(pt.frame, false);
        piv.transform.localPosition = Vector3.Scale(f.p, lossy) + nrm * boltProtrude;
        piv.transform.localRotation = Quaternion.FromToRotation(Vector3.down, nrm);   // 피벗의 -Y 가 바깥(공구 쪽)
        var g = new GameObject($"PartBolt {d.id}-{n + 1}");
        g.transform.SetParent(piv.transform, false);
        g.layer = 18;
        try { g.tag = "Bolt"; } catch { }
        var col = g.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, -0.015f, 0f);
        col.radius = 0.024f * d.boltScale;

        if (TireService.EnsureBoltTemplate())
        {
            var vis = new GameObject("Mesh");
            vis.layer = 18;
            vis.transform.SetParent(g.transform, false);
            vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 메시 +Z(머리) → 바깥(-Y)
            vis.transform.localScale = Vector3.one * d.boltScale;
            vis.AddComponent<MeshFilter>().sharedMesh = TireService.s_boltMesh;
            var mr = vis.AddComponent<MeshRenderer>();
            mr.sharedMaterials = TireService.s_boltMats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            var vis = TireService.Cyl(g.transform, "Hex", new Vector3(0f, -0.006f, 0f), 0.018f * d.boltScale, 0.012f * d.boltScale, TireService.NutMat);
            vis.layer = 18;
        }

        var b = g.AddComponent<Bolt>();
        b.timeToComplete = d.boltScale > 1.2f ? 1.6f : 1.0f;
        b.ejectLocalY = 0.025f * d.boltScale;
        var free = g.AddComponent<FreeWorkBolt>();
        free.torqueMin = d.tMin; free.torqueMax = d.tMax;
        free.specPart = d.specPart; free.specSource = d.specSource;
        free.socket = d.socket;
        int idx = s_off[pt.index] + n;
        b.OnProgressChanged += _ => { _pending[idx] = b.Progress; _pendingAsm[idx] = b.isAssembleMode; _activeAt[idx] = Time.time; };
        return b;
    }

    private void ClearBolts(Part pt, bool fade)
    {
        if (pt.bolts != null)
            for (int n = 0; n < pt.bolts.Length; n++)
            {
                var b = pt.bolts[n];
                if (b != null)
                {
                    var piv = b.transform.parent;
                    if (fade && b.gameObject.activeInHierarchy) FadeOutBolt(b);
                    else Destroy(b.gameObject);
                    if (piv != null && piv != pt.frame) Destroy(piv.gameObject);
                }
                if (pt.leads != null && n < pt.leads.Length && pt.leads[n] != null) Destroy(pt.leads[n].gameObject);
                var r = pt.rings != null ? pt.rings[n] : null;
                if (r != null)
                {
                    if (r.track != null) Destroy(r.track.gameObject);
                    if (r.arc != null) Destroy(r.arc.gameObject);
                }
                _pending[s_off[pt.index] + n] = -1f;
            }
        int k = pt.def.bolts.Length;
        pt.bolts = new Bolt[k];
        pt.outlines = new Outlinable[k];
        pt.outState = new int[k];
        for (int n = 0; n < k; n++) pt.outState[n] = -1;
        pt.rings = new Ring[k];
        pt.leads = new LineRenderer[k];
        pt.shown = new int[k];
        for (int n = 0; n < k; n++) pt.shown[n] = -1;
    }

    private void FadeOutBolt(Bolt b)
    {
        var go = b.gameObject;
        b.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        var o = go.GetComponent<Outlinable>(); if (o != null) o.enabled = false;
        go.transform.SetParent(null, true);
        go.name = "[TightenedBolt]";
        StartCoroutine(TireService.ShrinkAndDestroy(go.transform, 0.35f));
    }

    private void RebuildAll()
    {
        if (_parts == null) return;
        for (int p = 0; p < _parts.Length; p++) Rebuild(p);
    }

    /// <summary>부위 상태(와 '먼저 할 일' 잠금)에 맞춰 볼트·차 모습을 다시 만든다.</summary>
    private void Rebuild(int p)
    {
        var pt = _parts[p];
        if (pt == null || p >= _state.Count) return;
        int st = _state[p];
        bool locked = st == (int)PartState.Tight && !CanRemove(p);
        int key = st * 2 + (locked ? 1 : 0);
        if (pt.builtKey == key) return;
        int prevKey = pt.builtKey;
        int prev = prevKey < 0 ? -1 : prevKey / 2;
        pt.builtKey = key;

        StopSnap(pt);
        bool justTightened = prev == (int)PartState.Mounted && st == (int)PartState.Tight;
        ClearBolts(pt, justTightened);

        if (!IsClientStarted)
        {
            SetVisible(pt, st != (int)PartState.Off);
            return;
        }

        if (prev >= 0 && prev != st && st != (int)PartState.Mounted)
            Flash(pt, st == (int)PartState.Off ? $"<b>{pt.def.name} 분해 완료</b>\n<size=62%><color=#8FD3FF>부품을 잡아서 떼어내세요</color></size>"
                                                : $"<b>{pt.def.name} 체결 완료</b>\n<size=62%><color=#8FD3FF>장착이 끝났습니다</color></size>");

        if (st == (int)PartState.Off) { SetVisible(pt, false); return; }

        bool snapping = prev == (int)PartState.Off && st == (int)PartState.Mounted;
        if (snapping) CarPartLube.NoteMounted(pt.def);                       // 제자리에 얹었다 → 도포하라고 알린다
        if (justTightened)                                                     // 다 조였다 → 도포 결과
        {
            LubeStatus(p, out int lubeOk, out int lubeTotal, out int lubeExcess);
            CarPartLube.NoteTightened(pt.def, lubeOk, lubeTotal, lubeExcess);
        }
        if (!locked)
        {
            for (int n = 0; n < pt.bolts.Length; n++)
            {
                var b = MakeBolt(pt, n);
                if (st == (int)PartState.Mounted) b.ShowAssembleStart();
                pt.bolts[n] = b;
                int idx = s_off[p] + n;
                if (!justTightened && idx < _prog.Count && _prog[idx] > 0f) ApplyProgress(idx, _prog[idx]);
                if (justTightened) StartCoroutine(TireService.ScaleIn(b.transform, 0.25f, 0.35f));
            }
            if (justTightened)
            {
                _clientLockUntil[p] = Time.unscaledTime + TightenLock;
                StartCoroutine(LockBoltsBriefly(pt.bolts, TightenLock));
            }
        }

        if (snapping)
        {
            SetVisible(pt, false);
            foreach (var b in pt.bolts) if (b != null) b.gameObject.SetActive(false);
            pt.snap = StartCoroutine(SnapRoutine(pt));
        }
        else SetVisible(pt, true);
    }

    /// <summary>체결 직후 새 볼트를 잠깐 못 잡게 한다(공구 버튼을 누른 채라 바로 풀리지 않게).</summary>
    private static IEnumerator LockBoltsBriefly(Bolt[] bolts, float sec)
    {
        var cols = new List<Collider>();
        foreach (var b in bolts) if (b != null) foreach (var c in b.GetComponentsInChildren<Collider>()) if (c.enabled) { c.enabled = false; cols.Add(c); }
        yield return new WaitForSecondsRealtime(sec);
        foreach (var c in cols) if (c != null) c.enabled = true;   // 그 사이 다시 만들어졌으면(파괴) 건너뛴다
    }

    private void ApplyProgress(int idx, float v)
    {
        if (_parts == null) return;
        int p = PartOfBolt(idx);
        if (p < 0 || _parts[p] == null || _parts[p].bolts == null) return;
        if (Time.unscaledTime < _clientLockUntil[p]) return;   // 체결 직후 늦게 온 이전 진행도는 받지 않는다
        var b = _parts[p].bolts[idx - s_off[p]];
        if (b == null || v <= b.Progress) return;
        b.ApplyRemoteProgress(v);
        _activeAt[idx] = Time.time;
    }

    private static int PartOfBolt(int idx)
    {
        EnsureOffsets();
        if (idx < 0 || idx >= s_total) return -1;
        for (int p = Defs.Length - 1; p >= 0; p--) if (idx >= s_off[p]) return p;
        return -1;
    }

    // 내 화면에선 볼트가 다 끝났는데 서버 상태가 그대로면 1.5초마다 완료를 다시 알린다 (TireService 와 같은 보호)
    private void ResendIfStuck(int p, Part pt)
    {
        if (pt.bolts == null || p >= _state.Count || _state[p] == (int)PartState.Off || pt.snap != null) { _allDoneSince[p] = 0f; return; }
        for (int n = 0; n < pt.bolts.Length; n++)
            if (pt.bolts[n] == null || !pt.bolts[n].isloosened) { _allDoneSince[p] = 0f; return; }
        if (_allDoneSince[p] <= 0f) { _allDoneSince[p] = Time.unscaledTime; _resendAt[p] = Time.unscaledTime + 1.5f; return; }
        if (Time.unscaledTime < _resendAt[p]) return;
        _resendAt[p] = Time.unscaledTime + 1.5f;
        Debug.LogWarning($"[CarPartService] {pt.def.name}: 볼트 완료인데 서버 상태가 그대로({(PartState)_state[p]}) → 완료 다시 전송");
        PartDoneServerRpc(p, _state[p]);
    }

    // ══════════════════════════════════════════
    //  표시 — 아웃라인·진행 링·'분해/체결 n/m' 카드·안내
    // ══════════════════════════════════════════

    // ── 지금 손에 든 것 (0.2초마다) ──
    // 11/16 테스트: 시나리오 작업 중에도 차 앞을 지나가기만 하면 부위 볼트·카드·'탈거·장착' 안내가 여기저기 떠서
    // 어느 볼트가 어느 부품 것인지 알 수 없었다 → 볼트 공구를 들고 있을 때만, 겨눈 부위 하나만 보여 준다.
    private static float s_heldCheckAt = -1f;
    private static bool s_toolHeld, s_wheelHeld;
    private static readonly List<Transform> s_toolTf = new List<Transform>();   // 내가 든 볼트 공구들(위치는 매 프레임 읽는다)

    // 공구가 볼트에서 이만큼(m) 안이면 시선이 어디를 보든 그 볼트를 '작업 중'으로 본다.
    // 11/16 테스트: 범퍼에 바짝 다가가면 볼트가 시선 중심에서 벗어나(각도 ↑) 숨겨지고 콜라이더까지 꺼져 드릴이 볼트를 못 잡았다.
    private const float ToolNear = 0.45f;

    private static void RefreshHeld()
    {
        if (Time.unscaledTime < s_heldCheckAt && s_heldCheckAt - Time.unscaledTime < 1f) return;
        s_heldCheckAt = Time.unscaledTime + 0.2f;
        s_toolHeld = s_wheelHeld = false;
        s_toolTf.Clear();
        foreach (var g in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (g == null || !g.IsOwner || !g.IsGrabbed || g.IsOnBelt) continue;
            if (g.GetComponentInChildren<ImpactWrench>(true) != null || g.GetComponentInChildren<AirImpactWrench>(true) != null
                || g.GetComponentInChildren<ManualTool>(true) != null || g.GetComponentInChildren<TorqueWrench>(true) != null)
            { s_toolHeld = true; s_toolTf.Add(g.transform); }
            if (g.GetComponentInChildren<TireWheel>(true) != null) s_wheelHeld = true;
        }
    }

    private static float ToolDist(Vector3 p)
    {
        float best = 999f;
        for (int i = 0; i < s_toolTf.Count; i++)
        {
            var t = s_toolTf[i];
            if (t == null) continue;
            best = Mathf.Min(best, Vector3.Distance(t.position, p));
        }
        return best;
    }

    // 시선과 볼트가 이루는 '유효 각도'. 가까이 가면 같은 거리 차이도 각도가 크게 벌어지므로, 1m 보다 가까운 볼트는 1m 에 있는 것으로 쳐서
    // 시선 옆 몇십 cm 안이면 계속 겨눈 것으로 본다. 시선 뒤쪽이면 180.
    private static float EffAngle(Vector3 d, Vector3 fwd)
    {
        float f = Vector3.Dot(d, fwd);
        if (f <= 0.02f) return 180f;
        float perp = Mathf.Sqrt(Mathf.Max(0f, d.sqrMagnitude - f * f));
        return Mathf.Atan2(perp, Mathf.Max(f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>내가 볼트 공구(드릴·에어 임팩렌치·스패너·토크렌치)를 들고 있는가.</summary>
    public static bool LocalHoldsBoltTool { get { RefreshHeld(); return s_toolHeld; } }
    /// <summary>내가 휠을 들고 있는가.</summary>
    public static bool LocalHoldsWheel { get { RefreshHeld(); return s_wheelHeld; } }

    // 다른 시스템(시나리오·타이어)의 볼트 — 그쪽을 겨누고 있으면 부위 표시를 끈다
    private readonly List<Bolt> _otherBolts = new List<Bolt>();
    private float _otherBoltsAt = -1f;

    private float OtherBoltAngle(Vector3 cp, Vector3 fwd, float near)
    {
        if (Time.unscaledTime >= _otherBoltsAt)
        {
            _otherBoltsAt = Time.unscaledTime + 1f;
            _otherBolts.Clear();
            foreach (var b in FindObjectsByType<Bolt>(FindObjectsSortMode.None))
                if (b != null && !b.transform.IsChildOf(transform)) _otherBolts.Add(b);
        }
        float best = 999f;
        foreach (var b in _otherBolts)
        {
            if (b == null || !b.gameObject.activeInHierarchy || b.Progress >= 1f) continue;
            Vector3 d = b.transform.position - cp;
            if (d.sqrMagnitude > near * near) continue;
            float a = EffAngle(d, fwd);
            if (ToolDist(b.transform.position) < ToolNear) a = Mathf.Min(a, 1f);
            best = Mathf.Min(best, a);
        }
        return best;
    }

    // 시선 앞 가림 — 지금 바라보는 면(첫 충돌)보다 한참 뒤에 있는 볼트(차 반대편 좌석 볼트 등)는 겨눈 것으로 치지 않는다.
    // 공구를 볼트 옆에 댄 경우(ToolNear)는 예외. 들고 있는 도구·부품·내 아바타 충돌체는 건너뛴다.
    private static float s_gazeDist = 999f;
    private static int s_gazeFrame = -1;
    private static readonly RaycastHit[] s_gazeHits = new RaycastHit[16];

    private static void UpdateGaze(Vector3 cp, Vector3 fwd)
    {
        if (s_gazeFrame == Time.frameCount) return;
        s_gazeFrame = Time.frameCount;
        float best = 999f;
        int n = Physics.RaycastNonAlloc(new Ray(cp, fwd), s_gazeHits, 8f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = s_gazeHits[i].collider;
            if (c == null || s_gazeHits[i].distance < 0.15f) continue;
            if (c.GetComponentInParent<CharacterController>() != null) continue;
            var sg = c.GetComponentInParent<SyncGrab>();
            if (sg != null && sg.IsGrabbed) continue;
            best = Mathf.Min(best, s_gazeHits[i].distance);
        }
        s_gazeDist = best;
    }

    private float PartAngle(Part pt, Vector3 cp, Vector3 fwd, float near)
    {
        UpdateGaze(cp, fwd);
        float best = 999f;
        for (int n = 0; n < pt.def.bolts.Length; n++)
        {
            Vector3 bw = BoltWorld(pt, n);
            Vector3 d = bw - cp;
            if (d.sqrMagnitude > near * near) continue;
            if (d.magnitude > s_gazeDist + 0.35f && ToolDist(bw) >= ToolNear) continue;   // 바라보는 면 뒤쪽(반대편) 볼트
            float a = EffAngle(d, fwd);
            if (ToolDist(bw) < ToolNear) a = Mathf.Min(a, 1f);   // 공구를 볼트 옆에 댔으면 시선과 상관없이 이 부위
            best = Mathf.Min(best, a);
        }
        return best;
    }

    private bool PartActive(int p)
    {
        var pt = _parts[p];
        if (pt == null || pt.bolts == null) return false;
        for (int n = 0; n < pt.bolts.Length; n++)
            if (pt.bolts[n] != null && Time.time - _activeAt[s_off[p] + n] < 0.5f) return true;
        return false;
    }

    private int _focus = -1;

    /// <summary>
    /// 지금 보여 줄 부위 하나 — 볼트 공구를 들고 화면 가운데(PC 조준점·VR 시선)에 볼트가 가장 가까운 부위.
    /// 시나리오·타이어 볼트가 더 가까우면 없음(-1). 작업 중인 부위는 시선이 조금 벗어나도 유지한다.
    /// </summary>
    private int PickFocus(Camera cam, float near)
    {
        if (!LocalHoldsBoltTool) return -1;

        // 드릴이 이미 물고 있는 볼트가 있으면 그것이 기준
        var tb = ImpactWrench.LocalTargetBolt;
        if (tb != null && tb.gameObject.activeInHierarchy)
        {
            if (!tb.transform.IsChildOf(transform)) return -1;
            for (int p = 0; p < _parts.Length; p++)
                if (_parts[p] != null && _parts[p].frame != null && tb.transform.IsChildOf(_parts[p].frame)) return p;
        }

        Vector3 cp = cam.transform.position, fwd = cam.transform.forward;
        float lim = IsVRPlatform ? 28f : 16f;
        int best = -1; float bestA = lim, curA = 999f;
        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            if (pt == null || pt.anchor == null || pt.snap != null || StateOf(p) == (int)PartState.Off) continue;
            // 다른 부품에 가려 아직 못 푸는 부위(예: 범퍼 안쪽 인버터)는 겨눠지지 않게 — 안 그러면 차 속 부품 카드가 한꺼번에 떴다
            if (StateOf(p) == (int)PartState.Tight && !CanRemove(p)) continue;
            if (pt.yieldToScenario) continue;   // 시나리오 볼트와 같은 부위 — 시나리오 쪽만 보이게
            float a = PartAngle(pt, cp, fwd, near);
            if (p == _focus) curA = a;
            if (a < bestA) { bestA = a; best = p; }
        }
        // 지금 보던 부위는 작업 중이거나 새 후보보다 크게 나쁘지 않으면 유지(경계에서 깜빡이지 않게)
        if (_focus >= 0 && _focus < _parts.Length && StateOf(_focus) != (int)PartState.Off && curA < lim + 6f
            && (PartActive(_focus) || curA <= bestA + 4f)) { best = _focus; bestA = curA; }
        if (best < 0) return -1;
        if (!PartActive(best) && OtherBoltAngle(cp, fwd, near) + 1f < bestA) return -1;
        return best;
    }

    // vis = 볼트 모습, col = 드릴이 잡는 콜라이더. 공구가 바로 옆에 있으면(col) 모습이 꺼져 있어도 콜라이더는 살려 둔다 — 안 그러면 다가가는 중에 볼트가 풀리다 말고 사라진다
    private static void ShowBolt(Part pt, int n, Bolt b, bool vis, bool col)
    {
        int v = (vis ? 1 : 0) | (col ? 2 : 0);
        if (pt.shown[n] == v) return;
        pt.shown[n] = v;
        foreach (var r in b.GetComponentsInChildren<Renderer>(true)) r.enabled = vis;
        foreach (var c in b.GetComponentsInChildren<Collider>(true)) c.enabled = col;
    }

    private void UpdateVisuals()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float near = IsVRPlatform ? Mathf.Min(markerDist, 1.8f) : markerDist;

        if (Time.unscaledTime >= _boundsAt)
        {
            _boundsAt = Time.unscaledTime + 0.3f;
            foreach (var bp in _parts) RefreshBounds(bp);
        }
        RefreshYield();
        // 시나리오 중에도 표시는 켜 두되, 한 번에 한 부위(겨눈 부위)만 보여 준다 — 카드는 아래 cardPart 하나만
        int hover = PickHover(cam);
        int focus = PickFocus(cam, near);
        // 볼트가 시선 각도 밖이어도 공구를 든 채 부품을 바라보면 그 부품을 작업 대상으로 (시나리오·타이어 볼트를 겨누는 중이면 제외)
        if (focus < 0 && hover >= 0 && OtherBoltAngle(cam.transform.position, cam.transform.forward, near) > 20f) focus = hover;
        UpdateHover(hover);
        UpdateSlotGhosts(cam);
        int cardPart = focus >= 0 ? focus : hover;   // 카드는 한 번에 한 부위만
        if (focus != _focus)
        {
            _focus = focus;
            if (focus >= 0)
            {
                var fd = _parts[focus].def;
                int fst = StateOf(focus);
                string body = fst == (int)PartState.Mounted ? $"{fd.tool}로 볼트를 조이세요 — 규정 {fd.tMin:0.0#} ~ {fd.tMax:0.0#} kgf·m"
                            : !CanRemove(focus) ? $"먼저 {RemoveBlockers(focus)}을(를) 떼어야 합니다"
                            : fd.hint;
                // 이름·공구·규정·접근 위치는 부품 카드에 이미 있다 — 겨눌 때마다 큰 안내 배너(후드 탈거·장착 …)까지 띄우면 시나리오 안내와 겹친다.
                // 작업 절차 설명(hint)은 그 부위 볼트를 처음 돌릴 때 한 번만 보여 준다(아래 UpdateVisuals).
                _ = body;
                // 볼트가 실제로 시선 가까이 있을 때만 경고 — 큰 부품(배터리 팩·하부 커버)의 범위 상자가 휠 쪽까지 걸쳐 있어서,
                // 타이어를 조립하려고 휠을 겨누기만 해도 '고전압 차단 전' 경고가 떴다(범위 상자 시선 = hover 후보)
                if (HvUnsafe(fd) && PartAngle(_parts[focus], cam.transform.position, cam.transform.forward, near) < (IsVRPlatform ? 28f : 16f))
                    NoticeHv(fd, 4f, 20f);
            }
        }

        bool hvTouching = false;

        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            if (pt == null || pt.anchor == null) continue;
            int st = StateOf(p);
            bool on = p == focus;
            bool anyActive = false;
            int done = 0;

            if (pt.bolts != null)
            {
                bool working = PartActive(p);   // 다른 사람이 작업 중인 부위도 볼트는 보이게
                for (int n = 0; n < pt.bolts.Length; n++)
                {
                    var b = pt.bolts[n];
                    if (b == null) continue;
                    int idx = s_off[p] + n;
                    if (b.isloosened) done++;
                    if (b.gameObject.activeInHierarchy)
                    {
                        bool vis = on || working || b.IsMarkedDone;
                        ShowBolt(pt, n, b, vis, vis || (!b.isloosened && ToolDist(b.transform.position) < ToolNear));
                    }
                    bool open = b.gameObject.activeInHierarchy && !b.isloosened;
                    bool active = open && b.Progress > 0f && Time.time - _activeAt[idx] < 0.5f;
                    if (active) anyActive = true;
                    // 드릴이 물고 있는 볼트는 드릴이 진행 링을 그린다 — 같은 볼트에 링이 두 겹으로 보였다
                    bool ownRing = open && on && b != ImpactWrench.LocalTargetBolt;
                    DrawRing(pt, n, b, ownRing, active, cam);
                    SetOutline(pt, n, b, open && on ? (active ? 2 : 1) : 0);
                }
            }
            if (anyActive && on && st == (int)PartState.Tight && CarPartAccident.IsHeavy(pt.def.id)) DropRiskNotice(pt, p, done);   // 낙하 사고 직전 안내(보라)
            if (anyActive && on && HvUnsafe(pt.def))   // 차단 안 된 채 고전압 부품 볼트를 돌리는 중 — 경고, 계속하면 감전 사고
            {
                NoticeHv(pt.def, 3f, 8f);
                hvTouching = true;
                if (_hvTouchT <= 0.01f && Time.time >= _hvSparkAt && FreeLoto.IsFreePractice)   // 비절연 공구(임팩트 렌치)를 고전압 부위에 댔다 — 스파크와 경고음
                {
                    _hvSparkAt = Time.time + 6f;
                    AccidentFx.SmallSpark(HvAccidentPoint(pt));
                    EquipFx.Notice("hv_noninsulated", "비절연 공구 — 고전압 근처", "임팩트 렌치는 절연 공구가 아닙니다 (감점 -15). 절연 공구를 쓰고 먼저 고전압을 차단하세요", EquipFx.Danger, 4.5f, 6f);
                }
                var smg = ElectricSafetyManager.Instance;
                _hvTouchT += Time.deltaTime * (smg != null && !smg.isGlovesEquipped ? 1.6f : 1f);   // 절연 장갑이 없으면 더 빨리 감전
                if (_hvTouchT >= HvShockDelay) { _hvTouchT = 0f; if (FreeLoto.IsFreePractice) FreeLoto.TriggerShock(pt.def, HvShockReason(), HvAccidentPoint(pt)); }   // 교육·평가 시나리오 중의 사고 판정은 시나리오 쪽이 한다
            }
            else if (anyActive && on && CarPartRules.NeedsToolWarning(pt.def, pt.anchor != null ? pt.anchor.position : Vector3.zero)) { }   // 특수공구·크레인 없이 작업 중(경고만)
            else if (anyActive && on)   // 작업 절차 설명은 볼트를 처음 돌릴 때 한 번(부위별 3분에 한 번까지)
                EquipFx.Notice("carpart_" + pt.def.id, pt.def.name + (st == (int)PartState.Mounted ? " 장착" : " 탈거"),
                    st == (int)PartState.Mounted ? $"{pt.def.tool}로 규정 {pt.def.tMin:0.0#} ~ {pt.def.tMax:0.0#} kgf·m까지 조이세요" : pt.def.hint,
                    EquipFx.Info, 4f, 180f);
            if ((cardPart >= 0 && p != cardPart) || TireService.LabelShowing || pt.yieldToScenario) { pt.flashUntil = 0f; pt.cardUntil = 0f; }   // 다른 부위·휠 카드가 떠 있으면 내린다
            UpdateCard(pt, p, st, done, anyActive && on, p == cardPart && !TireService.LabelShowing, cam);
            UpdateLeads(pt, on && pt.card != null && pt.card.root.activeSelf);
        }
        if (hvTouching) _hvLastTouch = Time.time;
        else if (HvSafeNow()) _hvTouchT = Mathf.Max(0f, _hvTouchT - Time.deltaTime * 3f);   // 차단 절차를 마쳤을 때만 내려간다 — 쉬었다 다시 해도 감전 위험은 그대로 쌓인다(전에는 손을 떼면 줄어 간격을 두고 계속 풀 수 있었다)
    }

    /// <summary>감전 위험 게이지(0~1) — 안전 절차 없이 고전압 부위 볼트를 돌린 시간 / 사고까지 시간. HUD(HvShockGauge)가 읽는다.</summary>
    public float HvTouchRatio => Time.time - _hvLastTouch < 8f ? Mathf.Clamp01(_hvTouchT / HvShockDelay) : 0f;   // 화면에는 만지는 중·직후 8초만 보인다(값은 계속 쌓여 있다)
    public bool HvTouchHolding => Time.time - _hvLastTouch > 0.5f;
    public string HvTouchWhy => HvShockReason();

    private float _hvLastTouch = -999f;

    private static bool HvSafeNow()
    {
        var sm = ElectricSafetyManager.Instance;
        return sm == null || (sm.IsSafeToWork() && !HvVoltageCheck.NeedsCheck);
    }

    // ── 대형 부품 낙하 위험 — 받침 없이 마지막 볼트 몇 개만 남았을 때(사고 직전) 미리 알린다 ──
    private float _supportCheckAt;
    private bool _supportCached;

    private void DropRiskNotice(Part pt, int p, int loosened)
    {
        if (!FreeLoto.IsFreeScenario) return;
        int bn = pt.bolts != null ? pt.bolts.Length : 0;
        if (bn < 2) return;
        int remain = bn - loosened;
        if (remain > Mathf.Max(1, bn / 2)) return;   // 절반 넘게 풀었을 때부터
        if (Time.time >= _supportCheckAt) { _supportCheckAt = Time.time + 1f; _supportCached = CarPartAccident.Supported(PartCenter(p)); }
        if (_supportCached) return;
        string what = CarPartAccident.IsBatteryPart(pt.def.id) ? "부품이 떨어져 부서지고 배터리 화재(열폭주)가 납니다" : "부품이 떨어져 부서집니다";
        EquipFx.Notice("drop_risk_" + pt.def.id, "낙하 위험 — " + pt.def.name,
            $"받침(크레인·배터리 리프트)이 없습니다 — 볼트 {remain}개만 더 풀면 {what}. 멈추고 먼저 차 밑에 받치세요", EquipFx.Hazard, 4.5f, 6f);
        MentorDialogue.Say("support_need", 90f);
    }

    // 고전압 부품 볼트를 안전 절차 없이 이 시간(초) 넘게 돌리면 감전 사고 — 경고(NoticeHv)를 본 뒤 멈출 시간은 준다
    private const float HvShockDelay = 3f;
    private float _hvTouchT;
    private float _hvSparkAt;

    // 사고 지점 — 드릴이 물고 있는 볼트(없으면 이 부위의 볼트 가운데)
    private Vector3 HvAccidentPoint(Part pt)
    {
        var tb = ImpactWrench.LocalTargetBolt;
        if (tb != null && tb.gameObject.activeInHierarchy) return tb.transform.position;
        return WorkPoint(pt, out _);
    }

    private static string HvShockReason()
    {
        var sm = ElectricSafetyManager.Instance;
        if (sm == null) return "고전압 차단 절차를 하지 않았습니다.";
        if (!sm.isGlovesEquipped) return "절연 장갑을 착용하지 않고 고전압 부품을 만졌습니다.";
        if (!sm.isMSDRemoved) return "고전압 서비스 플러그(MSD)를 차단하지 않고 고전압 부품을 만졌습니다.";
        return "단자 잔류 전압(30V 이하)을 확인하지 않고 고전압 부품을 만졌습니다.";
    }

    // ══════════════════════════════════════════
    //  부품 단위 강조 — Car Mechanic Simulator VR 영상 참고
    //   · 볼트 공구를 들고 부품을 바라보면 그 부품 윤곽선(노랑) + 이름·접근 위치 카드
    //   · 떼어낸 부품의 자리에는 반투명 흰 고스트가 계속 남는다(무엇이 빠졌는지, 어디에 끼우는지)
    // ══════════════════════════════════════════

    private int _hover = -1;
    private float _boundsAt;
    private GameObject _hoverObj;
    private Outlinable _hoverOutline;
    private int _hoverBuilt = -1;
    private Material _hoverMat, _slotMat;
    private readonly Dictionary<int, GameObject> _slotGhosts = new Dictionary<int, GameObject>();

    private static string AccessOf(PartDef d)
    {
        if (!string.IsNullOrEmpty(d.parent)) return "떼어낸 어셈블리를 바닥에 내려놓고 작업";
        switch (d.id)
        {
            case "FrontUnderCover": case "RearUnderCover": case "FrontMotor": case "RearMotor":
            case "FrontSuspension": case "RearSuspension": case "RearAxleL": case "RearAxleR": case "RearLowerArmL": case "RearLowerArmR": case "RearTrailingArmL": case "RearTrailingArmR": case "RearShockL": case "RearShockR": case "SteeringRack": case "FrontStabilizer": case "RearStabilizer": case "FrontLowerArmL": case "FrontLowerArmR": case "RearJunctionBlock": case "BatteryPackCover": case "BatteryPack":
                return "차 밑 — 리프트로 올리고 작업";
            case "BrakeFL": case "BrakeFR": case "BrakeRL": case "BrakeRR": return "휠 안쪽 — 해당 휠을 떼고 작업";
            case "SeatFL": case "SeatFR": case "SeatRear": case "Cockpit": case "Hvac": return "실내 — 도어를 열고 작업";
            case "Iccu": return "실내 — 뒷좌석을 떼고 좌석 밑에서 작업";
            case "Hood": case "Frunk": case "Headlamp": case "FrontBeam": case "Cooling": case "AcCompressor": case "CoolantTankMotor": case "CoolantTankBattery":
            case "FrontInverter": case "JunctionBlock": case "Battery12VCover": case "Battery12V":
            case "FrontStrutL": case "FrontStrutR": case "Vcu":
                return "엔진룸 — 후드를 열고 작업";
            default: return "차 바깥에서 작업";
        }
    }

    private static void RefreshBounds(Part pt)
    {
        if (pt == null || pt.rends == null) return;
        bool any = false; Bounds b = default;
        foreach (var r in pt.rends)
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        pt.wb = b; pt.wbOk = any;
    }

    /// <summary>볼트 공구를 든 채 시선(화면 가운데)이 가리키는, 지금 작업할 수 있는 부품. 안 쪽에 가려 접근 못 하는 부품은 제외.</summary>
    private int PickHover(Camera cam)
    {
        _hover = -1;
        if (!LocalHoldsBoltTool) return -1;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        int best = -1; float bd = 3.5f, bv = float.MaxValue;
        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            if (pt == null || !pt.wbOk || pt.snap != null || pt.yieldToScenario) continue;
            int st = StateOf(p);
            if (st == (int)PartState.Off) continue;
            if (st == (int)PartState.Tight && !CanRemove(p)) continue;
            if (!pt.wb.IntersectRay(ray, out float dist) || dist > bd + 0.05f) continue;
            float vol = pt.wb.size.x * pt.wb.size.y * pt.wb.size.z;
            // 거의 같은 거리면 더 작은(구체적인) 부품 — 후드 안의 프렁크·모터처럼
            if (dist < bd - 0.05f || vol < bv) { bd = Mathf.Min(bd, dist); bv = vol; best = p; }
        }
        _hover = best;
        return best;
    }

    // 부품 전체 윤곽선(노란 선) — 시야를 어지럽힌다는 의견(26.11.16)으로 끔. 부품 이름은 카드와 볼트 진행 패널 제목에 나온다.
    private const bool ShowPartOutline = false;

    private void UpdateHover(int hover)
    {
        if (!ShowPartOutline) { if (_hoverObj != null && _hoverObj.activeSelf) _hoverObj.SetActive(false); return; }
        if (hover < 0)
        {
            if (_hoverObj != null && _hoverObj.activeSelf) _hoverObj.SetActive(false);
            return;
        }
        if (_hoverObj == null) _hoverObj = new GameObject("[CarPartHover]");
        if (_hoverBuilt != hover)
        {
            // 부품 메시를 복사해 반투명으로 덮으면 원래 메시와 같은 자리라 깊이가 겹쳐 줄무늬(z-fighting)가 생겼다
            // → 복사본 없이 진짜 부품 렌더러에 윤곽선만 건다(렌더러 목록을 비우려고 윤곽선 오브젝트를 통째로 새로 만든다)
            foreach (Transform c in _hoverObj.transform) Destroy(c.gameObject);
            _hoverOutline = null;
            _hoverBuilt = -1;
            var pt = _parts[hover];
            if (pt == null || pt.rends == null) { _hoverObj.SetActive(false); return; }
            var oGo = new GameObject("Outline");
            oGo.transform.SetParent(_hoverObj.transform, false);
            try
            {
                var o = oGo.AddComponent<Outlinable>();
                foreach (var r in pt.rends) if (r != null && (r is MeshRenderer || r is SkinnedMeshRenderer)) o.AddRenderer(r);
                o.RenderStyle = RenderStyle.Single;
                var prm = o.OutlineParameters; prm.Color = new Color(1f, 0.85f, 0.25f, 1f); prm.Enabled = true;
                prm.DilateShift = 0.25f; prm.BlurShift = 0f;
                _hoverOutline = o;
                _hoverBuilt = hover;
            }
            catch (System.Exception e) { Debug.LogWarning("[CarPartService] 부품 윤곽선 실패: " + e.Message); _hoverOutline = null; }
        }
        if (_hoverBuilt < 0) { _hoverObj.SetActive(false); return; }
        if (!_hoverObj.activeSelf) _hoverObj.SetActive(true);
    }

    // 떼어낸 부품 자리의 반투명 고스트 — 가까이 있을 때만(8m)
    // 11/17 피드백: 분해한 자리에 하얀 반투명 부품이 계속 남아 보기 어렵다 → 평소엔 끄고,
    // 떼어낸 부품(CarPartItem)을 들고 자리 근처로 올 때만 ShowMountGhost 가 그리는 고스트(파랑→초록)가 뜬다.
    private const bool ShowSlotGhosts = false;

    private void UpdateSlotGhosts(Camera cam)
    {
        if (!ShowSlotGhosts) { HideSlotGhosts(); return; }
        if (_slotMat == null) { _slotMat = TireService.MakeGhostMaterial(); _slotMat.color = new Color(0.85f, 0.95f, 1f, 0.10f); }
        for (int p = 0; p < _parts.Length; p++)
        {
            bool want = _parts[p] != null && _parts[p].anchor != null && StateOf(p) == (int)PartState.Off && _parts[p].snap == null
                        && (PartCenter(p) - cam.transform.position).sqrMagnitude < 8f * 8f;
            _slotGhosts.TryGetValue(p, out var g);
            if (!want) { if (g != null && g.activeSelf) g.SetActive(false); continue; }
            if (g == null)
            {
                g = new GameObject($"[CarPartSlot {Defs[p].id}]");
                if (!BuildCopy(p, g.transform, _slotMat, out _)) { Destroy(g); continue; }
                _slotGhosts[p] = g;
            }
            if (!g.activeSelf) g.SetActive(true);
            g.transform.SetPositionAndRotation(PartCenter(p), PartRotation(p));
        }
    }

    // 시나리오가 다루는 볼트(드릴이 문 볼트·안내 중인 볼트 묶음)가 자유 실습 부위 가까이(범위 + 0.4m)에 있으면 그 부위는 표시를 양보한다.
    // 같은 하부 커버를 시나리오 볼트와 자유 실습 볼트가 각자 따로 다뤄서, 시나리오 중에 두 카드·두 볼트 세트가 한 화면에 겹쳤다.
    private NextActionGuide _nag;
    private float _yieldAt;
    private readonly List<Vector3> _scnBolts = new List<Vector3>();

    private void RefreshYield()
    {
        if (Time.unscaledTime < _yieldAt) return;
        _yieldAt = Time.unscaledTime + 0.25f;
        foreach (var pt in _parts) if (pt != null) pt.yieldToScenario = false;
        if (!ScenarioRunning) return;

        _scnBolts.Clear();
        var tb = ImpactWrench.LocalTargetBolt;
        if (tb != null && tb.gameObject.activeInHierarchy && !tb.transform.IsChildOf(transform)) _scnBolts.Add(tb.transform.position);
        if (_nag == null) _nag = FindFirstObjectByType<NextActionGuide>();
        var g = _nag != null ? _nag.FocusGroup : null;
        if (g != null)
            foreach (var b in g.GetComponentsInChildren<Bolt>(false))
                if (b != null && !b.isloosened) _scnBolts.Add(b.transform.position);
        if (_scnBolts.Count == 0) return;

        foreach (var pt in _parts)
        {
            if (pt == null || !pt.wbOk) continue;
            var bb = pt.wb; bb.Expand(0.8f);   // 사방 0.4m
            foreach (var bp in _scnBolts) if (bb.Contains(bp)) { pt.yieldToScenario = true; break; }
        }
    }

    private void HideSlotGhosts()
    {
        foreach (var kv in _slotGhosts) if (kv.Value != null && kv.Value.activeSelf) kv.Value.SetActive(false);
    }

    /// <summary>시나리오(교육·평가)가 진행 중인가 — 이때는 자유 실습 표시를 끈다. 마지막 단계를 끝냈거나 시나리오가 없으면 false.</summary>
    public static bool ScenarioRunning
    {
        get
        {
            var r = ScenarioStateReceiver.Instance;
            if (r == null || r.CurrentScenario == null) return false;
            var t = r.CurrentTaskState; int total = r.TotalTaskCount;
            return !(total > 0 && t.taskIndex >= total - 1 && t.status == TaskStatus.Completed);
        }
    }

    private void DestroyHighlights()
    {
        if (_hoverObj != null) Destroy(_hoverObj);
        _hoverObj = null; _hoverOutline = null; _hoverBuilt = -1;
        foreach (var kv in _slotGhosts) if (kv.Value != null) Destroy(kv.Value);
        _slotGhosts.Clear();
    }

    // 고전압 부품인데 안전 상태(보호구 + 서비스 플러그 차단)가 아니다 — 막지는 않고 경고만 (자유 실습·시나리오 모두 같은 기준: ElectricSafetyManager)
    private static bool HvUnsafe(PartDef d)
    {
        if (d == null || !d.hv) return false;
        var sm = ElectricSafetyManager.Instance;
        if (sm == null) return false;
        if (!sm.IsSafeToWork()) return true;
        return HvVoltageCheck.NeedsCheck;   // 보호구·MSD 는 됐지만 단자 잔류 전압(30V 이하)을 아직 안 쟀다
    }

    private static void NoticeHv(PartDef d, float show, float cooldown)
    {
        MentorDialogue.Say("hv_warn", 120f);   // 선임 정비사 코칭(시나리오에 trigger 대사가 있을 때만)
        var sm = ElectricSafetyManager.Instance;
        if (sm != null && sm.IsSafeToWork())
            EquipFx.Notice("carpart_hv", "단자 전압 확인 전입니다 — " + d.name,
                "방전 뒤 멀티미터(카트 위)로 인버터 HV 단자(청록 점)를 재서 잔류 전압이 30V 이하인지 확인한 뒤에 작업하세요 (책상 옆 보드 [단자 전압 측정]은 간편 확인)", EquipFx.Danger, show, cooldown);
        else
            EquipFx.Notice("carpart_hv", "고전압 차단 전입니다 — " + d.name,
                "절연장갑 → 12V(−) 분리 → 서비스 플러그(MSD) 분리 → 3분 방전 → 인버터 단자 전압 30V 이하 확인 뒤에 작업하세요", EquipFx.Danger, show, cooldown);
    }

    // 카드 아래 → 이 부품의 남은 볼트로 가는 가는 선 (어느 볼트가 이 부품 것인지)
    private void UpdateLeads(Part pt, bool show)
    {
        if (pt.bolts == null || pt.leads == null) return;
        Vector3 from = Vector3.zero;
        if (show)
        {
            var ct = pt.card.root.transform;
            from = ct.position - ct.up * 0.07f * ct.localScale.y;   // 카드 아래 가장자리 근처
        }
        for (int n = 0; n < pt.bolts.Length && n < pt.leads.Length; n++)
        {
            var b = pt.bolts[n];
            bool on = show && b != null && b.gameObject.activeInHierarchy && !b.isloosened;
            var lr = pt.leads[n];
            if (!on) { if (lr != null && lr.gameObject.activeSelf) lr.gameObject.SetActive(false); continue; }
            if (lr == null) lr = pt.leads[n] = TireService.MakeRing("[BoltLead]", pt.frame);
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            lr.widthMultiplier = 0.0015f;
            lr.positionCount = 2;
            lr.startColor = new Color(0.55f, 0.83f, 1f, 0.55f);
            lr.endColor = new Color(0.55f, 0.83f, 1f, 0.2f);
            lr.SetPosition(0, from);
            lr.SetPosition(1, b.transform.position - b.transform.up * 0.012f);
        }
    }

    private void SetOutline(Part pt, int n, Bolt b, int state)
    {
        if (b.IsMarkedDone) return;
        if (pt.outState[n] == state) return;
        pt.outState[n] = state;
        if (state == 0)
        {
            if (pt.outlines[n] != null) pt.outlines[n].enabled = false;
            return;
        }
        var o = pt.outlines[n] != null ? pt.outlines[n] : (pt.outlines[n] = TireService.EnsureOutline(b));
        o.enabled = true;
        var prm = o.OutlineParameters;
        prm.Color = state == 2 ? WorkOutline : IdleOutline;
        prm.Enabled = true;
    }

    private void DrawRing(Part pt, int n, Bolt b, bool show, bool active, Camera cam)
    {
        var ring = pt.rings[n];
        if (!show)
        {
            if (ring != null)
            {
                if (ring.track != null && ring.track.gameObject.activeSelf) ring.track.gameObject.SetActive(false);
                if (ring.arc != null && ring.arc.gameObject.activeSelf) ring.arc.gameObject.SetActive(false);
            }
            return;
        }
        if (ring == null) ring = pt.rings[n] = new Ring { track = TireService.MakeRing("[BoltTrack]", pt.frame), arc = TireService.MakeRing("[BoltArc]", pt.frame) };

        Vector3 c = b.transform.position - b.transform.up * 0.012f;   // 볼트 머리 쪽
        Vector3 nrm = (cam.transform.position - c).normalized;
        Vector3 u = Vector3.Cross(nrm, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(nrm, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(nrm, u);
        float radius = 0.022f * Mathf.Sqrt(pt.def.boltScale), width = 0.003f;
        TireService.SetArc(ring.track, c, u, v, radius, 1f, active ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.22f));
        ring.track.widthMultiplier = width;
        if (active)
        {
            TireService.SetArc(ring.arc, c, u, v, radius, Mathf.Max(Mathf.Clamp01(b.Progress), 0.001f), WorkOutline);
            ring.arc.widthMultiplier = width * 1.4f;
        }
        else if (ring.arc.gameObject.activeSelf) ring.arc.gameObject.SetActive(false);
    }

    private Card MakeCard(Part pt)
    {
        var lb = new Card();
        var root = new GameObject($"[PartCard {pt.def.id}]");
        root.layer = 1;   // TransparentFX — VR·PC 카메라 모두 그림
        root.transform.SetParent(pt.frame, false);
        var cg = new GameObject("Canvas");
        cg.layer = 1;
        cg.transform.SetParent(root.transform, false);
        var canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)cg.transform).sizeDelta = new Vector2(340f, 98f);
        cg.transform.localScale = Vector3.one * 0.0016f;

        var bgGo = new GameObject("BG");
        bgGo.layer = 1;
        bgGo.transform.SetParent(cg.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        WorldHoverLabel.StyleCard(bgGo.AddComponent<Image>());

        var tGo = new GameObject("Text");
        tGo.layer = 1;
        tGo.transform.SetParent(cg.transform, false);
        var trt = tGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(8f, 4f); trt.offsetMax = new Vector2(-8f, -4f);
        var t = tGo.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf; else if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.richText = true;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true;   // 부품 이름·진행 줄이 카드보다 길면 줄여서 맞춘다(카드 밖으로 튀어나옴 방지)
        t.fontSizeMin = 12f;
        t.fontSizeMax = 28f;
        t.color = Color.white;
        t.raycastTarget = false;
        WorldHoverLabel.DrawOnTop(root);

        lb.root = root;
        lb.txt = t;
        root.SetActive(false);
        return lb;
    }

    private void Flash(Part pt, string text)
    {
        pt.flashText = text;
        pt.flashUntil = Time.time + 1.8f;
        pt.cardUntil = Mathf.Max(pt.cardUntil, pt.flashUntil);
    }

    private void UpdateCard(Part pt, int p, int st, int done, bool anyActive, bool focused, Camera cam)
    {
        if (anyActive) pt.cardUntil = Time.time + 2.5f;
        if (focused) pt.cardUntil = Mathf.Max(pt.cardUntil, Time.time + 0.3f);
        else if (Time.time >= pt.flashUntil) pt.cardUntil = 0f;   // 다른 부위를 겨누면 바로 내린다(완료 알림만 잠깐 남김)
        if (Time.time >= pt.cardUntil)
        {
            if (pt.card != null && pt.card.root.activeSelf) pt.card.root.SetActive(false);
            return;
        }
        if (pt.card == null) pt.card = MakeCard(pt);
        var lb = pt.card;
        if (!lb.root.activeSelf) lb.root.SetActive(true);

        var d = pt.def;
        int total = d.bolts.Length;
        string text;
        if (Time.time < pt.flashUntil && !string.IsNullOrEmpty(pt.flashText)) text = pt.flashText;
        else if (st == (int)PartState.Off)
        {
            string blk = MountBlockers(p);
            text = string.IsNullOrEmpty(blk)
                ? $"<b>{d.name} 탈거됨</b>\n<size=62%><color=#8FD3FF>부품을 제자리에 대면 장착 단계로</color></size>"
                : $"<b>{d.name} 탈거됨</b>\n<size=62%><color=#FFC870>먼저 {blk} 장착</color></size>";
        }
        else if (st == (int)PartState.Mounted)
            text = $"<b>{d.name}</b> 장착 — 볼트 조임 {done}/{total}\n<size=62%><color=#8FD3FF>{d.tool} · {d.tMin:0.0#}~{d.tMax:0.0#} kgf·m</color></size>";
        else if (!CanRemove(p))
            text = $"<b>{d.name}</b>\n<size=62%><color=#FFC870>먼저 {RemoveBlockers(p)} 탈거</color></size>";
        else
            text = $"<b>{d.name}</b> 탈거 — 볼트 풀림 {done}/{total}\n<size=62%><color=#8FD3FF>{d.tool}</color></size>";
        // 어디서 접근하는 부품인지 — 영상(Car Mechanic Simulator VR)의 'Accessible from under the car' 안내처럼
        if (Time.time >= pt.flashUntil) text += $"\n<size=55%><color=#9BE8B0>접근: {AccessOf(d)}</color></size>";
        if (lb.text != text) { lb.text = text; lb.txt.text = text; }

        Vector3 wp = WorkPoint(pt, out var outward);
        var tr = lb.root.transform;
        tr.position = wp + outward * 0.18f + Vector3.up * 0.18f;
        tr.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        float dist = Vector3.Distance(cam.transform.position, tr.position);
        tr.localScale = Vector3.one * Mathf.Clamp(dist / 2.4f, 0.3f, 0.65f);   // 가까이 가면 글씨가 화면을 덮던 문제 — 상한을 낮춤
    }

    // ══════════════════════════════════════════
    //  부품 메시 복사 — 떼어낸 부품·고스트·스냅 연출 공용
    // ══════════════════════════════════════════

    /// <summary>
    /// 부위 p 의 차 메시들을 parent 아래에 복사한다. parent 의 원점 = 부품 중심, 회전 = 기준 오브젝트 회전(크기 1)으로 본다.
    /// mat 이 있으면 모든 재질을 그것으로 바꾼다(고스트). 복사본 전체의 로컬 범위를 bounds 로 돌려준다.
    /// </summary>
    public bool BuildCopy(int p, Transform parent, Material mat, out Bounds bounds)
    {
        bounds = default;
        var pt = _parts != null && p >= 0 && p < _parts.Length ? _parts[p] : null;
        if (pt == null || pt.anchor == null) return false;
        Vector3 refPos = pt.anchor.TransformPoint(pt.centerLocal);
        Quaternion inv = Quaternion.Inverse(pt.anchor.rotation);
        bool any = false;
        foreach (var r in pt.rends)
        {
            var mr = r as MeshRenderer;
            if (mr == null) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var go = new GameObject(mr.name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = inv * (mr.transform.position - refPos);
            go.transform.localRotation = inv * mr.transform.rotation;
            go.transform.localScale = mr.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var cmr = go.AddComponent<MeshRenderer>();
            if (mat != null)
            {
                var arr = new Material[Mathf.Max(1, mr.sharedMaterials.Length)];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                cmr.sharedMaterials = arr;
                cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else cmr.sharedMaterials = mr.sharedMaterials;

            // 메시 범위 8 꼭짓점을 부품 로컬로
            var mb = mf.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 lp = go.transform.localPosition + go.transform.localRotation * Vector3.Scale(c, go.transform.localScale);
                if (!any) { bounds = new Bounds(lp, Vector3.zero); any = true; } else bounds.Encapsulate(lp);
            }
        }
        return any;
    }

    // ══════════════════════════════════════════
    //  장착 고스트 · 스냅 (클라이언트)
    // ══════════════════════════════════════════

    public float MountDistance => IsVRPlatform ? mountDistVR : mountDistPC;
    public float GhostDistance => IsVRPlatform ? ghostDistVR : ghostDistPC;

    private GameObject _ghost;
    private int _ghostBuilt = -1;
    private Material _ghostMat;
    private Outlinable _ghostOutline;
    private int _ghostPart = -1;
    private int _ghostFrame = -10;
    private bool _ghostInRange;

    /// <summary>CarPartItem 이 부품을 들고 있는 동안 매 프레임 부른다.</summary>
    public void ShowMountGhost(int part, bool inRange)
    {
        _ghostPart = part;
        _ghostInRange = inRange;
        _ghostFrame = Time.frameCount;
    }

    private void HideGhost()
    {
        if (_ghost != null && _ghost.activeSelf) _ghost.SetActive(false);
    }

    private void UpdateGhost()
    {
        bool want = Time.frameCount - _ghostFrame <= 1 && _ghostPart >= 0 && _ghostPart < Defs.Length && StateOf(_ghostPart) == (int)PartState.Off;
        if (!want) { HideGhost(); return; }

        if (_ghost == null)
        {
            _ghost = new GameObject("[CarPartGhost]");
            _ghostMat = TireService.MakeGhostMaterial();
        }
        if (_ghostBuilt != _ghostPart)
        {
            foreach (Transform c in _ghost.transform) Destroy(c.gameObject);
            if (_ghostOutline != null) { Destroy(_ghostOutline); _ghostOutline = null; }
            _ghost.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (!BuildCopy(_ghostPart, _ghost.transform, _ghostMat, out _)) { HideGhost(); return; }
            _ghostBuilt = _ghostPart;
            try
            {
                _ghostOutline = _ghost.AddComponent<Outlinable>();
                _ghostOutline.AddAllChildRenderersToRenderingList();
                _ghostOutline.RenderStyle = RenderStyle.Single;
            }
            catch (System.Exception) { _ghostOutline = null; }
        }
        if (!_ghost.activeSelf) _ghost.SetActive(true);
        _ghost.transform.SetPositionAndRotation(PartCenter(_ghostPart), PartRotation(_ghostPart));

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        Color col = _ghostInRange ? new Color(0.25f, 1f, 0.45f) : new Color(0.45f, 0.8f, 1f);
        col.a = (_ghostInRange ? 0.3f : 0.16f) + 0.08f * pulse;
        _ghostMat.color = col;
        if (_ghostOutline != null)
        {
            var prm = _ghostOutline.OutlineParameters;
            prm.Color = _ghostInRange ? new Color(0.3f, 1f, 0.5f, 1f) : new Color(0.55f, 0.85f, 1f, 1f);
            prm.Enabled = true;
        }
    }

    // 놓은 부품의 자세 — 스냅 연출의 시작점(놓은 사람 화면에서만 알고, 다른 화면은 바깥쪽 기본 거리에서 시작)
    private static int s_snapPart = -1;
    private static Vector3 s_snapFrom;
    private static Quaternion s_snapRot;
    private static float s_snapTime;

    public void NoteMountPose(int part, Vector3 pos, Quaternion rot)
    {
        s_snapPart = part; s_snapFrom = pos; s_snapRot = rot; s_snapTime = Time.time;
    }

    private void StopSnap(Part pt)
    {
        if (pt.snap != null) { StopCoroutine(pt.snap); pt.snap = null; }
        if (pt.snapObj != null) { Destroy(pt.snapObj); pt.snapObj = null; }
    }

    private IEnumerator SnapRoutine(Part pt)
    {
        int p = pt.index;
        Vector3 endPos = PartCenter(p);
        Quaternion endRot = PartRotation(p);
        Vector3 outW; WorkPoint(pt, out outW);
        Vector3 startPos = endPos + outW * 0.4f;
        Quaternion startRot = Quaternion.AngleAxis(10f, Vector3.up) * endRot;
        if (s_snapPart == p && Time.time - s_snapTime < 4f && (s_snapFrom - endPos).magnitude < 2.5f) { startPos = s_snapFrom; startRot = s_snapRot; }

        var go = new GameObject("[CarPartSnap]");
        go.transform.SetPositionAndRotation(startPos, startRot);
        BuildCopy(p, go.transform, null, out _);
        pt.snapObj = go;

        float dur = Mathf.Max(0.1f, snapSeconds);
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (pt.anchor == null) yield break;
            float t = Mathf.Clamp01(e / dur);
            endPos = PartCenter(p); endRot = PartRotation(p);
            go.transform.position = Vector3.LerpUnclamped(startPos, endPos, TireService.EaseOutBack(t));
            go.transform.rotation = Quaternion.Slerp(startRot, endRot, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        Destroy(go);
        pt.snapObj = null;
        SetVisible(pt, true);
        if (pt.bolts != null) foreach (var b in pt.bolts) if (b != null) { b.gameObject.SetActive(true); StartCoroutine(TireService.ScaleIn(b.transform, 0.05f, 0.25f)); }
        pt.snap = null;
        AudioSource.PlayClipAtPoint(EquipFx.Click, endPos, 1f);
        AudioSource.PlayClipAtPoint(EquipFx.Crack, endPos, 0.3f);
    }

    /// <summary>떼어낸 부품 오브젝트(ObjectId)가 어느 부위인지. 모르면 -1.</summary>
    public int PartOfItem(int objectId)
    {
        for (int i = 0; i < _item.Count; i++) if (_item[i] == objectId) return i;
        return -1;
    }

    /// <summary>pos 근처의, 이 부위 자리가 비어 있는가(장착 판정용).</summary>
    public bool IsFreeSlotNear(int p, Vector3 pos, float maxDist)
    {
        if (!_found || p < 0 || p >= Defs.Length || StateOf(p) != (int)PartState.Off) return false;
        return (PartCenter(p) - pos).sqrMagnitude <= maxDist * maxDist;
    }

    public string MountBlockersOf(int p) => MountBlockers(p);

    // ══════════════════════════════════════════
    //  서버
    // ══════════════════════════════════════════

    [ServerRpc(RequireOwnership = false)]
    private void ProgressServerRpc(int idx, float v, bool assemble)
    {
        if (idx < 0 || idx >= _prog.Count) return;
        int p = PartOfBolt(idx);
        if (p < 0 || p >= _state.Count) return;
        int st = _state[p];
        if (st == (int)PartState.Off) return;
        if (assemble != (st == (int)PartState.Mounted)) return;   // 조이던 진행도가 체결 뒤에 왔다(또는 그 반대) — 이전 상태의 것이라 버린다
        if (Time.time < _lockUntil[p]) return;                    // 체결 직후 잠깐은 풀기 진행도를 받지 않는다
        if (st == (int)PartState.Tight && !CanRemove(p)) return;
        v = Mathf.Clamp01(v);
        if (v <= _prog[idx]) return;
        _prog[idx] = v;
        FinishPart(p);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PartDoneServerRpc(int p, int expectState)
    {
        if (p < 0 || p >= _state.Count || _state[p] != expectState || expectState == (int)PartState.Off) return;
        if (expectState == (int)PartState.Tight && !CanRemove(p)) return;
        for (int n = 0; n < Defs[p].bolts.Length; n++) _prog[s_off[p] + n] = 1f;
        FinishPart(p);
    }

    [Server]
    private void FinishPart(int p)
    {
        int k = Defs[p].bolts.Length;
        for (int n = 0; n < k; n++) if (_prog[s_off[p] + n] < 1f) return;

        for (int n = 0; n < k; n++) _prog[s_off[p] + n] = 0f;
        if (_state[p] == (int)PartState.Mounted)
        {
            _state[p] = (int)PartState.Tight;
            _lockUntil[p] = Time.time + TightenLock;
            CarPartRules.ServerPartTightened(Defs[p].id, id => { int q = IndexOf(id); return q >= 0 && _state[q] == (int)PartState.Off; });
            return;
        }
        _state[p] = (int)PartState.Off;
        ClearLube(p);
        string pid = Defs[p].id;
        bool circuitWasClosed = CarPartRules.CoolantAirValue == 0;
        CarPartRules.ServerPartRemoved(pid);
        if (_freeMode && circuitWasClosed && (CarPartRules.IsCoolantCircuit(pid) || pid.StartsWith("CoolantTank"))) ServerSpill(p);   // 냉각수 누출
        // 크레인·리프트로 받치지 않고 대형 부품의 마지막 볼트를 풀었다 → 그대로 떨어져 부서진다
        if (_freeMode && CarPartAccident.IsHeavy(pid) && !CarPartAccident.Supported(PartCenter(p)))
        {
            ServerShatter(p, "크레인·배터리 리프트로 받치지 않고 볼트를 풀어 부품이 그대로 아래로 떨어졌습니다", null, null);
            return;
        }
        SpawnItem(p);
    }

    // ══════════════════════════════════════════
    //  사고 — 대형 부품 낙하·충격, 냉각수 누출 (자유 탈거 모드)
    // ══════════════════════════════════════════
    private bool _freeMode;            // 서버: 접속자 중 누군가 '지금은 자유 탈거 모드'라고 알려줌 (서버는 시나리오를 모른다)
    private int _freeSent = -1;        // 클라: 마지막으로 보낸 값
    private float _freeSentAt;
    private readonly HashSet<int> _damaged = new HashSet<int>();   // 서버: 부서진 부위(새 부품 받기 전까지 아이템을 만들지 않는다)

    /// <summary>[클라] 착용한 절연 장갑 아이템을 없앤다(시나리오처럼 착용하면 손에서 사라짐)</summary>
    public void ConsumeItem(NetworkObject nob) { if (IsSpawned && nob != null) ConsumeItemServerRpc(nob); }

    [ServerRpc(RequireOwnership = false)]
    private void ConsumeItemServerRpc(NetworkObject nob)
    {
        if (nob == null) return;
        var ti = nob.GetComponent<TaskItem>();
        if (ti == null || ti.prefabId != "InsulatedGloves") return;
        var sg = nob.GetComponent<SyncGrab>();
        if (sg == null) return;
        sg.ForceDetach();
        sg.TriggerDisappear();
    }

    /// <summary>[클라] 지금 자유 탈거 모드인지 서버에 알린다(바뀔 때, 그리고 가끔 다시)</summary>
    public void ReportFreeMode(bool on)
    {
        int v = on ? 1 : 0;
        if (!IsSpawned || (v == _freeSent && Time.unscaledTime - _freeSentAt < 15f)) return;
        _freeSent = v; _freeSentAt = Time.unscaledTime;
        FreeModeServerRpc(on);
    }

    [ServerRpc(RequireOwnership = false)]
    private void FreeModeServerRpc(bool on) { _freeMode = on; }

    [Server]
    private void DespawnItemOf(int p)
    {
        if (p < 0 || p >= _item.Count || _item[p] < 0) return;
        if (ServerManager.Objects.Spawned.TryGetValue(_item[p], out var nob) && nob != null) ServerManager.Despawn(nob);
        _item[p] = -1;
    }

    /// <summary>[서버] 부품이 부서졌다 — 아이템을 치우고 파손 처리, 모든 기기에 조각 연출(배터리면 화재 사고)</summary>
    [Server]
    private void ServerShatter(int p, string why, Vector3? at, Quaternion? rot)
    {
        string id = Defs[p].id;
        Vector3 pos = at ?? PartCenter(p);
        Quaternion q = rot ?? PartRotation(p);
        DespawnItemOf(p);
        _damaged.Add(p);
        CarPartRules.ServerDamage(id, true);
        ShatterObserversRpc(p, pos, q, CarPartAccident.IsBatteryPart(id), why);
    }

    [ObserversRpc(BufferLast = false)]
    private void ShatterObserversRpc(int p, Vector3 pos, Quaternion rot, bool fire, string why)
    {
        CarPartAccident.PlayShatter(this, p, pos, rot, fire, why);
    }

    /// <summary>[클라] 떼어낸 대형·배터리 부품이 세게 부딪쳤다(던짐·높은 데서 낙하)</summary>
    public void ReportImpact(int p, float speed)
    {
        if (IsSpawned) ImpactServerRpc(p, speed);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ImpactServerRpc(int p, float speed)
    {
        if (!_freeMode || p < 0 || p >= Defs.Length || _state[p] != (int)PartState.Off || _damaged.Contains(p)) return;
        if (!CarPartAccident.IsDamageable(Defs[p].id) || speed < CarPartAccident.BreakSpeed) return;
        Vector3? at = null; Quaternion? rot = null;
        if (_item[p] >= 0 && ServerManager.Objects.Spawned.TryGetValue(_item[p], out var nob) && nob != null) { at = nob.transform.position; rot = nob.transform.rotation; }
        ServerShatter(p, $"떼어낸 부품을 떨어뜨리거나 던져 강한 충격을 받았습니다 ({speed:0.0} m/s)", at, rot);
    }

    /// <summary>[클라] 부서진 부품 대신 새 부품을 받는다(신품 보관함)</summary>
    public void ReplaceDamaged(int p) { if (IsSpawned) ReplaceDamagedServerRpc(p); }

    [ServerRpc(RequireOwnership = false)]
    private void ReplaceDamagedServerRpc(int p)
    {
        if (p < 0 || p >= Defs.Length || _state[p] != (int)PartState.Off || !_damaged.Remove(p)) return;
        CarPartRules.ServerDamage(Defs[p].id, false);
        SpawnItem(p);
    }

    // ── 냉각수 누출 ──
    private class SpillInfo { public Vector3 pos; public float t; public bool warned, battery; }
    private readonly Dictionary<int, SpillInfo> _spills = new Dictionary<int, SpillInfo>();
    private int _spillSeq;

    [Server]
    private void ServerSpill(int p)
    {
        int id = ++_spillSeq;
        Vector3 pos = PartCenter(p);
        bool bat = Defs[p].id.Contains("Battery");
        _spills[id] = new SpillInfo { pos = pos, t = Time.time, battery = bat };
        SpillObserversRpc(id, pos, bat);
    }

    [ObserversRpc(BufferLast = false)]
    private void SpillObserversRpc(int id, Vector3 pos, bool battery)
    {
        CarPartAccident.PlaySpill(this, id, pos, battery, () => CleanSpillServerRpc(id));
    }

    [ServerRpc(RequireOwnership = false)]
    private void CleanSpillServerRpc(int id)
    {
        if (_spills.Remove(id)) CleanedObserversRpc(id);
    }

    [ObserversRpc(BufferLast = false)]
    private void CleanedObserversRpc(int id) { CarPartAccident.RemoveSpill(id); }

    [ObserversRpc(BufferLast = false)]
    private void SpillWarnObserversRpc(string title, string msg) { EquipFx.Notice("spill_warn", title, msg, EquipFx.Danger, 6f, 4f); }

    [ObserversRpc(BufferLast = false)]
    private void ThermalObserversRpc(Vector3 pos, string why)
    {
        var em = EmergencyEventManager.Instance;
        if (em != null) em.TriggerAccident(EmergencyEventManager.AccidentType.ThermalRunaway, why, pos);
        else AccidentFx.Fire(pos);
    }

    [Server]
    private void ServerSpillWatch()
    {
        if (_spills.Count == 0) return;
        List<int> done = null;
        foreach (var kv in _spills)
        {
            float age = Time.time - kv.Value.t;
            var s = kv.Value;
            if (age > 60f && !s.warned)
            {
                s.warned = true;
                SpillWarnObserversRpc("냉각수 누출 방치", "바닥의 냉각수를 닦지 않았습니다 (-5) — 계속 두면 배터리가 과열될 수 있습니다");
            }
            int bp = IndexOf("BatteryPack");
            if (age > 150f && s.battery && bp >= 0 && _state[bp] != (int)PartState.Off)
            {
                (done ?? (done = new List<int>())).Add(kv.Key);
                ThermalObserversRpc(PartCenter(bp), "냉각수 누출을 오래 방치해 배터리가 과열되었습니다.");
            }
        }
        if (done != null) foreach (int k in done) { _spills.Remove(k); CleanedObserversRpc(k); }
    }

    [Server]
    private void SpawnItem(int p)
    {
        if (itemPrefab == null) return;
        Vector3 pos; Quaternion rot;
        if (_found && _parts != null && _parts[p] != null && _parts[p].anchor != null) { pos = PartCenter(p); rot = PartRotation(p); }
        else { pos = transform.position + Vector3.up; rot = transform.rotation; }
        var nob = Instantiate(itemPrefab, pos, rot);
        if (gameObject.scene.IsValid() && nob.gameObject.scene != gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);
        ServerManager.Spawn(nob);
        _item[p] = nob.ObjectId;
        var drop = nob.GetComponent<SyncGrab>();
        if (drop != null) drop.ServerDropToFloor();   // 떼어낸 부품은 중력으로 바닥까지 떨어진다(잡기 전까지 제자리에 떠 있지 않게)
    }

    // 떼어낸 부품이 사라졌거나(디스폰·서버 재시작 잔재) 바닥 밑·아주 먼 곳으로 떨어졌으면 서버가 되돌린다 — 안 그러면 그 부위는 영영 못 끼운다
    private float _watchAt;

    [Server]
    private void ServerWatch()
    {
        if (Time.unscaledTime < _watchAt || _state.Count < Defs.Length || _item.Count < Defs.Length) return;
        _watchAt = Time.unscaledTime + 2f;
        ServerSpillWatch();
        var spawned = ServerManager.Objects.Spawned;
        for (int p = 0; p < Defs.Length; p++)
        {
            if (_state[p] != (int)PartState.Off) continue;
            if (_damaged.Contains(p)) continue;   // 부서진 부품 — 새 부품을 받기 전까지는 아이템이 없다
            NetworkObject nob = null;
            bool have = _item[p] >= 0 && spawned.TryGetValue(_item[p], out nob) && nob != null;
            if (!have) { SpawnItem(p); continue; }
            var sg = nob.GetComponent<SyncGrab>();
            if (sg != null && sg.IsGrabbed) continue;
            Vector3 c = PartCenter(p);
            if (nob.transform.position.y < c.y - 6f || (nob.transform.position - c).sqrMagnitude > 40f * 40f)
            {
                nob.transform.SetPositionAndRotation(c + Vector3.up * 0.3f, PartRotation(p));
                var rb = nob.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                Debug.LogWarning($"[CarPartService] {Defs[p].name}: 떨어진 부품을 차 옆으로 되돌렸습니다");
            }
        }
    }

    /// <summary>모든 부위를 원래대로(전부 체결된 상태) — 떼어낸 부품은 지운다. 디버그 커맨드 EV/ResetParts 가 부른다.</summary>
    [Server]
    public void ServerResetAll()
    {
        if (_state.Count < Defs.Length) return;
        var spawned = ServerManager.Objects.Spawned;
        for (int p = 0; p < Defs.Length; p++)
        {
            if (_item[p] >= 0 && spawned.TryGetValue(_item[p], out var nob) && nob != null) ServerManager.Despawn(nob);
            _item[p] = -1;
        }
        for (int i = 0; i < _prog.Count; i++) _prog[i] = 0f;
        for (int i = 0; i < _lube.Count; i++) _lube[i] = 0f;
        for (int p = 0; p < Defs.Length; p++) _state[p] = (int)PartState.Tight;
        CarPartRules.ServerClear();
        ResetObserversRpc();
    }

    [ObserversRpc(BufferLast = false)]
    private void ResetObserversRpc()
    {
        if (_parts == null) return;
        foreach (var pt in _parts) if (pt != null) { pt.builtKey = -999; }   // 부분만 풀던 볼트 진행도까지 새로 만든다
        _disturbed = false;
        RebuildAll();
    }

    // ══════════════════════════════════════════
    //  도포(구리스) — 제자리에 끼운 부위의 도포 점 (규칙·표는 CarPartLube)
    // ══════════════════════════════════════════
    private float[] _lubeLocal, _lubeSent;
    private float _lubeFlushAt, _lubeUiAt;

    public int LubeSpotCount(int p) => p >= 0 && p < Defs.Length ? CarPartLube.SpotCount(Defs[p]) : 0;
    public string LubeNameOf(int p) => p >= 0 && p < Defs.Length ? CarPartLube.InfoOf(Defs[p].id)?.what : null;

    private void EnsureLubeLocal()
    {
        EnsureOffsets();
        if (_lubeLocal == null || _lubeLocal.Length != s_lubeTotal) { _lubeLocal = new float[s_lubeTotal]; _lubeSent = new float[s_lubeTotal]; }
    }

    private float LubeValue(int idx) { EnsureLubeLocal(); return idx < 0 || idx >= s_lubeTotal ? 0f : Mathf.Max(idx < _lube.Count ? _lube[idx] : 0f, _lubeLocal[idx]); }

    /// <summary>도포 상태 — ok = 적정(1.0~1.35)인 지점, excess = 과다인 지점, total = 전체 지점</summary>
    public void LubeStatus(int p, out int ok, out int total, out int excess)
    {
        ok = 0; excess = 0; total = LubeSpotCount(p);
        for (int k = 0; k < total; k++)
        {
            float v = LubeValue(s_lubeOff[p] + k);
            if (v >= CarPartLube.Ok && v <= CarPartLube.Max) ok++; else if (v > CarPartLube.Max) excess++;
        }
    }

    /// <summary>구리스 스프레이가 부른다 — 노즐(origin)에서 dir 쪽 원뿔 안의 도포 점마다 amount 만큼 올린다. 하나라도 맞았으면 true.</summary>
    public bool LubeSpray(Vector3 origin, Vector3 dir, float range, float coneDeg, float amount)
    {
        if (!_found || _parts == null || !IsClientStarted) return false;
        EnsureLubeLocal();
        float cos = Mathf.Cos(coneDeg * Mathf.Deg2Rad);
        bool any = false;
        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            if (pt == null || pt.lubeGo == null || StateOf(p) != (int)PartState.Mounted) continue;
            for (int k = 0; k < pt.lubeGo.Length; k++)
            {
                var g = pt.lubeGo[k];
                if (g == null || !g.activeInHierarchy) continue;
                Vector3 v = g.transform.position - origin;
                float d = v.magnitude;
                if (d > range || d < 1e-3f || Vector3.Dot(v / d, dir) < cos) continue;
                int idx = s_lubeOff[p] + k;
                float cur = LubeValue(idx);
                if (cur >= CarPartLube.Cap) continue;
                _lubeLocal[idx] = Mathf.Min(CarPartLube.Cap, cur + amount);
                any = true;
            }
        }
        if (any && Time.unscaledTime >= _lubeFlushAt) { _lubeFlushAt = Time.unscaledTime + 0.25f; SendLube(false); }
        return any;
    }

    private void SendLube(bool flush)
    {
        EnsureLubeLocal();
        for (int idx = 0; idx < s_lubeTotal; idx++)
            if (_lubeLocal[idx] - _lubeSent[idx] >= (flush ? 0.01f : 0.05f)) { LubeServerRpc(idx, _lubeLocal[idx]); _lubeSent[idx] = _lubeLocal[idx]; }
    }

    private static Color LubeColor(float v)
    {
        var yellow = new Color(1f, 0.85f, 0.1f, 0.9f);
        if (v < 0.05f) return yellow;
        if (v < CarPartLube.Ok) return Color.Lerp(yellow, new Color(1f, 0.97f, 0.7f, 0.95f), v);
        if (v <= CarPartLube.Max) return new Color(0.35f, 1f, 0.5f, 0.95f);
        return new Color(1f, 0.3f, 0.2f, 0.95f);
    }

    private void CreateLubeSpots(Part pt, int n)
    {
        pt.lubeGo = new GameObject[n]; pt.lubeMat = new Material[n];
        // 빌드에서 빠지지 않는 셰이더 — 생성 파츠(EnsureGen)가 이미 쓰는 URP Lit 을 우선
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        if (sh == null) return;
        Vector3 lossy = pt.anchor.lossyScale;
        for (int k = 0; k < n; k++)
        {
            int bi = CarPartLube.BoltOf(pt.def, k);
            if (bi < 0 || bi >= pt.def.bolts.Length) continue;
            var f = pt.def.bolts[bi];
            Vector3 nrm = f.n.normalized;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.name = $"[LubeSpot {pt.def.id}-{k + 1}]";
            go.layer = pt.frame.gameObject.layer;
            go.transform.SetParent(pt.frame, false);
            go.transform.localPosition = Vector3.Scale(f.p, lossy) + nrm * 0.014f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, nrm);
            go.transform.localScale = new Vector3(0.045f, 0.012f, 0.045f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var m = new Material(sh);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", LubeColor(0f)); else m.color = LubeColor(0f);
            mr.sharedMaterial = m;
            pt.lubeGo[k] = go; pt.lubeMat[k] = m;
        }
    }

    // 제자리에 끼운(조이기 전) 부위에만 도포 점을 보인다. 색은 도포량에 따라.
    private void UpdateLubeSpots()
    {
        if (_parts == null || Time.unscaledTime < _lubeUiAt) return;
        _lubeUiAt = Time.unscaledTime + 0.1f;
        EnsureLubeLocal();
        for (int p = 0; p < _parts.Length; p++)
        {
            var pt = _parts[p];
            if (pt == null) continue;
            int n = LubeSpotCount(p);
            if (n == 0) continue;
            bool want = StateOf(p) == (int)PartState.Mounted && pt.visible && pt.anchor != null && pt.frame != null && !pt.yieldToScenario;
            if (want && pt.lubeGo == null) CreateLubeSpots(pt, n);
            for (int k = 0; k < n; k++)
            {
                int idx = s_lubeOff[p] + k;
                if (StateOf(p) != (int)PartState.Mounted) { _lubeLocal[idx] = 0f; _lubeSent[idx] = 0f; }
                if (pt.lubeGo == null || pt.lubeGo[k] == null) continue;
                var g = pt.lubeGo[k];
                if (g.activeSelf != want) g.SetActive(want);
                if (want && pt.lubeMat[k] != null) { if (pt.lubeMat[k].HasProperty("_BaseColor")) pt.lubeMat[k].SetColor("_BaseColor", LubeColor(LubeValue(idx))); else pt.lubeMat[k].color = LubeColor(LubeValue(idx)); }
            }
        }
        if (Time.unscaledTime >= _lubeFlushAt) { _lubeFlushAt = Time.unscaledTime + 0.4f; SendLube(true); }
    }

    [ServerRpc(RequireOwnership = false)]
    private void LubeServerRpc(int idx, float v)
    {
        if (idx < 0 || idx >= _lube.Count || float.IsNaN(v) || float.IsInfinity(v)) return;
        int p = PartOfLube(idx);
        if (p < 0 || p >= _state.Count || _state[p] != (int)PartState.Mounted) return;
        v = Mathf.Clamp(v, 0f, CarPartLube.Cap);
        if (v > _lube[idx]) _lube[idx] = v;
    }

    private static int PartOfLube(int idx)
    {
        EnsureOffsets();
        for (int p = Defs.Length - 1; p >= 0; p--)
        {
            int n = CarPartLube.SpotCount(Defs[p]);
            if (n > 0 && idx >= s_lubeOff[p] && idx < s_lubeOff[p] + n) return p;
        }
        return -1;
    }

    [Server]
    private void ClearLube(int p)
    {
        int n = LubeSpotCount(p);
        for (int k = 0; k < n; k++) { int idx = s_lubeOff[p] + k; if (idx < _lube.Count) _lube[idx] = 0f; }
    }

    /// <summary>부품을 놓은 클라이언트가 부른다. 자리가 비어 있고 순서가 맞으면 부품을 없애고 장착(체결 전) 상태로.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void MountServerRpc(int p, NetworkObject itemObj, NetworkConnection sender = null)
    {
        if (p < 0 || p >= _state.Count || itemObj == null || !itemObj.IsSpawned) return;
        if (_state[p] != (int)PartState.Off || _item[p] != itemObj.ObjectId) return;
        if (!CanMount(p)) return;
        var sg = itemObj.GetComponent<SyncGrab>();
        if (sg != null && sg.IsGrabbed && itemObj.Owner != sender) return;   // 남이 들고 있는 부품은 안 된다
        ServerManager.Despawn(itemObj);
        _item[p] = -1;
        for (int n = 0; n < Defs[p].bolts.Length; n++) _prog[s_off[p] + n] = 0f;
        ClearLube(p);
        _state[p] = (int)PartState.Mounted;
    }
}
