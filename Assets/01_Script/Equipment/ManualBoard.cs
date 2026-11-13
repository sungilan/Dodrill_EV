using TMPro;
using UnityEngine;

// ============================================================
//  ManualBoard.cs
//
//  작업장 벽의 '정비 매뉴얼' 게시판. 아이오닉 5 정비 매뉴얼의 핵심(고전압 차단 절차, PPE, 체결 토크,
//  리프트 포인트, 점검 규정값, 사고·화재 대응, 특수공구)을 페이지로 넘겨 본다.
//  · PC 클릭 / 모바일 터치 / VR 검지로 ◀ ▶ 버튼을 누른다. 페이지는 사람마다 따로 넘긴다(동기화 없음).
// ============================================================
public class ManualBoard : MonoBehaviour
{
    public TMP_Text title;
    public TMP_Text body;
    public TMP_Text pageLabel;
    public ManualBoardButton[] buttons;

    private int _page;
    private float _vrCd;

    private static readonly string[][] Pages =
    {
        new[] { "고전압 차단 절차 (1/2)",
            "1. 진단기를 자기 진단 커넥터(DLC)에 연결\n" +
            "2. POWER ON (EV READY 금지)\n" +
            "3. 서비스 데이터 BMS 융착 상태 확인\n      규정: <b>Relay Welding not detection</b>\n" +
            "4. 서비스 데이터 '절연 저항' 점검\n      규정: <b>500 kΩ 이상</b>\n" +
            "5. POWER OFF\n" +
            "6. 12V 배터리 (−) 터미널 분리\n" +
            "7. 고전압 차단 스위치(서비스 플러그) 분리\n      → 캐패시터 방전을 위해 <b><color=#FFB000>3분 이상 대기</color></b>" },
        new[] { "고전압 차단 절차 (2/2)",
            "8. 인버터 캐패시터 방전 확인\n" +
            "   (1) 리프트로 차량 들어올림\n" +
            "   (2) 프런트·리어 언더커버 탈거\n" +
            "   (3) 고전압 커넥터 커버 탈거  <size=80%>(0.8~1.2 kgf·m)</size>\n" +
            "   (4) 고전압 배터리 프런트·리어 커넥터 분리\n" +
            "   (5) 프런트·리어 인버터 단자 간 전압 <b>30V 이하</b>\n" +
            "9. 배터리 리어 고전압 커넥터 단자 간 전압 <b>0V</b>\n      (파워 릴레이 융착 점검)\n" +
            "<color=#FF6060>※ 비정상이면 차단이 안 된 것 — 메인 퓨즈 탈거</color>" },
        new[] { "개인 보호 장비 (PPE)",
            "• 절연 장갑  <size=80%>1000V / 300A 이상</size>\n" +
            "• 절연화 · 절연복 · 절연 안전모\n" +
            "• 보호 안경 · 안면 보호대  <size=80%>(스파크 위험 작업)</size>\n" +
            "• 절연 매트  <size=80%>탈거한 고전압 부품 보관</size>\n" +
            "• 절연 덮개  <size=80%>작업 안 하는 고전압 부품 차단</size>\n" +
            "• 경고 테이프·'고전압 위험 차량' 표시\n\n" +
            "<b>절연 장갑 점검</b>: 접고 3~4번 더 말아 공기를 가둔 뒤\n찢어짐·누설 확인, 물기 완전히 제거 후 착용\n" +
            "<color=#FFB000>시계·반지 등 금속 장신구는 작업 전 제거</color>" },
        new[] { "고전압 작업 주의사항",
            "• 보호 장비 착용자 외에는 고전압 부품 접촉 금지\n" +
            "• 고전압 작업은 <b>절연 공구</b> 사용\n" +
            "• 단자 간 전압 0V 확인 후 작업\n" +
            "• 체결 토크 준수\n" +
            "• 분리한 고전압 케이블은 즉시 절연 테이프로 절연\n" +
            "• (+)(−) 단자끼리 접촉 금지\n" +
            "• 커넥터 분리 후 주변 냉각수 제거 (화재 위험)\n" +
            "• <color=#FF8C1A>오렌지색 배선·커넥터 = 고전압</color>\n" +
            "• 고전압 부품: 배터리 시스템, 모터, 인버터,\n  고전압 정션 블록, 파워 케이블" },
        new[] { "체결 토크 (kgf·m)",
            "고전압 커넥터 커버 ........ <b>0.8 ~ 1.2</b>\n" +
            "프런트·리어 언더커버 ....... <b>0.8 ~ 1.2</b>\n" +
            "12V 배터리 단자 (+)(−) ...... <b>0.8 ~ 1.0</b>\n" +
            "12V 배터리 클램프 ............ <b>0.9 ~ 1.4</b>\n" +
            "12V 배터리 센서 ................ <b>2.7 ~ 3.3</b>\n\n" +
            "<b>일반 조임 토크 (육각 보통나사, 8T)</b>\n" +
            "M6 0.9~1.3   M8 2.3~3.1   M10 4.6~6.2\n" +
            "M12 8.0~10.8   M14 12.8~17.3\n\n" +
            "<size=85%>토크렌치: 볼트 밖에서 눌러 설정 변경 → 규정 범위 안에서만 체결</size>" },
        new[] { "리프트 포인트 · 차량 안전",
            "• EV 전용 리프트 잭 사용\n" +
            "• 리프트 블록을 지지점에 맞춤 → 조금 들어 안착 확인\n" +
            "  → 끝까지 들어 올린 뒤 다시 확인\n" +
            "• <color=#FF6060><b>배터리 케이스는 절대 리프트 포인트로 쓰지 않음</b></color>\n" +
            "• 들 때 가니쉬·언더커버·배터리 손상 여부 육안 확인\n" +
            "• 차체 아래 작업 시 안전 스탠드 사용\n" +
            "• 타이어 앞·뒤에 고임목, 주차 브레이크·P 위치\n" +
            "• 보호 안경 착용, 작업 중 금연\n" +
            "• 후드 닫기 전 공구·부품 남았는지 확인" },
        new[] { "배터리 점검 규정값",
            "<b>진단기 서비스 데이터</b>  (POWER ON, EV READY 금지)\n" +
            "• 배터리 팩 전압 ........ 480 ~ 826 V\n" +
            "• 셀 전압 ..................... 2.5 ~ 4.2 V\n" +
            "• 셀 간 전압 편차 ........ 40 mV 이하\n" +
            "• 절연 저항 ................. 500 kΩ 이상\n\n" +
            "<b>메가옴 테스터 직접 측정</b>  (고전압 차단 후)\n" +
            "• (−) 리드: 배터리 케이스(접지)\n" +
            "• (+) 리드: PRA 배터리(+)(−) / 인버터(+)(−)\n" +
            "• 1000V 인가, 약 1분 대기 → <b>2 MΩ 이상</b> (20°C)" },
        new[] { "사고 차량 · 화재 대응",
            "<b>준비</b>: PPE, 붕소액, CO2 소화기, 전해질용 수건,\n비닐 테이프, 메가옴 테스터\n\n" +
            "• 피복 벗겨진 파워 케이블 절대 접촉 금지\n" +
            "• 침수 차량은 고전압 부품 접근 금지\n" +
            "• 가스 발생 시 즉시 환기, 안전한 곳으로 대피\n" +
            "• 누출액이 피부에 닿으면 붕소액으로 중화 후 세척\n" +
            "• 소규모 화재: ABC·BC 소화기\n" +
            "• <color=#FF6060>하부 배터리 화재: 다량의 물을 오래 — 어려우면 대피,\n  119에 '전기차 화재' 신고</color>" },
        new[] { "특수공구 · EV 전용 장비",
            "• 고전압 배터리 이송 행어  <size=80%>09375-K4400 / K4100</size>\n" +
            "• 배터리 장착 가이드 핀  <size=80%>09375-BF000</size>\n" +
            "• 절연 공구 세트  <size=80%>09090-09000</size>\n" +
            "• 리프트 테이블(플로어 잭) — 배터리 탈거\n" +
            "• 디지털 테스터 — 전압·전류·저항\n" +
            "• 메가옴 테스터 — 절연 저항\n" +
            "• 크레인 자키 — 배터리·모듈 탈거\n" +
            "• 기밀 점검 장비(ULT-M100), 헬륨 가스 감지기" },
    };

    private void Start() => Show(0);

    public void Flip(int dir)
    {
        Show((_page + dir + Pages.Length) % Pages.Length);
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.5f);
    }

    private void Show(int p)
    {
        _page = p;
        if (title != null) title.text = Pages[p][0];
        if (body != null) body.text = Pages[p][1];
        if (pageLabel != null) pageLabel.text = $"{p + 1} / {Pages.Length}";
    }

    private void Update()
    {
        // VR: 검지 끝으로 버튼 누르기
        _vrCd -= Time.deltaTime;
        if (_vrCd > 0f || buttons == null) return;
        var hands = LocalHands.All;
        if (hands == null || hands.Count == 0) return;
        foreach (var h in hands)
        {
            if (h == null) continue;
            var tip = LocalHands.IndexTip(h);
            Vector3 p = tip != null ? tip.position : h.transform.position;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                var c = b.GetComponent<Collider>();
                if (c == null || (c.ClosestPoint(p) - p).sqrMagnitude > 0.02f * 0.02f) continue;
                _vrCd = 0.6f;
                b.OnPCClick();
                EquipFx.Haptic(h, 0.03f, 0.4f);
                return;
            }
        }
    }
}
