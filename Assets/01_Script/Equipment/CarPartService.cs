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
        public bool needsWheelOff; // 휠(타이어)을 하나 이상 먼저 떼야 접근되는 부위(브레이크) — TireService 상태를 본다
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

    public static readonly PartDef[] Defs =
    {
        new PartDef {
            id = "FrontBumper", name = "앞 범퍼",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "후드를 열고 윗면 볼트 → 휠하우스 쪽 볼트 → 아랫면 볼트 순으로 풀고, 범퍼를 앞으로 당겨 뺀다 (안개등·센서 커넥터 분리)",
            objects = new[] { "SM_Ioniq_Bumper" },
            bolts = new[] {
                new F(-0.4546f, -0.1471f, -0.2121f, 0f, -1f, 0f), new F(0.4452f, -0.148f, -0.2121f, 0f, -1f, 0f),
                new F(-0.9447f, 0.0055f, 0.1378f, -1f, 0f, 0f),   new F(0.9447f, 0.0055f, 0.1378f, 1f, 0f, 0f),
                new F(-0.4046f, 0.21f, -0.2621f, 0f, 1f, 0f),     new F(0.3952f, 0.2147f, -0.2621f, 0f, 1f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "앞 범퍼 장착 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "Headlamp", name = "헤드램프",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "앞 범퍼를 뗀 뒤 램프 위·앞 장착 볼트를 풀고, 커넥터를 분리해 빼낸다",
            objects = new[] { "SM_Ioniq_glass", "SM_Ioniq_Bumper_Lights" },
            bolts = new[] {
                new F(-0.7845f, -0.2858f, 0.0378f, 0f, -1f, 0f), new F(0.7751f, -0.2837f, 0.0178f, 0f, -1f, 0f),
                new F(-0.8045f, -0.2045f, 0.0453f, 0f, 0f, -1f), new F(0.7751f, -0.2045f, -0.0119f, 0f, 0f, -1f) },
            requires = new[] { "FrontBumper" },
            tMin = 1.0f, tMax = 1.4f, specPart = "헤드램프 장착 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "Hood", name = "후드",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "후드를 열고 받친 상태에서 힌지 볼트 4개를 푼다 (워셔액 호스 분리, 2인 1조로 받쳐 든다)",
            objects = new[] { "Hood_Latch_Point" },
            bolts = new[] {
                new F(-0.6247f, -0.0767f, 0.3317f, 0f, 1f, 0f), new F(-0.6247f, -0.0727f, 0.2717f, 0f, 1f, 0f),
                new F(0.6153f, -0.0767f, 0.3317f, 0f, 1f, 0f),  new F(0.6153f, -0.0727f, 0.2717f, 0f, 1f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "후드 힌지 볼트(M8)", specSource = SrcM8 },
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
            hint = "리저버 캡을 먼저 열고 드레인 플러그로 냉각수를 받아낸다 → 냉매 회수기로 냉매 회수 → A/C 라인·호스·팬 커넥터 분리 → 상단·좌우 볼트를 풀어 한 덩어리로 뺀다",
            gen = new[] {
                G.Box(-2.47f, 0.53f, 0f, 0.02f, 0.38f, 1.0f, Fin, 0.3f),                                   // 콘덴서
                G.Box(-2.435f, 0.53f, 0f, 0.03f, 0.38f, 1.0f, DarkAlu, 0.5f),                              // 라디에이터 코어
                G.Box(-2.435f, 0.53f, -0.515f, 0.05f, 0.40f, 0.03f, Plastic, 0f), G.Box(-2.435f, 0.53f, 0.515f, 0.05f, 0.40f, 0.03f, Plastic, 0f),   // 좌우 탱크
                G.Box(-2.395f, 0.53f, 0f, 0.02f, 0.38f, 0.96f, Plastic, 0f),                               // 팬 슈라우드
                G.Cyl(-2.375f, 0.53f, -0.24f, 0.32f, 0.03f, Plastic, 0f, 0f, 0f, 90f), G.Cyl(-2.375f, 0.53f, 0.24f, 0.32f, 0.03f, Plastic, 0f, 0f, 0f, 90f),   // 팬
                G.Cyl(-2.355f, 0.53f, -0.24f, 0.10f, 0.04f, DarkAlu, 0.7f, 0f, 0f, 90f), G.Cyl(-2.355f, 0.53f, 0.24f, 0.10f, 0.04f, DarkAlu, 0.7f, 0f, 0f, 90f) },   // 팬 모터
            bolts = new[] {
                new F(-2.435f, 0.73f, -0.515f, 0f, 1f, 0f), new F(-2.435f, 0.73f, 0.515f, 0f, 1f, 0f),
                new F(-2.460f, 0.40f, -0.515f, -1f, 0f, 0f), new F(-2.460f, 0.40f, 0.515f, -1f, 0f, 0f) },
            requires = new[] { "FrontBeam" },
            tMin = 1.0f, tMax = 1.4f, specPart = "냉각 모듈 장착 볼트(M6)", specSource = SrcM6 },
        new PartDef {
            id = "AcCompressor", name = "전동식 A/C 컴프레서",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)", hv = true,
            hint = "냉매 회수기로 냉매를 먼저 회수하고 고전압을 차단 → 냉각 모듈을 뗀 자리에서 고전압·저전압 커넥터, 석션·디스차지 라인 분리 → 앞쪽 마운팅 볼트 3개를 푼다",
            gen = new[] {
                G.Cyl(-2.26f, 0.41f, 0.45f, 0.13f, 0.22f, Alu, 0.7f, 90f),                                 // 본체(축 = 좌우)
                G.Box(-2.26f, 0.41f, 0.30f, 0.12f, 0.12f, 0.08f, DarkAlu, 0.6f),                           // 인버터
                G.Box(-2.26f, 0.49f, 0.30f, 0.05f, 0.04f, 0.05f, HvOrange, 0f),                            // 고전압 커넥터
                G.Cyl(-2.26f, 0.49f, 0.50f, 0.035f, 0.04f, Alu, 0.8f), G.Cyl(-2.26f, 0.49f, 0.41f, 0.03f, 0.04f, Alu, 0.8f),   // 석션·디스차지 포트
                G.Box(-2.332f, 0.44f, 0.37f, 0.012f, 0.05f, 0.04f, DarkAlu), G.Box(-2.332f, 0.44f, 0.53f, 0.012f, 0.05f, 0.04f, DarkAlu),
                G.Box(-2.332f, 0.36f, 0.45f, 0.012f, 0.05f, 0.04f, DarkAlu) },                              // 앞쪽 마운팅 브래킷
            bolts = new[] {
                new F(-2.338f, 0.44f, 0.37f, -1f, 0f, 0f), new F(-2.338f, 0.44f, 0.53f, -1f, 0f, 0f), new F(-2.338f, 0.36f, 0.45f, -1f, 0f, 0f) },
            requires = new[] { "Cooling" },
            tMin = 2.5f, tMax = 3.3f, specPart = "컴프레서 마운팅 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "DoorFL", name = "앞 도어(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 커넥터·체커를 분리한 뒤 힌지 볼트를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorFrontLeft" },
            bolts = new[] { new F(0.0613f, -0.15f, -0.546f, 1f, 0f, 0f), new F(0.0265f, 0.28f, -0.546f, 1f, 0f, 0f), new F(0.1111f, 0.06f, -0.496f, 1f, 0f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "도어 힌지 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "DoorFR", name = "앞 도어(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 커넥터·체커를 분리한 뒤 힌지 볼트를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorFrontRight" },
            bolts = new[] { new F(0.6662f, 0.8697f, -0.1497f, -1f, 0f, 0f), new F(0.6784f, 0.8697f, 0.2803f, -1f, 0f, 0f), new F(0.6498f, 0.8197f, 0.0603f, -1f, 0f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "도어 힌지 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "DoorRL", name = "뒤 도어(왼쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 커넥터·체커를 분리한 뒤 힌지 볼트를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorRearLeft" },
            bolts = new[] { new F(-0.0936f, -0.1438f, -0.5831f, 1f, 0f, 0f), new F(-0.1152f, 0.2861f, -0.5831f, 1f, 0f, 0f), new F(-0.0371f, 0.0661f, -0.5331f, 1f, 0f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "도어 힌지 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "DoorRR", name = "뒤 도어(오른쪽)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "도어를 열고 커넥터·체커를 분리한 뒤 힌지 볼트를 푼다 (2인 1조로 받쳐 든다)",
            objects = new[] { "SM_Ioniq_DoorRearRight" },
            bolts = new[] { new F(0.0936f, 0.5677f, -0.1437f, -1f, 0f, 0f), new F(0.1152f, 0.5677f, 0.2863f, -1f, 0f, 0f), new F(-0.0196f, 0.5177f, 0.0663f, -1f, 0f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "도어 힌지 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "Tailgate", name = "테일게이트",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "테일게이트를 열어 받치고 리프터·커넥터를 분리한 뒤 힌지 볼트 4개를 푼다 (2인 1조)",
            objects = new[] { "SM_Ioniq_Back" },
            bolts = new[] {
                new F(-0.5047f, -0.6045f, 1.3825f, 0f, -1f, 0f), new F(0.4953f, -0.6061f, 1.3825f, 0f, -1f, 0f),
                new F(-0.5047f, -0.604f, 1.4625f, 0f, -1f, 0f),  new F(0.4953f, -0.5877f, 1.4625f, 0f, -1f, 0f) },
            tMin = 2.5f, tMax = 3.3f, specPart = "테일게이트 힌지 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "RearBumper", name = "뒤 범퍼",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "윗면 볼트 → 휠하우스 쪽 볼트 → 아랫면 볼트 순으로 풀고, 범퍼를 뒤로 당겨 뺀다 (센서 커넥터 분리)",
            objects = new[] { "SM_Ioniq_Back.001", "SM_Ioniq_Back.002", "SM_Ioniq_Back.003", "SM_Ioniq_Back.004", "SM_Ioniq_Back.005" },
            bolts = new[] {
                new F(-0.5047f, 0.2669f, 2.2125f, 0f, -1f, 0f), new F(0.4953f, 0.2637f, 2.2125f, 0f, -1f, 0f),
                new F(-0.9242f, 0.4315f, 2.0125f, -1f, 0f, 0f), new F(0.9242f, 0.4315f, 2.0125f, 1f, 0f, 0f),
                new F(-0.4547f, 0.5976f, 2.1625f, 0f, 1f, 0f),  new F(0.4453f, 0.5985f, 2.1625f, 0f, 1f, 0f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "뒤 범퍼 장착 볼트(M6)", specSource = SrcM6 },

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
            hint = "전륜 모터·감속기를 뗀 뒤 서브 프레임 장착 볼트(4개)를 푼다 — 크레인 잭·리프트 테이블로 받치고, 스티어링 기어박스·스태빌라이저 바·드라이브 샤프트를 분리한 뒤 내린다",
            objects = new[] { "SM_FrontSuspension" },
            bolts = new[] {
                new F(0.1086f, -0.1372f, -0.4000f, 0f, -1f, 0f), new F(0.1086f, -0.1372f, 0.4000f, 0f, -1f, 0f),
                new F(0.3086f, -0.1672f, -0.4000f, 0f, -1f, 0f), new F(0.3086f, -0.1672f, 0.4000f, 0f, -1f, 0f) },
            requires = new[] { "FrontMotor", "SteeringRack", "FrontStabilizer" },
            tMin = 13.8f, tMax = 18.6f, boltScale = 1.6f, specPart = "서브 프레임 장착 볼트(M14)", specSource = "일반 조임 토크 — 플랜지 볼트 M14·8T (부위별 값 미기재)" },
        new PartDef {
            id = "RearMotor", name = "후륜 모터·감속기(IDA)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2, hv = true,
            hint = "고전압 차단 → 냉각수 배출 → 리어 언더 커버를 뗀 뒤 크레인 잭으로 받치고 마운팅 볼트를 푼다 (HV 커넥터·냉각 호스 분리, 오일 쿨러 호스 주의)",
            objects = new[] { "SM_RearElectricengine", "SM_RearPowerDistribution" },
            bolts = new[] {
                new F(-0.1098f, -0.0760f, -0.0670f, 0f, -1f, 0f), new F(-0.1098f, -0.0289f, 0.1030f, 0f, -1f, 0f),
                new F(0.0902f, -0.0830f, 0.0030f, 0f, -1f, 0f) },
            requires = new[] { "RearUnderCover" },
            tMin = 11f, tMax = 13f, boltScale = 1.4f, specPart = "후륜 모터 마운팅 볼트", specSource = "전륜 모터 마운팅 규정 준용 (후륜 값 미기재)" },
        new PartDef {
            id = "RearSuspension", name = "리어 크로스 멤버·서스펜션",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후륜 모터·감속기를 뗀 뒤 크로스 멤버 장착 볼트(4개)를 푼다 — 스태빌라이저 바·쇽 업소버·로어 암 연결을 확인하고 받쳐 내린다",
            objects = new[] { "SM_RearSuspension" },
            bolts = new[] {
                new F(-0.3596f, -0.0903f, -0.6000f, 0f, -1f, 0f), new F(-0.3596f, -0.0903f, 0.6000f, 0f, -1f, 0f),
                new F(0.2404f, -0.1373f, -0.5000f, 0f, -1f, 0f),  new F(0.2404f, -0.1373f, 0.5000f, 0f, -1f, 0f) },
            requires = new[] { "RearMotor", "RearStabilizer" },
            tMin = 8.6f, tMax = 11.6f, boltScale = 1.5f, specPart = "크로스 멤버 장착 볼트(M12)", specSource = "일반 조임 토크 — 플랜지 볼트 M12·8T (부위별 값 미기재)" },
        new PartDef {
            id = "Seats", name = "시트(앞·뒤 좌석)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "도어를 열고 시트 밑 레일 볼트를 푼다 — 시트 하부 커넥터(에어백·열선·벨트 경고)를 분리하고, 에어백 커넥터는 배터리(−) 분리 뒤에 뺀다",
            objects = new[] { "SM_Seats" },
            bolts = new[] {
                new F(-0.4256f, 0.4779f, -1.1085f, 0f, 1f, 0f), new F(-0.4256f, 0.4996f, -0.8086f, 0f, 1f, 0f), new F(-0.4256f, 0.3358f, 0.1912f, 0f, 1f, 0f),
                new F(0.3742f, 0.4552f, -1.1085f, 0f, 1f, 0f),  new F(0.3742f, 0.4765f, -0.8086f, 0f, 1f, 0f),  new F(0.3742f, 0.3349f, 0.1912f, 0f, 1f, 0f) },
            tMin = 4.9f, tMax = 6.7f, specPart = "시트 장착 볼트(M10)", specSource = "일반 조임 토크 — 플랜지 볼트 M10·8T (부위별 값 미기재)" },
        new PartDef {
            id = "Cockpit", name = "크래시 패드(대시보드)",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "12V (−) 단자를 분리한 뒤 센터 가니쉬·에어 벤트·글로브 박스를 떼고 메인 크래쉬 패드 볼트를 푼다 — 클러스터·모니터·에어백 커넥터를 분리해 들어낸다 (HVAC·블로워는 그 안쪽)",
            objects = new[] { "SM_Ioniq_Cockpit" },
            bolts = new[] {
                new F(-0.2222f, -0.5406f, -0.1607f, 0f, 0f, -1f), new F(0.0278f, -0.5406f, -0.1389f, 0f, 0f, -1f),
                new F(-0.1222f, 0.0094f, -0.1519f, 0f, 0f, -1f),
                new F(-0.2222f, 0.5594f, -0.1604f, 0f, 0f, -1f),  new F(0.0278f, 0.5594f, -0.1363f, 0f, 0f, -1f) },
            tMin = 1.0f, tMax = 1.4f, specPart = "크래시 패드 장착 볼트(M6)", specSource = SrcM6 },
        // 대시 안쪽 HVAC·블로워 — 차 모델에 없어 상자·원통으로 만든다 (영상: 블로워 모터 진동·냉매 라인 점검은 대시를 뜯어야 보인다)
        new PartDef {
            id = "Hvac", name = "HVAC·블로워 유닛",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "크래시 패드를 뗀 뒤 케이스 윗면 볼트를 푼다 — 냉매 회수를 먼저 하고 에어컨 라인(팽창밸브)·블로워 커넥터·드레인 호스를 분리해 들어낸다",
            gen = new[] {
                G.Box(-1.05f, 0.80f, 0f, 0.34f, 0.20f, 0.72f, Plastic, 0f),                                       // 케이스
                G.Box(-1.21f, 0.80f, -0.10f, 0.02f, 0.17f, 0.46f, Alu, 0.8f),                                      // 증발기 코어
                G.Cyl(-1.02f, 0.93f, 0.25f, 0.26f, 0.12f, Fin, 0.2f),                                              // 블로워 스크롤(축 = 위아래)
                G.Cyl(-1.02f, 0.99f, 0.25f, 0.10f, 0.05f, DarkAlu, 0.7f),                                          // 블로워 모터
                G.Cyl(-1.17f, 0.80f, 0.40f, 0.03f, 0.08f, Alu, 0.8f, 0f, 0f, 90f), G.Cyl(-1.17f, 0.80f, 0.32f, 0.025f, 0.08f, Alu, 0.8f, 0f, 0f, 90f) },   // 냉매 라인 포트
            bolts = new[] {
                new F(-1.17f, 0.905f, -0.28f, 0f, 1f, 0f), new F(-0.93f, 0.905f, -0.28f, 0f, 1f, 0f), new F(-1.05f, 0.905f, 0.02f, 0f, 1f, 0f) },
            requires = new[] { "Cockpit" },
            tMin = 1.0f, tMax = 1.4f, specPart = "HVAC 케이스 장착 볼트(M6)", specSource = SrcM6 },

        // ══════════════════════════════════════════════════════════════
        //  11/16 추가(2) — 차를 이루는 나머지 부품: 12V 배터리·브레이크(차 모델 메시) + 모델에 없는 하부·구동 내부 부품(생성 파츠).
        //  정비 매뉴얼 구성부품 목록(배터리 제어 시스템: 인버터·ICCU·고전압 정션 블록, 프런트/리어 서스펜션: 스티어링 기어박스·스태빌라이저 바·스트럿·쇽 업소버)을 따름.
        //  생성 파츠는 전부 외판 안쪽(프렁크·후드·언더 커버 밑)에 있어 바깥 부품을 떼기 전에는 보이지 않는다.
        //  ※ 펜더·루프·유리·미러·도어 패널 등 외판은 차 모델에서 SM_Ioniq_Electric 한 덩어리라 분리할 수 없다(모델러가 메시를 나눠야 한다).
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
            id = "BrakeSystem", name = "브레이크 시스템(캘리퍼·디스크)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2, needsWheelOff = true,
            hint = "휠을 먼저 떼고 캘리퍼 장착 볼트를 푼다 — 디스크·패드 점검은 이 상태에서 (브레이크 라인은 건드리지 않는다)",
            objects = new[] { "SM_Breaksystem" },
            bolts = new[] {
                new F(-1.4847f, -0.1256f, -0.8892f, 0f, 0f, -1f), new F(-1.4847f, -0.1256f, 0.8963f, 0f, 0f, 1f),
                new F(1.5153f, -0.1256f, -0.8899f, 0f, 0f, -1f),  new F(1.5153f, -0.1256f, 0.8944f, 0f, 0f, 1f) },
            tMin = 8.6f, tMax = 11.6f, boltScale = 1.4f, specPart = "캘리퍼 장착 볼트(M12)", specSource = "일반 조임 토크 — 플랜지 볼트 M12·8T (부위별 값 미기재)" },

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
            hint = "고전압 차단 후 프렁크를 뗀다 — HV 커넥터와 12V 출력 케이블을 분리하고 윗면 볼트 4개를 푼다 (OBC·LDC 통합 유닛)",
            gen = new[] {
                G.Box(-1.62f, 0.64f, 0.42f, 0.30f, 0.12f, 0.38f, Alu, 0.7f),
                G.Box(-1.62f, 0.705f, 0.42f, 0.24f, 0.012f, 0.32f, DarkAlu, 0.6f),
                G.Box(-1.46f, 0.66f, 0.30f, 0.04f, 0.05f, 0.07f, HvOrange, 0f) },
            bolts = new[] {
                new F(-1.72f, 0.712f, 0.28f, 0f, 1f, 0f), new F(-1.52f, 0.712f, 0.28f, 0f, 1f, 0f),
                new F(-1.72f, 0.712f, 0.56f, 0f, 1f, 0f), new F(-1.52f, 0.712f, 0.56f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 2.5f, tMax = 3.3f, specPart = "ICCU 장착 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "JunctionBlock", name = "고전압 정션 블록", hv = true,
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "고전압 차단 후 프렁크를 뗀다 — 배터리·인버터·ICCU로 가는 HV 커넥터를 모두 분리하고 윗면 볼트를 푼다 (절연 저항 점검 때 여기서 부품별 커넥터를 하나씩 뽑는다)",
            gen = new[] {
                G.Box(-1.62f, 0.63f, -0.12f, 0.22f, 0.10f, 0.28f, DarkAlu, 0.5f),
                G.Box(-1.49f, 0.64f, -0.20f, 0.03f, 0.04f, 0.06f, HvOrange, 0f), G.Box(-1.49f, 0.64f, -0.04f, 0.03f, 0.04f, 0.06f, HvOrange, 0f) },
            bolts = new[] {
                new F(-1.69f, 0.683f, -0.22f, 0f, 1f, 0f), new F(-1.55f, 0.683f, -0.22f, 0f, 1f, 0f),
                new F(-1.69f, 0.683f, -0.02f, 0f, 1f, 0f), new F(-1.55f, 0.683f, -0.02f, 0f, 1f, 0f) },
            requires = new[] { "Frunk" },
            tMin = 1.0f, tMax = 1.4f, specPart = "정션 블록 장착 볼트(M6)", specSource = SrcM6 },

        // ── 서스펜션·조향 ──
        new PartDef {
            id = "SteeringRack", name = "스티어링 기어박스",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "프런트 언더 커버를 뗀 뒤 타이로드 엔드·컬럼 조인트를 분리하고 장착 볼트를 푼다 (서브 프레임보다 먼저)",
            gen = new[] {
                G.Cyl(-1.33f, 0.42f, 0f, 0.07f, 0.85f, Alu, 0.7f, 90f, 0f, 0f),
                G.Cyl(-1.33f, 0.42f, -0.50f, 0.09f, 0.18f, Plastic, 0f, 90f, 0f, 0f), G.Cyl(-1.33f, 0.42f, 0.50f, 0.09f, 0.18f, Plastic, 0f, 90f, 0f, 0f) },
            bolts = new[] { new F(-1.33f, 0.382f, -0.20f, 0f, -1f, 0f), new F(-1.33f, 0.382f, 0.20f, 0f, -1f, 0f) },
            requires = new[] { "FrontUnderCover" },
            tMin = 4.9f, tMax = 6.7f, specPart = "스티어링 기어박스 장착 볼트(M10)", specSource = SrcM10 },
        new PartDef {
            id = "FrontStabilizer", name = "프런트 스태빌라이저 바",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "프런트 언더 커버를 뗀 뒤 부싱 브래킷 볼트 2개와 엔드 링크를 풀어 뺀다",
            gen = new[] {
                G.Cyl(-2.25f, 0.33f, 0f, 0.022f, 1.15f, DarkAlu, 0.7f, 90f, 0f, 0f),
                G.Box(-2.25f, 0.33f, -0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f), G.Box(-2.25f, 0.33f, 0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f),
                G.Cyl(-2.25f, 0.43f, -0.55f, 0.012f, 0.18f, Alu, 0.7f), G.Cyl(-2.25f, 0.43f, 0.55f, 0.012f, 0.18f, Alu, 0.7f) },
            bolts = new[] { new F(-2.25f, 0.305f, -0.30f, 0f, -1f, 0f), new F(-2.25f, 0.305f, 0.30f, 0f, -1f, 0f) },
            requires = new[] { "FrontUnderCover" },
            tMin = 2.5f, tMax = 3.3f, specPart = "스태빌라이저 부싱 볼트(M8)", specSource = SrcM8 },
        new PartDef {
            id = "FrontStrutL", name = "프런트 스트럿(왼쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후드를 열고 스트럿 타워 위 너트를 풀고 너클 쪽 볼트를 분리해 뺀다 (코일 스프링은 압축 상태 — 전용 공구 없이 분해 금지)",
            gen = new[] {
                G.Cyl(-1.75f, 0.62f, -0.60f, 0.075f, 0.50f, Alu, 0.7f), G.Cyl(-1.75f, 0.70f, -0.60f, 0.12f, 0.24f, Plastic, 0f),
                G.Box(-1.75f, 0.875f, -0.60f, 0.12f, 0.02f, 0.12f, DarkAlu, 0.6f) },
            bolts = new[] { new F(-1.80f, 0.886f, -0.60f, 0f, 1f, 0f), new F(-1.70f, 0.886f, -0.60f, 0f, 1f, 0f) },
            requires = new[] { "Hood" },
            tMin = 4.9f, tMax = 6.7f, specPart = "스트럿 상단 너트(M10)", specSource = SrcM10 },
        new PartDef {
            id = "FrontStrutR", name = "프런트 스트럿(오른쪽)",
            tool = "에어 임팩렌치(17mm 소켓)", socket = 2,
            hint = "후드를 열고 스트럿 타워 위 너트를 풀고 너클 쪽 볼트를 분리해 뺀다 (코일 스프링은 압축 상태 — 전용 공구 없이 분해 금지)",
            gen = new[] {
                G.Cyl(-1.75f, 0.62f, 0.60f, 0.075f, 0.50f, Alu, 0.7f), G.Cyl(-1.75f, 0.70f, 0.60f, 0.12f, 0.24f, Plastic, 0f),
                G.Box(-1.75f, 0.875f, 0.60f, 0.12f, 0.02f, 0.12f, DarkAlu, 0.6f) },
            bolts = new[] { new F(-1.80f, 0.886f, 0.60f, 0f, 1f, 0f), new F(-1.70f, 0.886f, 0.60f, 0f, 1f, 0f) },
            requires = new[] { "Hood" },
            tMin = 4.9f, tMax = 6.7f, specPart = "스트럿 상단 너트(M10)", specSource = SrcM10 },
        new PartDef {
            id = "RearStabilizer", name = "리어 스태빌라이저 바",
            tool = "스패너 · 에어 임팩렌치(10mm 소켓)",
            hint = "리어 언더 커버를 뗀 뒤 부싱 브래킷 볼트 2개와 엔드 링크를 풀어 뺀다",
            gen = new[] {
                G.Cyl(1.48f, 0.34f, 0f, 0.02f, 1.10f, DarkAlu, 0.7f, 90f, 0f, 0f),
                G.Box(1.48f, 0.34f, -0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f), G.Box(1.48f, 0.34f, 0.30f, 0.05f, 0.05f, 0.07f, Plastic, 0f) },
            bolts = new[] { new F(1.48f, 0.315f, -0.30f, 0f, -1f, 0f), new F(1.48f, 0.315f, 0.30f, 0f, -1f, 0f) },
            requires = new[] { "RearUnderCover" },
            tMin = 2.5f, tMax = 3.3f, specPart = "스태빌라이저 부싱 볼트(M8)", specSource = SrcM8 },
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
    }

    [Tooltip("떼어낸 부품으로 Spawn 할 장비(CarPartItem)")] public NetworkObject itemPrefab;
    [Tooltip("부품을 놓았을 때 제자리에 장착되는 거리(m) — VR")] public float mountDistVR = 0.35f;
    [Tooltip("부품을 놓았을 때 제자리에 장착되는 거리(m) — PC·모바일")] public float mountDistPC = 1.6f;
    [Tooltip("고스트가 보이기 시작하는 거리(m) — VR")] public float ghostDistVR = 1.2f;
    [Tooltip("고스트가 보이기 시작하는 거리(m) — PC·모바일")] public float ghostDistPC = 3.5f;
    [Tooltip("볼트 표시(아웃라인·링·카드)가 나타나는 거리(m)")] public float markerDist = 2.2f;
    [Tooltip("장착 스냅 애니메이션 시간(초)")] public float snapSeconds = 0.35f;
    [Tooltip("볼트를 메시 바깥쪽으로 내미는 거리(m)")] public float boltProtrude = 0.004f;

    private readonly SyncList<float> _prog = new SyncList<float>();
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
    }

    private Part[] _parts;
    [System.NonSerialized] private bool _found;
    private float _nextFind, _wheelPollAt;
    private float[] _sendAt, _pending, _activeAt, _allDoneSince, _resendAt;

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

    public int StateOf(int p) => p >= 0 && p < _state.Count ? _state[p] : (int)PartState.Tight;

    /// <summary>이 부위를 풀 수 있는가 — requires 의 부위가 모두 떨어져 있어야 한다.</summary>
    public bool CanRemove(int p)
    {
        foreach (var r in Defs[p].requires)
        {
            int q = IndexOf(r);
            if (q >= 0 && StateOf(q) != (int)PartState.Off) return false;
        }
        if (Defs[p].needsWheelOff && !WheelOff) return false;
        return true;
    }

    // 휠을 하나 이상 떼어 둔 상태 — 브레이크(캘리퍼·디스크)는 휠 안쪽이라 휠을 떼야 접근된다
    private static bool WheelOff => TireService.Instance != null && TireService.Instance.AnyHubFree;

    /// <summary>이 부위를 다시 끼울 수 있는가 — 이 부위를 먼저 떼야 하는(requires 로 가진) 부위가 모두 달려 있어야 한다.</summary>
    public bool CanMount(int p)
    {
        string id = Defs[p].id;
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
            if (System.Array.IndexOf(Defs[q].requires, id) >= 0 && StateOf(q) == (int)PartState.Off) list.Add(Defs[q].id);
        return NamesOf(list);
    }

    private string RemoveBlockers(int p)
    {
        var list = new List<string>();
        foreach (var r in Defs[p].requires) { int q = IndexOf(r); if (q >= 0 && StateOf(q) != (int)PartState.Off) list.Add(r); }
        string s = NamesOf(list);
        if (Defs[p].needsWheelOff && !WheelOff) s = s.Length > 0 ? s + ", 휠(바퀴 하나 이상)" : "휠(바퀴 하나 이상)";
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
            for (int q = 0; q < _parts.Length; q++) if (_parts[q] != null && Defs[q].needsWheelOff) Rebuild(q);
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
                    ProgressServerRpc(idx, _pending[idx]);
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
        PlaceFrames();
        EnforceHidden();
        if (!IsClientStarted) return;
        UpdateVisuals();
        UpdateGhost();
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

    private void ResetLocal(bool restore)
    {
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
        b.OnProgressChanged += _ => { _pending[idx] = b.Progress; _activeAt[idx] = Time.time; };
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
        }

        if (snapping)
        {
            SetVisible(pt, false);
            foreach (var b in pt.bolts) if (b != null) b.gameObject.SetActive(false);
            pt.snap = StartCoroutine(SnapRoutine(pt));
        }
        else SetVisible(pt, true);
    }

    private void ApplyProgress(int idx, float v)
    {
        if (_parts == null) return;
        int p = PartOfBolt(idx);
        if (p < 0 || _parts[p] == null || _parts[p].bolts == null) return;
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

    private float PartAngle(Part pt, Vector3 cp, Vector3 fwd, float near)
    {
        float best = 999f;
        for (int n = 0; n < pt.def.bolts.Length; n++)
        {
            Vector3 bw = BoltWorld(pt, n);
            Vector3 d = bw - cp;
            if (d.sqrMagnitude > near * near) continue;
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
                if (HvUnsafe(fd)) NoticeHv(fd, 4f, 20f);
            }
        }

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
            if (anyActive && on && HvUnsafe(pt.def)) NoticeHv(pt.def, 3f, 8f);   // 차단 안 된 채 고전압 부품 볼트를 돌리는 중
            else if (anyActive && on)   // 작업 절차 설명은 볼트를 처음 돌릴 때 한 번(부위별 3분에 한 번까지)
                EquipFx.Notice("carpart_" + pt.def.id, pt.def.name + (st == (int)PartState.Mounted ? " 장착" : " 탈거"),
                    st == (int)PartState.Mounted ? $"{pt.def.tool}로 규정 {pt.def.tMin:0.0#} ~ {pt.def.tMax:0.0#} kgf·m까지 조이세요" : pt.def.hint,
                    EquipFx.Info, 4f, 180f);
            if ((cardPart >= 0 && p != cardPart) || TireService.LabelShowing) { pt.flashUntil = 0f; pt.cardUntil = 0f; }   // 다른 부위·휠 카드가 떠 있으면 내린다
            UpdateCard(pt, p, st, done, anyActive && on, p == cardPart && !TireService.LabelShowing, cam);
            UpdateLeads(pt, on && pt.card != null && pt.card.root.activeSelf);
        }
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
        switch (d.id)
        {
            case "FrontUnderCover": case "RearUnderCover": case "FrontMotor": case "RearMotor":
            case "FrontSuspension": case "RearSuspension": case "SteeringRack": case "FrontStabilizer": case "RearStabilizer":
                return "차 밑 — 리프트로 올리고 작업";
            case "BrakeSystem": return "휠 안쪽 — 휠을 떼고 작업";
            case "Seats": case "Cockpit": case "Hvac": return "실내 — 도어를 열고 작업";
            case "Hood": case "Frunk": case "Headlamp": case "FrontBeam": case "Cooling": case "AcCompressor":
            case "FrontInverter": case "Iccu": case "JunctionBlock": case "Battery12VCover": case "Battery12V":
            case "FrontStrutL": case "FrontStrutR":
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
            if (pt == null || !pt.wbOk || pt.snap != null) continue;
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
    private void UpdateSlotGhosts(Camera cam)
    {
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
        return sm != null && !sm.IsSafeToWork();
    }

    private static void NoticeHv(PartDef d, float show, float cooldown)
        => EquipFx.Notice("carpart_hv", "고전압 차단 전입니다 — " + d.name,
            "절연장갑 → 12V(−) 분리 → 서비스 플러그(MSD) 분리 → 3분 방전 → 인버터 단자 전압 30V 이하 확인 뒤에 작업하세요", EquipFx.Danger, show, cooldown);

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
    private void ProgressServerRpc(int idx, float v)
    {
        if (idx < 0 || idx >= _prog.Count) return;
        int p = PartOfBolt(idx);
        if (p < 0 || p >= _state.Count) return;
        int st = _state[p];
        if (st == (int)PartState.Off) return;
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
        if (_state[p] == (int)PartState.Mounted) { _state[p] = (int)PartState.Tight; return; }
        _state[p] = (int)PartState.Off;
        SpawnItem(p);
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
    }

    // 떼어낸 부품이 사라졌거나(디스폰·서버 재시작 잔재) 바닥 밑·아주 먼 곳으로 떨어졌으면 서버가 되돌린다 — 안 그러면 그 부위는 영영 못 끼운다
    private float _watchAt;

    [Server]
    private void ServerWatch()
    {
        if (Time.unscaledTime < _watchAt || _state.Count < Defs.Length || _item.Count < Defs.Length) return;
        _watchAt = Time.unscaledTime + 2f;
        var spawned = ServerManager.Objects.Spawned;
        for (int p = 0; p < Defs.Length; p++)
        {
            if (_state[p] != (int)PartState.Off) continue;
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
        for (int p = 0; p < Defs.Length; p++) _state[p] = (int)PartState.Tight;
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
        _state[p] = (int)PartState.Mounted;
    }
}
