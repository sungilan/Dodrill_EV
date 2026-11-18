using UnityEngine;

// ============================================================
//  Battery12VServiceVisual.cs
//  Ioniq5N 프리팹의 Battery12VService(12V 배터리 단자 · 센서 · 클램프) 겉모습을 시나리오 진행에 맞춘다.
//
//  CoolantServiceVisual 과 같은 방식 — 클릭으로 뒤집히는 열림 상태가 아니라 '단계가 끝났는가'로 정한다.
//   · (−)/(+) 단자: 분리 단계 완료 ~ 연결 단계 완료 사이 케이블째 들려 옆으로 젖혀진다
//   · 센서 커넥터: 분리 완료 ~ 연결 완료 사이 빠져 있다
//   · 클램프: 탈거 완료 ~ 체결 완료 사이 옆에 치워져 있다
//   · 배터리 본체(SM_12v): '기존 배터리 탈거' 단계가 시작되면 숨긴다 — 그 자리에 들고 옮길 Old_12V_Battery 가 생긴다.
//     '신품 배터리 장착'이 끝나면 다시 보인다(장착한 신품 배터리는 단계가 끝나며 치워지므로 차 안의 본체가 그 역할을 한다).
//  12V 시나리오가 아니면 모두 기본(연결 · 체결) 상태.
// ============================================================
public class Battery12VServiceVisual : MonoBehaviour
{
    [Header("부품")]
    public Transform negTerminal;
    public Transform posTerminal;
    public Transform sensor;
    public Transform clamp;
    public GameObject batteryBody;      // 차 안의 12V 배터리 본체 메쉬(SM_12v)
    public GameObject[] hideWhenOut;    // 배터리를 들어낸 동안 같이 숨길 것(단자 기둥 등)

    [Header("분리 자세 (로컬)")]
    public Vector3 terminalLift = new Vector3(0f, 0.045f, 0.03f);
    public Vector3 terminalTilt = new Vector3(-35f, 0f, 0f);
    public Vector3 sensorPull = new Vector3(-0.03f, 0.025f, 0f);
    public Vector3 clampAside = new Vector3(-0.07f, 0.05f, 0f);

    private Vector3 _negPos, _posPos, _senPos, _clampPos;
    private Quaternion _negRot, _posRot;

    private void Awake()
    {
        if (negTerminal != null) { _negPos = negTerminal.localPosition; _negRot = negTerminal.localRotation; }
        if (posTerminal != null) { _posPos = posTerminal.localPosition; _posRot = posTerminal.localRotation; }
        if (sensor != null) _senPos = sensor.localPosition;
        if (clamp != null) _clampPos = clamp.localPosition;
    }

    private void Update()
    {
        bool s = GdsModel.Is12VScenario;
        bool negOff = s && GdsModel.Done("Battery_12V_Neg_Disconnect") && !GdsModel.Done("Battery_12V_Neg_Connect");
        if (FreeLoto.IsFreeScenario) negOff = FreeLoto.Battery12VOff;   // 자유 탈거: LOTO 연습 상태(차 안 단자 또는 연습 보드)를 그대로 보여 준다
        bool posOff = s && GdsModel.Done("Battery_12V_Pos_Disconnect") && !GdsModel.Done("Battery_12V_Pos_Connect");
        bool senOff = s && GdsModel.Done("Battery_12V_Sensor_Disconnect") && !GdsModel.Done("Battery_12V_Sensor_Connect");
        bool clampOff = s && GdsModel.Done("Battery_12V_Clamp_Removal") && !GdsModel.Done("Battery_12V_Clamp_Install");
        bool batteryOut = s && Reached("Battery_12V_Removal") && !GdsModel.Done("Battery_12V_Install");

        float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
        Move(negTerminal, negOff, _negPos, _negRot, k);
        Move(posTerminal, posOff, _posPos, _posRot, k);
        if (sensor != null) sensor.localPosition = Vector3.Lerp(sensor.localPosition, senOff ? _senPos + sensorPull : _senPos, k);
        if (clamp != null) clamp.localPosition = Vector3.Lerp(clamp.localPosition, clampOff ? _clampPos + clampAside : _clampPos, k);

        if (batteryBody != null && batteryBody.activeSelf == batteryOut) batteryBody.SetActive(!batteryOut);
        if (hideWhenOut != null)
            foreach (var g in hideWhenOut)
                if (g != null && g.activeSelf == batteryOut) g.SetActive(!batteryOut);
    }

    private void Move(Transform t, bool off, Vector3 pos, Quaternion rot, float k)
    {
        if (t == null) return;
        t.localPosition = Vector3.Lerp(t.localPosition, off ? pos + terminalLift : pos, k);
        t.localRotation = Quaternion.Slerp(t.localRotation, off ? rot * Quaternion.Euler(terminalTilt) : rot, k);
    }

    // 그 단계가 지금이거나 이미 지났는가
    private static bool Reached(string moduleId)
    {
        int i = GdsModel.IndexOf(moduleId);
        return i >= 0 && GdsModel.TaskIndex >= i;
    }
}
