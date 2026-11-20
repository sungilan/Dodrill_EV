using Autohand;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  LadderProp.cs
//
//  밟고 올라설 수 있는 사다리(작업 발판). 바닥에 끌려 다니는 장비(FloorDragProp)와 함께 쓴다 → 잡고 원하는 자리로 옮긴다.
//  · 모델 크기에서 계단 발판(BoxCollider)을 만든다 — 다리가 벌어진 방향(긴 수평축) 양쪽에서 가운데로 갈수록 높아지는 피라미드
//    한 칸 높이는 0.28m 이하 → VR(AutoHandPlayer, 최대 0.3m)·PC·모바일(접지 0.35m) 모두 걸어 들어가면 한 칸씩 올라선다
//  · 누가 잡고 있는 동안은 발판을 끈다(들고 옮길 때 사람을 밀어 올리지 않게)
//  · 발판은 ClimbSurface 표시 → PC·모바일 접지(PcMobileAvatarController)가 네트워크 물체라도 바닥으로 인정한다
//  · 오르기
//      PC·모바일: 사다리 가까이에서 Q(높이 올리기) = 한 칸 오르기, E(높이 내리기) = 한 칸 내려오기. 멀리 걸어 나가면 내려온다
//      VR      : 발판 앞 가로대를 잡고 아래로 당기면 몸이 올라간다(AutoHand Climbable). 올라선 뒤엔 발판 위에 선다
// ============================================================
public class LadderProp : MonoBehaviour, IHoldScaleExempt
{
    [Tooltip("가장 높은 발판 높이 — 모델 높이에 대한 비율")] [Range(0.2f, 0.9f)] public float standRatio = 0.5f;
    [Tooltip("가장 높은 발판 최대 높이(m)")] public float maxStand = 1.0f;
    [Tooltip("한 칸 높이 상한(m)")] public float maxRise = 0.26f;

    private SyncGrab _sync;
    private GameObject _steps;
    private float _standY;

    // PC·모바일용 '높이 보너스' 계산 값(발판 모양과 같은 계단)
    private static readonly System.Collections.Generic.List<LadderProp> s_all = new System.Collections.Generic.List<LadderProp>();
    private Vector3 _fpCenter;          // 발판 중심(로컬)
    private bool _alongX;
    private float _half, _side, _rise, _run, _bottomY;
    private int _n;
    private float _topHalf;
    private GameObject _rungs;

    // 이 화면(로컬 PC·모바일 플레이어)이 키로 올라선 사다리와 칸 수
    private static LadderProp s_pcLadder;
    private static int s_pcLevel;
    private static float s_pcSign = 1f;   // 사다리 어느 쪽 면으로 올라갔는지(A형 사다리 양면)

    /// <summary>발판 맨 위 높이(로컬).</summary>
    public float StandHeight => _standY;

    // (11/19 VR 테스트) 사다리 가까이에서 플레이어가 위아래로 떨리는 문제 — 발판 계단·오르기(VrLadderClimbAssist 가 몸 높이 고정을 풀었다 조였다 함)가 원인으로 보여
    // 사다리 기능을 모두 끈다. 사다리는 잡아서 옮기는 소품으로만 남는다. 다시 쓰려면 true.
    public static bool FeatureEnabled = false;

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        if (!FeatureEnabled) { enabled = false; return; }
        BuildSteps();
    }

    private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() { s_all.Remove(this); if (_rungs != null) _rungs.SetActive(false); }

    /// <summary>
    /// PC·모바일 아바타가 이 위치(월드)에 서 있을 때 사다리 때문에 더해야 하는 높이(m).
    /// 사다리 위로 걸어 들어가면 한 칸씩 저절로 올라가고, 걸어 나오면 같은 방식으로 내려온다.
    /// (예전: 발판 콜라이더를 바닥으로 인정해 올라갔는데, 내려와도 그 높이가 유지되는 경우가 있었다 — 위치만으로 계산해 항상 일치하게)
    /// </summary>
    public static float BonusAt(Vector3 worldPos)
    {
        float best = 0f;
        for (int i = 0; i < s_all.Count; i++)
        {
            var l = s_all[i];
            if (l == null || l._n <= 0) continue;
            if (l._sync != null && l._sync.IsGrabbed) continue;           // 누가 들고 있는 동안은 발판이 아니다
            float h = l.HeightAt(worldPos);
            if (l == s_pcLadder && s_pcLevel > 0)
            {
                if (l.NearFootprint(worldPos, 0.5f)) h = Mathf.Max(h, s_pcLevel * l._rise * l.transform.lossyScale.y);
                else { s_pcLadder = null; s_pcLevel = 0; }   // 걸어 나가면 내려온다
            }
            if (h > best) best = h;
        }
        if (s_pcLadder != null && (s_pcLadder._sync != null && s_pcLadder._sync.IsGrabbed)) { s_pcLadder = null; s_pcLevel = 0; }
        return best;
    }

    /// <summary>
    /// PC·모바일: 높이 올리기(Q)/내리기(E) 입력을 사다리 오르내리기로 쓴다. 가까운 사다리가 없으면 false(원래 높이 조절).
    /// </summary>
    public static bool TryClimbStep(Transform player, int dir)
    {
        if (player == null || dir == 0) return false;
        Vector3 pos = player.position;

        // 11/16 테스트: 사다리 근처에서 Q/E 가 높이 조절 대신 소비되고 앞뒤로 순간이동했다.
        // 올라가 있다는 기록(s_pcLadder)이 남은 채 사다리에서 떨어졌거나(크리퍼·걸어서 이탈, BonusAt 이 안 불리는 동안) 누가 사다리를 들었으면 기록을 버린다.
        if (s_pcLadder != null && (s_pcLadder._sync != null && s_pcLadder._sync.IsGrabbed || !s_pcLadder.isActiveAndEnabled || !s_pcLadder.NearFootprint(pos, 0.5f)))
        { s_pcLadder = null; s_pcLevel = 0; }

        if (s_pcLadder != null && s_pcLevel > 0)
        {
            int lv = Mathf.Clamp(s_pcLevel + dir, 0, s_pcLadder._n);
            if (lv == s_pcLevel) return true;
            s_pcLevel = lv;
            // 그 칸 발판 위로 옮겨 선다. 예전엔 첫 칸 자리(사다리 바깥 끝)에 선 채 높이만 올라가,
            // 끝까지 올리면 발이 아래 칸들 앞 허공에 떠 있었다(맨 위 발판은 사다리 가운데 쪽).
            if (lv > 0) { Vector3 t = s_pcLadder.StandPoint(lv, s_pcSign); player.position = new Vector3(t.x, pos.y, t.z); }
            else s_pcLadder = null;
            WorldSfx.TorqueClick(pos);
            return true;
        }
        if (dir < 0) return false;

        // 오르기 시작은 '사다리를 바라보고 바로 앞(발판 가장자리 0.4m 안)에 섰을 때'만 — 예전엔 발판 0.7m 안이면 어느 쪽을 보든 Q 가 사다리로 가로채져
        // 차 밑·작업대 옆에서 높이를 못 올리고 사다리 위로 순간이동했다. 아니면 false → 원래 높이 조절.
        var cam = Camera.main;
        Vector3 look = cam != null ? cam.transform.forward : player.forward; look.y = 0f;
        look = look.sqrMagnitude > 1e-4f ? look.normalized : player.forward;

        LadderProp best = null; float bestD = float.MaxValue;
        foreach (var l in s_all)
        {
            if (l == null || l._n <= 0 || (l._sync != null && l._sync.IsGrabbed)) continue;
            if (!l.NearFootprint(pos, 0.4f)) continue;
            Vector3 d = l.transform.TransformPoint(l._fpCenter) - pos; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f && Vector3.Dot(look, d.normalized) < 0.3f) continue;   // 사다리를 등지거나 옆으로 보고 있으면 제외
            if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = l; }
        }
        if (best == null) return false;

        s_pcLadder = best;
        s_pcLevel = 1;
        // 내 쪽 발판 위로 옮겨 선다(사다리 옆 허공에 떠 보이지 않게)
        Vector3 lp = best.transform.InverseTransformPoint(pos) - best._fpCenter;
        s_pcSign = (best._alongX ? lp.x : lp.z) >= 0f ? 1f : -1f;
        Vector3 target = best.StandPoint(1, s_pcSign);
        player.position = new Vector3(target.x, pos.y, target.z);
        WorldSfx.TorqueClick(pos);
        return true;
    }

    /// <summary>lv 번째 칸 발판의 가운데(월드). 칸이 오를수록 사다리 가운데(맨 위 발판) 쪽으로 들어간다.</summary>
    private Vector3 StandPoint(int lv, float sign)
    {
        float top = _topHalf * 0.5f;
        float w;
        if (_n == 1 || lv >= _n) w = top;
        else if (lv <= 1) w = Mathf.Max(top, _half - _run * 0.5f - 0.1f);      // 첫 칸: 예전 위치 그대로
        else w = Mathf.Max(top, _half - _run * (lv - 1) - _run * 0.5f);         // lv 칸 띠의 가운데
        Vector3 local = _fpCenter + (_alongX ? new Vector3(sign * w, 0f, 0f) : new Vector3(0f, 0f, sign * w));
        return transform.TransformPoint(local);
    }

    /// <summary>놓여 있는(누가 들고 있지 않은) 사다리 발판 근처인가 — VR 오르기 보조가 쓴다.</summary>
    public static bool AnyNear(Vector3 worldPos, float margin)
    {
        foreach (var l in s_all)
            if (l != null && l._n > 0 && (l._sync == null || !l._sync.IsGrabbed) && l.NearFootprint(worldPos, margin)) return true;
        return false;
    }

    private bool NearFootprint(Vector3 worldPos, float margin)
    {
        Vector3 lp = transform.InverseTransformPoint(worldPos) - _fpCenter;
        float along = Mathf.Abs(_alongX ? lp.x : lp.z);
        float across = Mathf.Abs(_alongX ? lp.z : lp.x);
        return along <= _half + margin && across <= _side + margin;
    }

    private float HeightAt(Vector3 worldPos)
    {
        Vector3 lp = transform.InverseTransformPoint(worldPos) - _fpCenter;
        float along = Mathf.Abs(_alongX ? lp.x : lp.z);
        float across = Mathf.Abs(_alongX ? lp.z : lp.x);
        if (across > _side + 0.1f) return 0f;
        int steps = 0;
        for (int i = 0; i < _n; i++)
        {
            float w = _n == 1 ? Mathf.Max(0.14f, _half * 0.3f) : _half - _run * i;
            if (along <= w) steps = i + 1;
        }
        return steps * _rise * transform.lossyScale.y;
    }

    private void BuildSteps()
    {
        if (!EquipFx.LocalBounds(transform, out var b)) return;
        _steps = new GameObject("Steps");
        _steps.transform.SetParent(transform, false);
        _steps.layer = 0;
        _steps.AddComponent<ClimbSurface>();

        float bottom = b.min.y;
        float stand = Mathf.Min(maxStand, b.size.y * standRatio);
        int n = Mathf.Max(1, Mathf.CeilToInt(stand / maxRise));
        float rise = stand / n;
        _standY = bottom + stand;

        bool alongX = b.size.x >= b.size.z;
        float half = (alongX ? b.extents.x : b.extents.z) + 0.15f;   // 앞쪽으로 조금 더 — 첫 칸을 밟기 쉽게
        float side = (alongX ? b.extents.z : b.extents.x);
        float topHalf = Mathf.Max(0.14f, half * 0.3f);                // 맨 위 발판 반 폭
        float run = (half - topHalf) / Mathf.Max(1, n - 1);

        _fpCenter = new Vector3(b.center.x, 0f, b.center.z);
        _alongX = alongX; _half = half; _side = side; _rise = rise; _run = run; _n = n; _bottomY = bottom; _topHalf = topHalf;

        for (int i = 0; i < n; i++)
        {
            float h = rise * (i + 1);
            float w = n == 1 ? topHalf : half - run * i;
            var bc = _steps.AddComponent<BoxCollider>();
            Vector3 size = alongX ? new Vector3(w * 2f, 0.04f, side * 2f) : new Vector3(side * 2f, 0.04f, w * 2f);
            bc.size = size;
            bc.center = new Vector3(b.center.x, bottom + h - 0.02f, b.center.z);
        }
        if (Utils.GetPlatformType() == PlatformType.VR) { BuildRungs(b, bottom); VrLadderClimbAssist.Ensure(); }
    }

    /// <summary>
    /// VR: 각 발판 앞 모서리에 잡고 당겨 오를 수 있는 가로대(AutoHand Climbable)를 만든다.
    /// (11/13) AutoHand 데모(Zone·MonkeyBar)와 같은 구성으로 다시 만듦:
    ///  · 예전엔 가로대가 사다리(동적 Rigidbody + SyncGrab Grabbable) 의 자식이라, 사다리 Grabbable 의 makeChildrenGrabbable 이
    ///    가로대 충돌체까지 '사다리 손잡이'로 삼아 가로대를 잡으면 사다리 전체가 손에 끌려 흔들리고 미끄러지듯 날아갔다
    ///  · 이제 가로대는 사다리 밖의 별도 루트(키네마틱 Rigidbody 하나)에 모으고, 각 가로대는 Grabbable(body=그 루트) + Climbable 만 가진다
    ///  · 루트는 매 프레임 사다리 자세를 따라간다(사다리를 옮겨도 가로대가 같이 이동)
    /// </summary>
    private Rigidbody _rungBody;
    private void BuildRungs(Bounds b, float bottom)
    {
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer < 0) layer = gameObject.layer;
        _rungs = new GameObject($"{name}_ClimbRungs");
        _rungs.transform.SetPositionAndRotation(transform.position, transform.rotation);
        _rungs.transform.localScale = transform.lossyScale;
        _rungBody = _rungs.AddComponent<Rigidbody>();
        _rungBody.isKinematic = true;
        _rungBody.useGravity = false;
        _rungBody.interpolation = RigidbodyInterpolation.None;
        for (int i = 0; i < _n; i++)
        {
            float h = bottom + _rise * (i + 1) + 0.03f;
            float w = _n == 1 ? _topHalf : _half - 0.15f - _run * i;   // 모델 다리 쪽(앞으로 늘린 0.15 는 뺀다)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var r = new GameObject($"Rung {i + 1}{(sgn < 0 ? "a" : "b")}");
                if (layer >= 0) r.layer = layer;
                r.transform.SetParent(_rungs.transform, false);
                r.transform.localPosition = new Vector3(b.center.x, h, b.center.z) + (_alongX ? new Vector3(sgn * w, 0f, 0f) : new Vector3(0f, 0f, sgn * w));
                var bc = r.AddComponent<BoxCollider>();
                bc.size = _alongX ? new Vector3(0.05f, 0.05f, _side * 1.6f) : new Vector3(_side * 1.6f, 0.05f, 0.05f);
                var g = r.AddComponent<Grabbable>();
                g.body = _rungBody;
                g.parentOnGrab = false;
                g.singleHandOnly = false;
                g.allowHeldSwapping = true;
                g.makeChildrenGrabbable = false;
                var c = r.AddComponent<Climbable>();
                c.axis = Vector3.one;
            }
        }
    }

    private void FollowRungs()
    {
        if (_rungs == null) return;
        var t = _rungs.transform;
        if ((t.position - transform.position).sqrMagnitude > 1e-8f || Quaternion.Angle(t.rotation, transform.rotation) > 0.01f)
        {
            t.SetPositionAndRotation(transform.position, transform.rotation);
            if (_rungBody != null) { _rungBody.position = transform.position; _rungBody.rotation = transform.rotation; }
        }
    }

    private void OnDestroy()
    {
        if (_rungs != null) Destroy(_rungs);
    }

    private void LateUpdate()
    {
        if (_steps == null) return;
        bool held = _sync != null && _sync.IsGrabbed;
        if (_steps.activeSelf == held) _steps.SetActive(!held);
        if (_rungs != null && _rungs.activeSelf == held) _rungs.SetActive(!held);
        if (!held) FollowRungs();

        // 가까이 가면 오르는 방법 안내
        var cam = Camera.main;
        if (held || cam == null || _n <= 0) return;
        if (!NearFootprint(cam.transform.position, 0.7f)) return;
        var plat = Utils.GetPlatformType();
        if (plat != PlatformType.VR && s_pcLevel <= 0)
        {
            // Q 오르기는 사다리를 바라보고 0.4m 안일 때만 동작 — 안내도 그때만 (TryClimbStep 과 같은 조건)
            Vector3 dd = transform.TransformPoint(_fpCenter) - cam.transform.position; dd.y = 0f;
            Vector3 lk = cam.transform.forward; lk.y = 0f;
            if (!NearFootprint(cam.transform.position, 0.4f) || (dd.sqrMagnitude > 1e-4f && lk.sqrMagnitude > 1e-4f && Vector3.Dot(lk.normalized, dd.normalized) < 0.3f)) return;
        }
        if (plat == PlatformType.VR)
            EquipFx.Notice("ladder_hint_vr", "사다리", "발판 앞 가로대를 잡고 아래로 당기면 올라갑니다. 발판 위로 걸어 올라서도 됩니다", EquipFx.Info, 2.5f, 40f);
        else if (plat == PlatformType.PC)
            EquipFx.Notice("ladder_hint", "사다리", s_pcLevel > 0 ? "Q = 한 칸 더 오르기, E = 한 칸 내려오기" : "Q = 사다리 오르기 (E = 내려오기)", EquipFx.Info, 2.5f, 40f);
        else
            EquipFx.Notice("ladder_hint_m", "사다리", "'서기' 버튼 = 한 칸 오르기, '앉기' = 내려오기", EquipFx.Info, 2.5f, 40f);
    }
}

/// <summary>밟고 올라설 수 있는 면(사다리 발판 등). 네트워크 물체 위라도 PC·모바일 접지가 바닥으로 인정한다.</summary>
public class ClimbSurface : MonoBehaviour, IClimbSurface { }

// ============================================================
//  VrLadderClimbAssist — VR 사다리 오르기 보조
//
//  원인(11/16 VR 테스트 '사다리 여전히 안 올라가짐'): VR 플레이어 몸통 Rigidbody 가 높이(Y) 고정(Constraints 116)이고
//  접지(useGrounding)도 꺼져 있어, 가로대를 잡고 당겨도(AutoHand 오르기 힘) 몸이 위로 움직일 수 없었다. 발판 콜라이더도 마찬가지.
//  대책: 사다리 가까이 있거나 가로대를 잡고 오르는 동안에만 Y 고정을 풀고 접지를 켠다(발판 위에 서고, 걸어 나가면 바닥으로 내려옴).
//  사다리에서 떨어져 원래 바닥 높이로 돌아오면 원래 설정(Y 고정·접지 끔)으로 되돌린다 — 평소 이동감은 그대로.
// ============================================================
public class VrLadderClimbAssist : MonoBehaviour
{
    private static VrLadderClimbAssist s_inst;
    private AutoHandPlayer _player;
    private Rigidbody _body;
    private float _nextFind;
    private bool _active;
    private float _baseY;
    private RigidbodyConstraints _origConstraints;
    private bool _origGrounding, _origGravity;

    /// <summary>LadderProp 가 VR 에서 생성될 때 부른다.</summary>
    public static void Ensure()
    {
        if (s_inst != null) return;
        var go = new GameObject("[VrLadderClimbAssist]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<VrLadderClimbAssist>();
    }

    private void FindPlayer()
    {
        if (_player != null || Time.unscaledTime < _nextFind) return;
        _nextFind = Time.unscaledTime + 2f;
        foreach (var p in FindObjectsByType<AutoHandPlayer>(FindObjectsSortMode.None))
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            var nob = p.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 아바타 제외
            _player = p;
            _body = p.GetComponent<Rigidbody>();
            break;
        }
    }

    private void FixedUpdate()
    {
        FindPlayer();
        if (_player == null || _body == null) { _active = false; return; }

        Vector3 pos = _body.position;
        bool climbing = _player.IsClimbing();
        bool near = LadderProp.AnyNear(pos, 0.6f);

        if (!_active)
        {
            if (!climbing && !near) return;
            _active = true;
            _baseY = pos.y;
            _origConstraints = _body.constraints;
            _origGrounding = _player.useGrounding;
            _origGravity = _body.useGravity;
            _body.constraints = _origConstraints & ~RigidbodyConstraints.FreezePositionY;
            _player.useGrounding = true;
            return;
        }

        // 바닥보다 아래로 빠지면 바로 원래 높이로(바닥 콜라이더가 접지 레이어에 없을 때 대비)
        if (pos.y < _baseY - 0.03f)
        {
            pos.y = _baseY;
            _body.position = pos;
            var v = _body.linearVelocity; if (v.y < 0f) v.y = 0f; _body.linearVelocity = v;
        }

        // 사다리에서 벗어나 원래 바닥 높이로 돌아왔으면 원래 설정으로
        if (!climbing && !near && pos.y <= _baseY + 0.03f)
        {
            pos.y = _baseY;
            _body.position = pos;
            var v = _body.linearVelocity; v.y = 0f; _body.linearVelocity = v;
            _body.constraints = _origConstraints;
            _player.useGrounding = _origGrounding;
            _body.useGravity = _origGravity;
            _active = false;
        }
    }
}
