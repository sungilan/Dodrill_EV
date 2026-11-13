using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VehicleCoolant.cs
//
//  후드 아래 냉각수 보조탱크와 뚜껑.
//  · 뚜껑은 돌려서 연다: PC = 뚜껑을 누르고 있으면(마우스를 좌우로 움직이면 더 빨리) 돌아간다 / 모바일 = 한 번 터치하면 자동으로 돌아간다 /
//    VR = 뚜껑 근처에서 손을 쥐고 손목을 돌린다(반시계 = 열기)
//  · 다 풀리면(진행 1.0) 열린 것으로 보고 뚜껑이 들려 옆에 놓인다. 반대로 돌리면 다시 잠근다
//  · 열린 탱크에 냉각수 통(PourContainer)의 줄기가 떨어지면 수위가 오른다(ILiquidSink). 닫혀 있으면 받지 않아 뚜껑 위로 흘러 웅덩이가 된다
//  · 뚜껑 돌림 정도와 수위는 서버를 거쳐 모든 플레이어에게 같게, 늦게 들어온 사람도 현재 값으로 맞춘다(VehicleStateSync)
//  모델에 탱크 메시가 없어 둥근 통·뚜껑·수위를 코드로 만든다(위치는 tankLocalPos 로 조정).
// ============================================================
public class CoolantReservoir : MonoBehaviour, ILiquidSink
{
    [Header("차량 로컬 좌표 (Guide_Coolant_Reservoir 근처)")]
    public Vector3 tankLocalPos = new Vector3(-1.92f, 0.78f, 0.43f);
    public float tankHeight = 0.16f;
    public float tankDiameter = 0.13f;
    [Tooltip("탱크 용량(L)")] public float capacityLiters = 1.2f;
    [Range(0f, 1f), Tooltip("처음 수위(0~1) — 낮게 시작해 보충 실습이 되게")] public float startLevel = 0.35f;
    [Tooltip("뚜껑을 다 여는 데 걸리는 시간(초, 누르고 있을 때)")] public float turnSeconds = 1.6f;
    [Tooltip("다 열 때까지 돌아가는 바퀴 수")] public float turns = 3f;

    private const string CapId = "veh/coolantcap";
    private const string LevelId = "veh/coolantlevel";

    // 뚜껑
    private Transform _tank, _capPivot, _liquid;
    private float _p;           // 0 잠김 ~ 1 완전히 풀림(표시 값)
    private float _target;       // 서버에서 받은 목표(내가 돌리지 않는 동안 따라간다)
    private bool _dragging, _auto;
    private float _autoDir, _lastDir = 1f, _lastMouseX, _nextSend, _lastStep;
    private Hand _vrHand;
    private bool IsOpen => _p >= 0.999f;
    // 수위
    private float _level, _levelShown, _serverSent, _nextLevelSend;
    private MaterialPropertyBlock _mpb;
    private Renderer _liquidRend;
    private Transform _root;
    private XRAirpotrSecurity.PlatformType? _plat;
    private XRAirpotrSecurity.PlatformType Platform => _plat ??= XRAirpotrSecurity.Utils.GetPlatformType();

    // 서브 상태 객체(이 컴포넌트가 두 개의 id 를 동기화하므로)
    private class CapState : IVehicleState
    {
        public CoolantReservoir o; public string StateId => CapId;
        public void ApplyState(float v) { o.OnCapState(v); }
    }
    private class LevelState : IVehicleState
    {
        public CoolantReservoir o; public string StateId => LevelId;
        public void ApplyState(float v) { o.OnLevelState(v); }
    }
    private CapState _capState; private LevelState _levelState;

    public void Build(Transform vehicle, int layer)
    {
        _root = vehicle;
        _level = _levelShown = startLevel;
        _mpb = new MaterialPropertyBlock();

        Material glass = null, opaque = null;
        foreach (var r in vehicle.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.gameObject.activeInHierarchy) continue;
            if (glass == null && r.name == "SM_Ioniq_glass") glass = r.sharedMaterial;
            if (opaque == null && r.name == "SM_Ioniq_Bumper") opaque = r.sharedMaterial;
        }

        var tankGo = new GameObject("CoolantTank");
        _tank = tankGo.transform;
        _tank.SetParent(vehicle, false);
        _tank.localPosition = tankLocalPos + Vector3.up * (tankHeight * 0.5f);
        _tank.gameObject.layer = layer;

        // 통 몸체(반투명) + 수위(안쪽 기둥)
        var body = Prim(PrimitiveType.Cylinder, "Body", _tank, new Vector3(tankDiameter, tankHeight * 0.5f, tankDiameter), Vector3.zero, layer);
        if (glass != null)
        {
            var m = new Material(glass);
            SetColor(m, new Color(0.75f, 0.85f, 0.95f, 0.28f));
            body.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        else body.GetComponent<MeshRenderer>().enabled = false;
        body.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var lib = EquipFx.Lib;
        var liq = Prim(PrimitiveType.Cylinder, "Liquid", _tank, new Vector3(tankDiameter * 0.9f, 0.01f, tankDiameter * 0.9f),
                       new Vector3(0, -tankHeight * 0.5f, 0), layer);
        DestroyCollider(liq);
        _liquid = liq.transform; _liquidRend = liq.GetComponent<MeshRenderer>();
        if (lib != null && lib.puddle != null) _liquidRend.sharedMaterial = lib.puddle;
        else if (opaque != null) _liquidRend.sharedMaterial = opaque;
        _liquidRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        UpdateLiquidVisual(true);

        // 붓는 줄기가 맞는 면(열림/닫힘 모두 레이가 닿도록 통 위쪽에 충돌체) — ILiquidSink 는 이 컴포넌트가 받는다
        var col = body.GetComponent<Collider>();
        if (col != null) col.isTrigger = false;
        body.AddComponent<CoolantTankHit>().owner = this;
        var bodyProxy = body.AddComponent<ClickProxy>();
        bodyProxy.displayName = "냉각수 보조탱크";
        bodyProxy.vrPokeable = false;
        bodyProxy.hover = touch => IsOpen ? "냉각수 통을 기울여 부으세요 (수위 " + Mathf.RoundToInt(_level * 100f) + "%)"
                                          : "먼저 뚜껑을 돌려 여세요 (수위 " + Mathf.RoundToInt(_level * 100f) + "%)";
        bodyProxy.onClick = () => EquipFx.Notice("coolant_info", "냉각수 보조탱크",
            "수위 " + Mathf.RoundToInt(_level * 100f) + "% — 뚜껑을 열고 냉각수 통으로 보충하세요", EquipFx.Info, 2.5f, 4f);

        // 뚜껑: 피벗(돌아가는 중심) + 메시
        var pivot = new GameObject("CapPivot");
        _capPivot = pivot.transform;
        _capPivot.SetParent(_tank, false);
        _capPivot.localPosition = new Vector3(0, tankHeight * 0.5f + 0.012f, 0);
        var cap = Prim(PrimitiveType.Cylinder, "Cap", _capPivot, new Vector3(tankDiameter * 0.62f, 0.012f, tankDiameter * 0.62f), Vector3.zero, layer);
        var capRend = cap.GetComponent<MeshRenderer>();
        if (opaque != null)
        {
            var m = new Material(opaque);
            SetColor(m, new Color(0.08f, 0.2f, 0.75f, 1f));
            capRend.sharedMaterial = m;
        }
        // 뚜껑 손잡이(돌리는 방향이 보이게 가로 막대)
        var bar = Prim(PrimitiveType.Cube, "Grip", _capPivot, new Vector3(tankDiameter * 0.55f, 0.012f, 0.012f), new Vector3(0, 0.014f, 0), layer);
        DestroyCollider(bar);
        if (opaque != null) bar.GetComponent<MeshRenderer>().sharedMaterial = capRend.sharedMaterial;
        DestroyCollider(cap);   // 실린더 프리미티브의 캡슐 콜라이더 → 박스로 교체(더 잘 눌리게 크게)
        var bc = cap.AddComponent<BoxCollider>();
        bc.size = new Vector3(2.2f, 3.2f, 2.2f);    // 로컬 단위(스케일 곱해져 실제로는 약 9cm x 4cm)
        var proxy = cap.AddComponent<ClickProxy>();
        proxy.displayName = "냉각수 탱크 뚜껑";
        proxy.vrPokeable = false;
        proxy.hover = touch => IsOpen
            ? (touch ? "터치하여 뚜껑 닫기(돌리기)" : "누르고 있으면 뚜껑이 닫힙니다")
            : (Platform == XRAirpotrSecurity.PlatformType.VR ? "뚜껑을 쥐고 손목을 돌려 여세요"
               : (touch ? "터치하여 뚜껑 돌리기" : "누르고 있으면 뚜껑이 돌아가 열립니다 (좌우로 움직이면 더 빨리)"));
        proxy.onClick = OnCapPressed;

        _capState = new CapState { o = this };
        _levelState = new LevelState { o = this };
        VehicleStateSync.Register(_capState);
        VehicleStateSync.Register(_levelState);

        // 서버: 처음 수위를 모두에게 알린다(클라이언트는 접속 후 요청으로 받는다)
        if (FishNet.InstanceFinder.IsServerStarted) { VehicleStateSync.Request(LevelId, _level); _serverSent = _level; }
    }

    private void OnDestroy()
    {
        VehicleStateSync.Unregister(_capState);
        VehicleStateSync.Unregister(_levelState);
        if (_tank != null) Destroy(_tank.gameObject);
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 scale, Vector3 localPos, int layer)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        return go;
    }
    private static void DestroyCollider(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) Destroy(c); }
    private static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // ── 입력 ─────────────────────────────

    private void OnCapPressed()
    {
        bool down = Input.GetMouseButton(0) || Input.touchCount > 0;
        float dir = IsOpen ? -1f : (_p > 0.02f ? _lastDir : 1f);
        _lastDir = dir;
        if (down && Platform == XRAirpotrSecurity.PlatformType.PC)
        {
            _dragging = true; _auto = false;
            _lastMouseX = Input.mousePosition.x;
        }
        else
        {
            // 터치(탭은 손가락이 이미 떨어진 뒤에 도착한다) → 자동으로 끝까지 돌린다
            _auto = true; _autoDir = dir; _dragging = false;
        }
    }

    private void Update()
    {
        if (_capPivot == null) return;
        float dt = Time.deltaTime;
        float before = _p;

        if (_dragging)
        {
            bool down = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (!down) { _dragging = false; SendCap(true); }
            else
            {
                float mx = Input.mousePosition.x;
                float dx = Mathf.Abs(mx - _lastMouseX) / Mathf.Max(1f, Screen.width);
                _lastMouseX = mx;
                _p = Mathf.Clamp01(_p + _lastDir * (dt / Mathf.Max(0.3f, turnSeconds) + dx * 3f));
                SendCap(false);
            }
        }
        else if (_auto)
        {
            _p = Mathf.Clamp01(_p + _autoDir * dt / Mathf.Max(0.3f, turnSeconds));
            SendCap(false);
            if ((_autoDir > 0 && _p >= 1f) || (_autoDir < 0 && _p <= 0f)) { _auto = false; SendCap(true); }
        }
        else if (Platform == XRAirpotrSecurity.PlatformType.VR && UpdateVrTwist()) { }
        else if (!Mathf.Approximately(_p, _target)) _p = Mathf.MoveTowards(_p, _target, dt / 0.6f);   // 남이 돌린 값을 따라간다

        // 효과음(1/12 칸마다 딸깍) · 열림/닫힘 확정음
        if (!Mathf.Approximately(before, _p))
        {
            if (Mathf.FloorToInt(_p * 12f) != Mathf.FloorToInt(before * 12f))
                AudioSource.PlayClipAtPoint(EquipFx.Ratchet, _capPivot.position, 0.5f);
            if (before < 0.999f && _p >= 0.999f) AudioSource.PlayClipAtPoint(EquipFx.Click, _capPivot.position, 0.8f);
            if (before >= 0.999f && _p < 0.999f) AudioSource.PlayClipAtPoint(EquipFx.Click, _capPivot.position, 0.5f);
        }

        // 뚜껑 모양: 풀릴수록 위로 올라오고(0~2.5cm), 다 풀리면 비스듬히 얹힌다
        float aside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, _p));
        _capPivot.localRotation = Quaternion.Euler(0, -_p * 360f * turns, 0) * Quaternion.Euler(aside * 18f, 0, aside * 12f);
        float baseY = tankHeight * 0.5f + 0.012f;
        _capPivot.localPosition = new Vector3(aside * 0.045f, baseY + _p * 0.025f + aside * 0.02f, 0);

        // 수위 표시를 부드럽게
        if (!Mathf.Approximately(_levelShown, _level))
        {
            _levelShown = Mathf.MoveTowards(_levelShown, _level, dt * 0.25f);
            UpdateLiquidVisual(false);
        }

        // 서버: 수위가 바뀌면 1초에 한 번 모두에게 알린다
        if (FishNet.InstanceFinder.IsServerStarted && Time.time >= _nextLevelSend && Mathf.Abs(_level - _serverSent) > 0.005f)
        {
            _nextLevelSend = Time.time + 1f; _serverSent = _level;
            VehicleStateSync.Request(LevelId, _level);
        }
    }

    private bool UpdateVrTwist()
    {
        if (_vrHand != null)
        {
            if (!_vrHand.IsSqueezing() || _vrHand.holdingObj != null) { _vrHand = null; SendCap(true); return true; }
            Vector3 v = FlatDir(_vrHand);
            float ang = Vector3.SignedAngle(_vrPrevAngleVec, v, _root.up);
            _vrPrevAngleVec = v;
            // 위에서 봤을 때 반시계(음수) = 열기
            _p = Mathf.Clamp01(_p - ang / (360f * turns));
            _lastDir = ang < 0 ? 1f : -1f;
            SendCap(false);
            return true;
        }
        foreach (var h in LocalHands.All)
        {
            if (h == null || h.holdingObj != null || !h.IsSqueezing()) continue;
            if ((h.transform.position - _capPivot.position).sqrMagnitude > 0.09f * 0.09f) continue;
            _vrHand = h; _vrPrevAngleVec = FlatDir(h);
            return true;
        }
        return false;
    }
    private Vector3 _vrPrevAngleVec;
    private Vector3 FlatDir(Hand h)
    {
        Vector3 v = Vector3.ProjectOnPlane(h.transform.forward, _root.up);
        if (v.sqrMagnitude < 0.05f) v = Vector3.ProjectOnPlane(h.transform.right, _root.up);
        return v.normalized;
    }

    private void SendCap(bool final)
    {
        if (!final && Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + 0.1f;
        _target = _p;
        VehicleStateSync.Request(CapId, _p);
    }

    // ── 동기화 수신 ─────────────────────────────

    private void OnCapState(float v)
    {
        if (_dragging || _auto || _vrHand != null) return;   // 내가 돌리는 중이면 내 값 우선
        _target = Mathf.Clamp01(v);
    }

    private void OnLevelState(float v)
    {
        _level = Mathf.Clamp01(v);
        if (FishNet.InstanceFinder.IsServerStarted) _serverSent = _level;
    }

    // ── 액체 받기 ─────────────────────────────

    public bool AddLiquid(float liters, string liquidName)
    {
        bool coolant = !string.IsNullOrEmpty(liquidName) && liquidName.Contains("냉각수");
        if (!IsOpen)
        {
            EquipFx.Notice("coolant_closed", "냉각수 탱크", "뚜껑이 닫혀 있습니다. 먼저 뚜껑을 돌려 여세요", EquipFx.Warn, 2.5f, 6f);
            return false;
        }
        if (!coolant)
        {
            EquipFx.Notice("coolant_wrong", "잘못된 액체", "냉각수 탱크에는 냉각수만 넣습니다 (" + liquidName + " 아님)", EquipFx.Danger, 3f, 8f);
            return false;
        }
        float before = _level;
        _level = Mathf.Clamp01(_level + liters / Mathf.Max(0.1f, capacityLiters));
        if (before < 0.999f && _level >= 0.999f)
            EquipFx.Notice("coolant_full", "냉각수 탱크", "가득 찼습니다 — 그만 넣으세요", EquipFx.Info, 3f, 6f);
        return true;
    }

    private void UpdateLiquidVisual(bool instant)
    {
        if (_liquid == null) return;
        if (instant) _levelShown = _level;
        float h = Mathf.Max(0.002f, tankHeight * 0.92f * _levelShown);
        // 실린더 프리미티브는 높이 2 → localScale.y = h/2
        _liquid.localScale = new Vector3(tankDiameter * 0.9f, h * 0.5f, tankDiameter * 0.9f);
        _liquid.localPosition = new Vector3(0, -tankHeight * 0.5f + h * 0.5f, 0);
        if (_liquidRend != null)
        {
            _liquidRend.enabled = _levelShown > 0.005f;
            _mpb.SetColor("_BaseColor", new Color(0.12f, 0.45f, 0.95f, 0.85f));
            _liquidRend.SetPropertyBlock(_mpb);
        }
    }
}

/// <summary>탱크 몸체에 붙어 PourContainer 가 GetComponentInParent&lt;ILiquidSink&gt;() 로 찾을 수 있게 소유 컴포넌트에 연결.</summary>
public class CoolantTankHit : MonoBehaviour, ILiquidSink
{
    public CoolantReservoir owner;
    public bool AddLiquid(float liters, string liquidName) => owner != null && owner.AddLiquid(liters, liquidName);
}
