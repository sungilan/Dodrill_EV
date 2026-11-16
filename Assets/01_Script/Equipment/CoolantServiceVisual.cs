using UnityEngine;

// ============================================================
//  CoolantServiceVisual.cs
//  Ioniq5N 프리팹의 CoolantService(리저버 탱크 · 캡 · 드레인 플러그) 겉모습을 시나리오 진행에 맞춘다.
//
//  ClickableAnimator 의 열림 상태(클릭할 때마다 뒤집힘)를 쓰지 않고 '단계가 끝났는가'로 정한다.
//  순서 위반으로 캡을 먼저 눌러도(감점만 되고 단계는 안 넘어감) 캡이 열린 것처럼 보이지 않게 하기 위해서다.
//   · 캡: Coolant_Cap_Open 완료 ~ Coolant_Cap_Close 완료 사이 열림(탱크 옆에 내려놓은 자세)
//   · 드레인 플러그: Coolant_Drain_Plug_Open 완료 ~ Coolant_Drain_Plug_Close 완료 사이 빠짐 + 냉각수 흘러내림 효과
//   · 수위: 처음 MIN 아래 → 배출 중 줄어듦 → 주입 후 MAX → 에어빼기 중 내려감 → 보충 후 MAX
//  냉각수 시나리오가 아니면 캡 · 플러그는 닫힘, 수위는 정상.
// ============================================================
public class CoolantServiceVisual : MonoBehaviour
{
    [Header("부품")]
    public Transform cap;
    public Transform drainPlug;
    public Transform liquid;            // 탱크 안 냉각수(원통) — 바닥 기준으로 높이만 바꾼다
    public Transform drainFxPoint;      // 흘러내림 효과 위치(플러그 바로 아래)
    public GameObject drainFxPrefab;    // DrainCoolant 파티클

    [Header("수위 (탱크 높이 대비 0~1)")]
    public float tankBottomY = 0f;      // liquid 의 로컬 기준 바닥 높이
    public float tankHeight = 0.12f;    // 탱크 안쪽 높이(m)
    [Range(0, 1)] public float levelNormal = 0.75f;
    [Range(0, 1)] public float levelLow = 0.22f;     // MIN 선 아래
    [Range(0, 1)] public float levelFull = 0.85f;    // MAX 선
    [Range(0, 1)] public float levelAfterBleed = 0.45f;

    [Header("열림 자세")]
    public Vector3 capOpenOffset = new Vector3(0.09f, -0.04f, 0f);
    public Vector3 capOpenEuler = new Vector3(0f, 0f, 80f);
    public Vector3 plugOpenOffset = new Vector3(0f, -0.06f, 0f);

    private Vector3 _capPos, _plugPos;
    private Quaternion _capRot;
    private float _level, _drainStart = -1f, _bleedStart = -1f, _next;
    private GameObject _fx;

    private void Awake()
    {
        if (cap != null) { _capPos = cap.localPosition; _capRot = cap.localRotation; }
        if (drainPlug != null) _plugPos = drainPlug.localPosition;
        _level = levelNormal;
        ApplyLevel(_level);
    }

    private void OnDisable() { SetFx(false); ShowGuide(false); }

    private void Update()
    {
        bool coolant = GdsModel.IsCoolantScenario;
        bool capOpen = coolant && GdsModel.Done("Coolant_Cap_Open") && !GdsModel.Done("Coolant_Cap_Close");
        bool plugOpen = coolant && GdsModel.Done("Coolant_Drain_Plug_Open") && !GdsModel.Done("Coolant_Drain_Plug_Close");

        float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
        if (cap != null)
        {
            cap.localPosition = Vector3.Lerp(cap.localPosition, capOpen ? _capPos + capOpenOffset : _capPos, k);
            cap.localRotation = Quaternion.Slerp(cap.localRotation, capOpen ? _capRot * Quaternion.Euler(capOpenEuler) : _capRot, k);
        }
        if (drainPlug != null)
            drainPlug.localPosition = Vector3.Lerp(drainPlug.localPosition, plugOpen ? _plugPos + plugOpenOffset : _plugPos, k);
        SetFx(plugOpen);
        UpdateDrainGuide();

        if (Time.time < _next) return;
        _next = Time.time + 0.1f;
        float target = TargetLevel(coolant, plugOpen);
        _level = Mathf.MoveTowards(_level, target, 0.6f * 0.1f);
        ApplyLevel(_level);
    }

    private float TargetLevel(bool coolant, bool plugOpen)
    {
        if (!coolant) return levelNormal;
        if (GdsModel.Done("Coolant_Top_Up")) return levelFull;

        // 에어빼기 중: 공기가 빠지는 만큼 천천히 내려간다(약 18초)
        if (GdsModel.AirBleedRunning)
        {
            if (_bleedStart < 0f) _bleedStart = Time.time;
            return Mathf.Lerp(levelFull, levelAfterBleed, Mathf.Clamp01((Time.time - _bleedStart) / 18f));
        }
        _bleedStart = -1f;
        if (GdsModel.Done("Coolant_Air_Bleed")) return levelAfterBleed;
        if (GdsModel.Done("Coolant_Fill")) return levelFull;

        // 배출 중: 대기 시간(약 30초) 동안 바닥까지
        if (plugOpen || GdsModel.Done("Coolant_Drain_Plug_Close"))
        {
            if (_drainStart < 0f) _drainStart = Time.time;
            return Mathf.Lerp(levelLow, 0.02f, Mathf.Clamp01((Time.time - _drainStart) / 25f));
        }
        _drainStart = -1f;
        return levelLow;
    }

    private void ApplyLevel(float f)
    {
        if (liquid == null) return;
        float h = Mathf.Max(0.002f, tankHeight * f);
        var s = liquid.localScale;
        liquid.localScale = new Vector3(s.x, h * 0.5f, s.z);     // 기본 원통 높이 2 → 절반
        var p = liquid.localPosition;
        liquid.localPosition = new Vector3(p.x, tankBottomY + h * 0.5f, p.z);
    }

    private void SetFx(bool on)
    {
        if (on && _fx == null && drainFxPrefab != null && drainFxPoint != null)
            _fx = Instantiate(drainFxPrefab, drainFxPoint.position, drainFxPoint.rotation, drainFxPoint);
        else if (!on && _fx != null)
        {
            Destroy(_fx);
            _fx = null;
        }
    }

    // ── 드레인 플러그 찾기 안내 (11/16) ──
    // 플러그가 차 밑의 지름 3.5cm 원판이라 '드레인 플러그가 어디냐, 받침을 어디 두냐'에서 막혔다.
    // 배출 3단계(받침 놓기 · 플러그 풀기 · 잠그기) 동안 플러그에 깜빡이는 원 + 바닥까지 내려오는 선 + 바닥 원 + 이름표를 띄운다. 평가 모드에서는 끈다.
    private LineRenderer _gRing, _gDrop, _gFloor;
    private GameObject _gTag;
    private WorldHoverLabel _gTagComp;
    private string _gTagShown;

    private void UpdateDrainGuide()
    {
        string cur = GdsModel.CurrentModule;
        if (drainPlug != null && GdsModel.IsCoolantScenario) AlignDrainZone(drainPlug.position);   // 평가 모드에서도 존 위치는 맞춘다
        bool on = drainPlug != null && GdsModel.IsCoolantScenario && BoltHint.HintsAllowed &&
                  (cur == "Coolant_Drain_Pan_Place" || cur == "Coolant_Drain_Plug_Open" || cur == "Coolant_Drain_Plug_Close");
        if (!on) { ShowGuide(false); return; }

        if (_gRing == null) { _gRing = MakeLine("[DrainPlugRing]"); _gDrop = MakeLine("[DrainPlugDrop]"); _gFloor = MakeLine("[DrainPanSpot]"); }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        Color col = Color.Lerp(new Color(1f, 0.82f, 0.1f, 0.65f), new Color(1f, 0.95f, 0.4f, 1f), pulse);

        Vector3 plug = drainPlug.position;
        Circle(_gRing, plug, 0.06f + 0.02f * pulse, col, 0.008f);

        // 바닥 높이: 플러그 아래로 쏴서 처음 맞는 바닥(차체·받침 자신은 무시하기 어려워 아래쪽 2m 안 가장 낮은 것)
        float floorY = 0f;
        var hits = Physics.RaycastAll(plug + Vector3.down * 0.05f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits) if (h.point.y < best) best = h.point.y;
        if (best != float.MaxValue) floorY = best;
        Vector3 floor = new Vector3(plug.x, floorY + 0.01f, plug.z);

        bool panStep = cur == "Coolant_Drain_Pan_Place";
        if (!_gDrop.gameObject.activeSelf) _gDrop.gameObject.SetActive(true);
        _gDrop.startColor = _gDrop.endColor = new Color(col.r, col.g, col.b, 0.55f);
        _gDrop.widthMultiplier = 0.006f;
        _gDrop.positionCount = 2;
        _gDrop.SetPosition(0, plug + Vector3.down * 0.02f);
        _gDrop.SetPosition(1, floor);
        if (panStep) Circle(_gFloor, floor, 0.32f, col, 0.012f);
        else if (_gFloor.gameObject.activeSelf) _gFloor.gameObject.SetActive(false);

        if (_gTag == null)
        {
            var prefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (prefab != null)
            {
                _gTag = Instantiate(prefab);
                _gTag.name = "[DrainPlugTag]";
                foreach (var t in _gTag.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
                _gTagComp = _gTag.GetComponent<WorldHoverLabel>();
            }
        }
        if (_gTag != null)
        {
            if (!_gTag.activeSelf) _gTag.SetActive(true);
            // 이름표는 플러그와 바닥 사이(차 밑을 들여다보면 보이는 높이)
            _gTag.transform.position = Vector3.Lerp(plug, floor, 0.35f);
            string sub = panStep ? "배출 받침(또는 오일 받이)을 아래 노란 원에 놓으세요"
                       : cur == "Coolant_Drain_Plug_Open" ? "차 밑에서 이 플러그를 클릭해 푸세요"
                       : "배출이 끝나면 이 플러그를 잠그세요";
            string key = cur;
            if (_gTagComp != null && key != _gTagShown) { _gTagShown = key; _gTagComp.SetText("냉각수 드레인 플러그", sub); }
        }
    }

    // 차는 네트워크로 생성돼 위치가 씬에 고정돼 있지 않다 → 받침 존(씬 배치)을 플러그 바로 아래(XZ)로 맞춘다. 높이는 그대로.
    private Transform _zone;
    private bool _zoneSearched;
    private void AlignDrainZone(Vector3 plug)
    {
        if (!_zoneSearched) { _zoneSearched = true; var go = GameObject.Find("Guide_Coolant_Drain_Zone"); _zone = go != null ? go.transform : null; }
        if (_zone == null) return;
        Vector3 z = _zone.position;
        if (new Vector2(z.x - plug.x, z.z - plug.z).sqrMagnitude < 0.02f * 0.02f) return;
        _zone.position = new Vector3(plug.x, z.y, plug.z);
    }

    private void ShowGuide(bool on)
    {
        if (on) return;
        if (_gRing != null && _gRing.gameObject.activeSelf) _gRing.gameObject.SetActive(false);
        if (_gDrop != null && _gDrop.gameObject.activeSelf) _gDrop.gameObject.SetActive(false);
        if (_gFloor != null && _gFloor.gameObject.activeSelf) _gFloor.gameObject.SetActive(false);
        if (_gTag != null && _gTag.activeSelf) _gTag.SetActive(false);
        _gTagShown = null;
    }

    private void OnDestroy()
    {
        foreach (var lr in new[] { _gRing, _gDrop, _gFloor }) if (lr != null) Destroy(lr.gameObject);
        if (_gTag != null) Destroy(_gTag);
    }

    private static void Circle(LineRenderer lr, Vector3 c, float r, Color col, float width)
    {
        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
        lr.startColor = lr.endColor = col;
        lr.widthMultiplier = width;
        const int n = 40;
        lr.positionCount = n + 1;
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.PI * 2f * i / n;
            lr.SetPosition(i, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
        }
    }

    private static LineRenderer MakeLine(string goName)
    {
        var go = new GameObject(goName);
        go.layer = 1;   // TransparentFX — VR 카메라도 그림
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh);
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);   // 차체에 가려도 보이게
        m.renderQueue = 4000;
        lr.material = m;
        go.SetActive(false);
        return lr;
    }
}
