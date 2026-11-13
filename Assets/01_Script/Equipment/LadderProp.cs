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
public class LadderProp : MonoBehaviour
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

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        BuildSteps();
    }

    private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() { s_all.Remove(this); }

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

        LadderProp best = null; float bestD = float.MaxValue;
        foreach (var l in s_all)
        {
            if (l == null || l._n <= 0 || (l._sync != null && l._sync.IsGrabbed)) continue;
            if (!l.NearFootprint(pos, 0.7f)) continue;
            Vector3 d = l.transform.TransformPoint(l._fpCenter) - pos; d.y = 0f;
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
        if (Utils.GetPlatformType() == PlatformType.VR) BuildRungs(b, bottom);
    }

    /// <summary>VR: 각 발판 앞 모서리에 잡고 당겨 오를 수 있는 가로대(AutoHand Climbable)를 만든다.</summary>
    private void BuildRungs(Bounds b, float bottom)
    {
        // 손이 잡는 레이어 = 이 프로젝트 장비와 같은 Interactable(15). 예전엔 AutoHand 기본값(Grabbable, 7)이라
        // 손의 잡기 마스크에 안 걸려 가로대를 잡을 수 없었다
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer < 0) layer = gameObject.layer;
        _rungs = new GameObject("Rungs");
        _rungs.transform.SetParent(transform, false);
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
                bc.size = _alongX ? new Vector3(0.04f, 0.04f, _side * 1.6f) : new Vector3(_side * 1.6f, 0.04f, 0.04f);
                var rb = r.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var g = r.AddComponent<Grabbable>();
                g.parentOnGrab = false;
                g.singleHandOnly = false;
                g.allowHeldSwapping = false;
                var c = r.AddComponent<Climbable>();
                c.axis = Vector3.one;
            }
        }
    }

    private void LateUpdate()
    {
        if (_steps == null) return;
        bool held = _sync != null && _sync.IsGrabbed;
        if (_steps.activeSelf == held) _steps.SetActive(!held);
        if (_rungs != null && _rungs.activeSelf == held) _rungs.SetActive(!held);

        // 가까이 가면 오르는 방법 안내
        var cam = Camera.main;
        if (held || cam == null || _n <= 0) return;
        if (!NearFootprint(cam.transform.position, 0.7f)) return;
        var plat = Utils.GetPlatformType();
        if (plat == PlatformType.VR)
            EquipFx.Notice("ladder_hint_vr", "사다리", "발판 앞 가로대를 잡고 아래로 당기면 올라갑니다. 발판 위로 걸어 올라서도 됩니다", EquipFx.Info, 2.5f, 40f);
        else if (plat == PlatformType.PC)
            EquipFx.Notice("ladder_hint", "사다리", s_pcLevel > 0 ? "Q = 한 칸 더 오르기, E = 한 칸 내려오기" : "Q = 사다리 오르기 (E = 내려오기)", EquipFx.Info, 2.5f, 40f);
        else
            EquipFx.Notice("ladder_hint_m", "사다리", "'서기' 버튼 = 한 칸 오르기, '앉기' = 내려오기", EquipFx.Info, 2.5f, 40f);
    }
}

/// <summary>밟고 올라설 수 있는 면(사다리 발판 등). 네트워크 물체 위라도 PC·모바일 접지가 바닥으로 인정한다.</summary>
public class ClimbSurface : MonoBehaviour { }
