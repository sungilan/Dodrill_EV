using System.Collections.Generic;
using Autohand;
using DG.Tweening;
using UnityEngine;

// ============================================================
//  VrPlacementGhost.cs
//
//  VR 전용 — 부품 장착 고스트. 지금 단계가 '부품을 정해진 곳에 가져다 놓기'(SmartKey→LockBox, 경고판, 새 PRA, 배터리 상부 커버 등)일 때
//  그 부품을 손에 들면, 놓을 자리에 같은 모양의 반투명 흰색 고스트가 뜬다.
//  · 가까이 가져가면 고스트가 초록색으로 바뀌고 손에 짧은 진동
//  · 초록색일 때 손을 놓으면 부품이 고스트 자리로 '착' 끌려가 고정된다(떨어지지 않음, 모두에게 같은 자리)
//  · 놓기 전에 부품이 존에 닿아 단계가 먼저 완료되면, 그때도 같은 자리로 끌어당겨 고정한다
//  놓을 자세(우선순위):
//    1) 씬에 'Final_{prefabId}_Model' 이 있으면 그 자세
//    2) 교체 부품(New_X)이면 떼어 낸 기존 부품(Old_X)이 처음 있던 자리·방향 — 새 PRA 는 기존 PRA 자리에 같은 방향으로
//    3) 존 중앙에 부품이 처음 놓여 있던 방향으로
//  도구(렌치·멀티미터·메거·태블릿·잭 등)는 대상이 아니다.
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrPlacementGhost : MonoBehaviour
{
    private const float NearDist = 0.22f;      // 이 안이면 초록 + 놓으면 스냅
    private const float SnapTime = 0.2f;
    private const float ArmGrace = 0.6f;       // 범위를 벗어나거나 단계가 바뀐 뒤에도 이 시간 동안은 스냅 허용

    private static readonly HashSet<string> ExcludedIds = new HashSet<string>
    {
        "ImpactWrench", "Multimeter", "Megger", "BlackProbe", "RedProbe", "InsulationTester",
        "GDS_Tablet", "InstalledBatteryJack", "BatteryJack", "BatteryPackPos", "EV_Coolant_Container",
    };

    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.28f);
    private static readonly Color NearColor = new Color(0.25f, 1f, 0.45f, 0.42f);

    private ScenarioStateReceiver _recv;
    private float _nextRecvFind;
    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;

    private readonly Dictionary<int, Quaternion> _restRot = new Dictionary<int, Quaternion>();
    private struct RestPose { public Vector3 center; public Quaternion rot; }
    private readonly Dictionary<string, RestPose> _restByPrefab = new Dictionary<string, RestPose>();   // 처음 본 자세(메시 중심·방향)
    private const float LateSnapDist = 0.6f;
    private readonly HashSet<string> _everGrabbed = new HashSet<string>();
    private float _nextRestPrefabScan;   // 존에 닿아 단계가 먼저 끝났을 때, 이 안이면 장착 자리로 끌어당긴다
    private readonly Dictionary<string, Transform> _finalModels = new Dictionary<string, Transform>();

    // 현재 고스트
    private GameObject _ghost;
    private Material _mat;
    private TaskItem _ghostItem;
    private string _ghostZone;
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private bool _near;
    private readonly List<UnityEngine.Mesh> _baked = new List<UnityEngine.Mesh>();

    // 스냅 대기
    private TaskItem _armedItem;
    private Vector3 _armedPos;
    private Quaternion _armedRot;
    private float _armedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrPlacementGhost>() != null) return;
        var go = new GameObject("[VrPlacementGhost]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrPlacementGhost>();
    }

    private void OnEnable() { if (!SyncGrab.VrReleaseInterceptors.Contains(TrySnap)) SyncGrab.VrReleaseInterceptors.Add(TrySnap); }

    private void OnDisable()
    {
        SyncGrab.VrReleaseInterceptors.Remove(TrySnap);
        HideGhost();
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        RecordRestPoses();

        var config = CurrentConfig();
        string zoneId = config != null ? config.targetZoneId : null;

        // 단계가 바뀜(존에 닿아 완료 등) — 들고 있던 부품이 고스트 근처였다면 손에서 떼어 그 자리로 끌어당긴다
        if (_ghostZone != null && zoneId != _ghostZone)
        {
            // 존이 커서 고스트에 닿기 전에 단계가 끝나는 경우가 있다(새 PRA) → 가까운 거리면 그때도 장착 자리로 끌어당긴다
            if ((_armedItem == null || Time.time >= _armedUntil) && _ghostItem != null
                && Vector3.Distance(_ghostItem.transform.position, _targetPos) < LateSnapDist)
            {
                _armedItem = _ghostItem; _armedPos = _targetPos; _armedRot = _targetRot; _armedUntil = Time.time + ArmGrace;
            }
            if (_armedItem != null && Time.time < _armedUntil)
            {
                var h = HandHolding(_armedItem);
                if (h != null) h.ForceReleaseGrab();   // → SyncGrab.OnVRRelease → TrySnap
            }
            HideGhost();
        }

        // 지금 들고 있는 대상 부품 찾기
        TaskItem held = null; Hand holder = null;
        if (config != null && !string.IsNullOrEmpty(zoneId) && config.requiredItems != null && config.requiredItems.Count > 0 && !zoneId.Contains("Player"))
        {
            foreach (var h in _hands)
            {
                if (h == null || !h.isActiveAndEnabled) continue;
                var g = h.GetHeld();
                if (g == null) continue;
                var ti = g.GetComponentInParent<TaskItem>();
                if (ti == null || !config.requiredItems.Contains(ti.prefabId) || !IsPlaceable(ti)) continue;
                held = ti; holder = h; break;
            }
        }

        if (held == null)
        {
            if (_ghost != null) HideGhost();
            return;
        }

        if (_ghost == null || _ghostItem != held || _ghostZone != zoneId)
        {
            HideGhost();
            if (!ResolveTarget(held, zoneId, out _targetPos, out _targetRot))
            {
                if (_warnedItem != held) { _warnedItem = held; Debug.LogWarning($"[PlacementGhost] {held.prefabId}: 놓을 자리를 못 찾음(존 '{zoneId}')"); }
                return;
            }
            Debug.Log($"[PlacementGhost] {held.prefabId} 고스트 → {_targetPos:F3} / {_targetRot.eulerAngles:F0}");
            BuildGhost(held);
            _ghostItem = held;
            _ghostZone = zoneId;
            _near = false;
        }

        float d = Vector3.Distance(held.transform.position, _targetPos);
        bool near = d < NearDist;
        if (near && !_near) Haptic(holder, 0.04f, 0.4f);
        _near = near;

        if (near)
        {
            _armedItem = held; _armedPos = _targetPos; _armedRot = _targetRot; _armedUntil = Time.time + ArmGrace;
        }

        // 색: 멀면 흰색(숨 쉬듯), 가까우면 초록
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
        Color c = near ? NearColor : new Color(IdleColor.r, IdleColor.g, IdleColor.b, IdleColor.a * pulse);
        SetColor(c);
        _ghost.transform.SetPositionAndRotation(_targetPos, _targetRot);
    }

    // ── 손을 놓을 때 ─────────────────────────────

    private TaskItem _warnedItem;

    private bool TrySnap(SyncGrab sg, Hand hand)
    {
        if (!isActiveAndEnabled || !VrProbeHandling.IsVRDevice || sg == null) return false;
        if (_armedItem == null || Time.time >= _armedUntil) return false;
        var ti = sg.GetComponent<TaskItem>() ?? sg.GetComponentInParent<TaskItem>();
        if (ti != _armedItem) return false;
        if (Vector3.Distance(sg.transform.position, _armedPos) > LateSnapDist) return false;

        Vector3 pos = _armedPos; Quaternion rot = _armedRot;
        _armedItem = null;
        HideGhost();

        var rb = sg.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }

        var t = sg.transform;
        DOTween.Kill(t);
        DOTween.Sequence()
            .Join(t.DOMove(pos, SnapTime).SetEase(Ease.OutBack, 1.2f))
            .Join(t.DORotateQuaternion(rot, SnapTime).SetEase(Ease.OutQuad))
            .SetTarget(t)
            .OnComplete(() =>
            {
                if (sg == null) return;
                t.SetPositionAndRotation(pos, rot);
                sg.ReleaseAndPlace(pos, rot);
                WorldSfx.PlayAt("Part_Assemble", pos, 0.8f);
                Haptic(hand, 0.08f, 0.7f);
            });
        return true;
    }

    // ── 놓을 자리 ─────────────────────────────

    private bool ResolveTarget(TaskItem item, string zoneId, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;

        // 1) 완성 모델이 있으면 그 자세 그대로
        var fm = FindFinalModel(item.prefabId);
        if (fm != null) { pos = fm.position; rot = fm.rotation; return true; }

        // 2) 교체 부품: 기존 부품(Old_X)이 처음 있던 자리·방향
        if (item.prefabId != null && item.prefabId.StartsWith("New_")
            && _restByPrefab.TryGetValue("Old_" + item.prefabId.Substring(4), out var old))
        {
            rot = old.rot;
            pos = old.center - rot * Vector3.Scale(LocalCenter(item), item.transform.lossyScale);
            return true;
        }

        // 2) 존 중앙에 부품의 중심을 맞춘다(방향은 처음 놓여 있던 방향)
        Transform zt = null; Collider zc = null;
        var iz = TaskInteractionZone.Find(zoneId);
        if (iz != null) { zt = iz.transform; zc = iz.GetComponent<Collider>(); }
        else
        {
            var cz = TaskConfirmZone.Find(zoneId);
            if (cz != null) { zt = cz.transform; zc = cz.GetComponent<Collider>(); }
        }
        if (zt == null) return false;

        Vector3 center = zt.position;
        if (zc is BoxCollider bc) center = zt.TransformPoint(bc.center);
        else if (zc is SphereCollider sc) center = zt.TransformPoint(sc.center);
        else if (zc is CapsuleCollider cc) center = zt.TransformPoint(cc.center);
        else { var r = zt.GetComponent<Renderer>(); if (r != null) center = r.bounds.center; }

        rot = _restRot.TryGetValue(item.GetInstanceID(), out var rr) ? rr : Quaternion.Euler(0f, item.transform.eulerAngles.y, 0f);

        // 부품 원점 → 메시 중심 오프셋(부품 로컬)
        Vector3 offset = rot * Vector3.Scale(LocalCenter(item), item.transform.lossyScale);
        pos = center - offset;
        return true;
    }

    /// <summary>부품 원점에서 메시 중심까지(부품 로컬, 크기 제외).</summary>
    private static Vector3 LocalCenter(TaskItem item)
    {
        if (!TryBounds(item.transform, out var b)) return Vector3.zero;
        return item.transform.InverseTransformPoint(b.center);
    }

    private Transform FindFinalModel(string prefabId)
    {
        if (string.IsNullOrEmpty(prefabId)) return null;
        if (_finalModels.TryGetValue(prefabId, out var t) && t != null) return t;
        string name = "Final_" + prefabId + "_Model";
        t = null;
        foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr != null && tr.name == name && tr.gameObject.scene.IsValid()) { t = tr; break; }
        _finalModels[prefabId] = t;
        return t;
    }

    private static bool TryBounds(Transform root, out Bounds b)
    {
        b = default; bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>부품이 손에 잡히기 전(처음 놓여 있던) 방향을 기억해 둔다.</summary>
    private void RecordRestPoses()
    {
        foreach (var kv in TaskItem.Registry)
        {
            var ti = kv.Value;
            if (ti == null || _restRot.ContainsKey(ti.GetInstanceID())) continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg != null && (sg.IsGrabbed || sg.IsHolstered)) continue;
            _restRot[ti.GetInstanceID()] = ti.transform.rotation;
        }

        // 부품별 '장착돼 있던 자세' — 한 번도 잡히지 않은 동안 계속 갱신한다(배터리 팩을 내리는 등 함께 움직이는 경우)
        if (Time.unscaledTime < _nextRestPrefabScan) return;
        _nextRestPrefabScan = Time.unscaledTime + 0.5f;
        foreach (var kv in TaskItem.Registry)
        {
            var ti = kv.Value;
            if (ti == null || !ti.isActiveAndEnabled || string.IsNullOrEmpty(ti.prefabId) || _everGrabbed.Contains(ti.prefabId)) continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg != null && (sg.IsGrabbed || sg.IsHolstered)) { _everGrabbed.Add(ti.prefabId); continue; }
            if (TryBounds(ti.transform, out var b)) _restByPrefab[ti.prefabId] = new RestPose { center = b.center, rot = ti.transform.rotation };
        }
    }

    // ── 고스트 메시 ─────────────────────────────

    private void BuildGhost(TaskItem item)
    {
        EnsureMaterial();
        var root = item.transform;
        _ghost = new GameObject("[PlacementGhost] " + item.prefabId);
        _ghost.layer = 1;
        _ghost.transform.SetPositionAndRotation(_targetPos, _targetRot);
        _ghost.transform.localScale = root.lossyScale;
        Vector3 rs = root.lossyScale;
        Quaternion inv = Quaternion.Inverse(root.rotation);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            UnityEngine.Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr)
            {
                mesh = new UnityEngine.Mesh();
                smr.BakeMesh(mesh, true);
                _baked.Add(mesh);
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null) continue;

            var go = new GameObject(r.name);
            go.layer = 1;
            go.transform.SetParent(_ghost.transform, false);
            Vector3 lp = root.InverseTransformPoint(r.transform.position);
            go.transform.localPosition = lp;
            go.transform.localRotation = inv * r.transform.rotation;
            Vector3 ls = r is SkinnedMeshRenderer ? Vector3.one : r.transform.lossyScale;   // BakeMesh(useScale) 는 이미 크기를 반영
            go.transform.localScale = new Vector3(SafeDiv(ls.x, rs.x), SafeDiv(ls.y, rs.y), SafeDiv(ls.z, rs.z));

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = _mat;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;

    private void HideGhost()
    {
        if (_ghost != null)
        {
            Destroy(_ghost);
        }
        foreach (var m in _baked) if (m != null) Destroy(m);
        _baked.Clear();
        _ghost = null; _ghostItem = null; _ghostZone = null; _near = false;
    }

    private void EnsureMaterial()
    {
        if (_mat != null) return;
        var src = GuideSystem.Instance != null ? GuideSystem.Instance.ghostMaterial : null;
        if (src != null) _mat = new Material(src);
        else
        {
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            _mat = new Material(sh);
        }
        _mat.name = "PlacementGhost";
        if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", null);
        if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", null);
        _mat.renderQueue = 3100;
    }

    private void SetColor(Color c)
    {
        if (_mat == null) return;
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * 0.6f);
    }

    // ── 공통 ─────────────────────────────

    private static bool IsPlaceable(TaskItem ti)
    {
        if (ExcludedIds.Contains(ti.prefabId)) return false;
        if (ti.GetComponentInChildren<ImpactWrench>(true) != null) return false;
        if (ti.GetComponentInChildren<MultimeterMaster>(true) != null) return false;
        if (ti.GetComponentInChildren<GreaseTool>(true) != null) return false;
        return true;
    }

    private ModuleConfig CurrentConfig()
    {
        if (_recv == null && Time.unscaledTime >= _nextRecvFind)
        {
            _nextRecvFind = Time.unscaledTime + 1f;
            _recv = FindAnyObjectByType<ScenarioStateReceiver>();
        }
        return _recv != null ? _recv.GetCurrentTaskConfig() : null;
    }

    private Hand HandHolding(TaskItem ti)
    {
        foreach (var h in _hands)
        {
            if (h == null) continue;
            var g = h.GetHeld();
            if (g != null && g.GetComponentInParent<TaskItem>() == ti) return h;
        }
        return null;
    }

    private void ScanHands()
    {
        _hands.RemoveAll(h => h == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Contains(hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hands.Add(hand);
        }
    }

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}
