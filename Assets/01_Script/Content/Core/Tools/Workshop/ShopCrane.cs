using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// ============================================================
//  ShopCrane.cs
//  크레인 자키(숍 크레인) — 매뉴얼 장비표 '크레인 자키: 고전압 배터리 시스템 어셈블리·배터리 모듈 어셈블리 탈거 시 사용'.
//  서브 프레임·모터·배터리 팩처럼 무거운 부품을 차에서 내리고 올릴 때 쓴다(엔진 서포트 빔 09200-3N000 과 같은 용도).
//
//  · 바닥에서 끌어 옮긴다(FloorDragProp — 프리팹이 같이 가진다)
//  · 펌프 손잡이를 누르면 붐이 올라가고(한 번에 약 8%), 밸브를 누르면 내려간다 — 높이는 서버를 거쳐 모두에게 같다
//  · 낮춘 갈고리 근처(약 0.8 m)에 떨어진 부품이 있으면 갈고리를 눌러 건다. 걸린 부품은 갈고리에 매달려 붐과 함께 오르내리고 크레인과 함께 옮겨진다
//  · 다시 갈고리를 누르면 내려놓는다. 떼어낸 차 부품(CarPartItem)이 원래 자리 근처면 그대로 장착(체결 전) 상태가 된다
//  · 누가 손으로 집으면 자동으로 풀린다
//  모양은 프리팹 빌더(Editor/EquipmentBuilder)가 도형으로 만든다 — 자식 이름: Arm(붐, 마스트를 따라 오르내림), Hook(갈고리), Pump, Valve
// ============================================================
[DefaultExecutionOrder(1100)]   // 네트워크 보간 뒤에 매달린 부품을 갈고리에 붙인다
public class ShopCrane : NetworkBehaviour
{
    public Transform arm, hook, pump, valve;
    [Tooltip("붐(Arm)의 로컬 높이 — 가장 낮을 때/높을 때")] public float armMinY = 0.30f, armMaxY = 1.55f;
    [Tooltip("갈고리에 걸 수 있는 거리(m)")] public float hookReach = 0.8f;

    public static readonly System.Collections.Generic.List<ShopCrane> All = new System.Collections.Generic.List<ShopCrane>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private readonly SyncVar<float> _h = new SyncVar<float>(0.1f);
    private readonly SyncVar<int> _load = new SyncVar<int>(-1);
    private float _hShown = 0.1f;
    private float _pumpKick;
    private Vector3 _hookLocal0;

    private void Awake()
    {
        if (arm == null) arm = transform.Find("Arm");
        if (hook == null) hook = transform.Find("Arm/Hook") ?? transform.Find("Hook");
        if (pump == null) pump = transform.Find("Pump");
        if (valve == null) valve = transform.Find("Valve");
        if (hook != null) _hookLocal0 = hook.localPosition;
        AttachClick(pump, "크레인 펌프 손잡이", "누르면 붐이 올라갑니다 (여러 번)", () => Raise(0.08f));
        AttachClick(valve, "크레인 유압 밸브", "누르면 붐이 내려갑니다", () => Raise(-0.08f));
        AttachClick(hook, "크레인 갈고리", HookHint(), ToggleHook);
    }

    private string HookHint() => _load.Value >= 0 ? "눌러서 내려놓기(풀기)" : "눌러서 근처 부품 걸기 (갈고리를 부품 위로 내린 뒤)";

    private void AttachClick(Transform t, string name, string hint, System.Action act)
    {
        if (t == null) return;
        if (t.GetComponent<Collider>() == null)
        {
            var sc = t.gameObject.AddComponent<SphereCollider>();
            sc.radius = 0.12f / Mathf.Max(0.0001f, t.lossyScale.x);
        }
        var proxy = t.gameObject.GetComponent<ClickProxy>() ?? t.gameObject.AddComponent<ClickProxy>();
        proxy.displayName = name;
        proxy.vrPokeable = true;
        proxy.hover = touch => t == hook ? HookHint() : hint;
        proxy.onClick = () => act();
    }

    private void Raise(float delta)
    {
        _pumpKick = 0.25f;
        AudioSource.PlayClipAtPoint(EquipFx.Ratchet, transform.position, 0.4f);
        RaiseServerRpc(delta);
    }

    private void ToggleHook()
    {
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
        HookServerRpc();
    }

    // ── 서버 ──
    [ServerRpc(RequireOwnership = false)]
    private void RaiseServerRpc(float delta)
    {
        if (float.IsNaN(delta)) return;
        _h.Value = Mathf.Clamp01(_h.Value + Mathf.Clamp(delta, -0.1f, 0.1f));
    }

    [ServerRpc(RequireOwnership = false)]
    private void HookServerRpc()
    {
        if (_load.Value >= 0) { ReleaseLoad(); return; }
        if (hook == null) return;
        Vector3 hp = hook.position;
        float best = hookReach;
        NetworkObject pick = null;
        foreach (var sg in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (sg == null || sg.gameObject == gameObject || sg.IsGrabbed || !sg.IsSpawned) continue;
            if (sg.GetComponent<ShopCrane>() != null || sg.GetComponent<FloorDragProp>() != null) continue;
            var nob = sg.GetComponent<NetworkObject>();
            if (nob == null) continue;
            float d = EquipFx.LocalBounds(sg.transform, out var b) ? Vector3.Distance(sg.transform.TransformPoint(b.center), hp) : Vector3.Distance(sg.transform.position, hp);
            if (d < best) { best = d; pick = nob; }
        }
        if (pick != null) _load.Value = pick.ObjectId;
    }

    [Server]
    private void ReleaseLoad()
    {
        var nob = LoadObject();
        _load.Value = -1;
        if (nob == null) return;
        var item = nob.GetComponent<CarPartItem>();
        var svc = CarPartService.Instance;
        if (item != null && svc != null && item.Part >= 0 && svc.IsFreeSlotNear(item.Part, nob.transform.position, svc.MountDistance) && svc.CanMount(item.Part))
        {
            svc.MountServerRpc(item.Part, nob);   // 원래 자리에 내렸다 — 장착(체결 전)
            return;
        }
        var sg = nob.GetComponent<SyncGrab>();
        if (sg != null) sg.ServerDropToFloor();
    }

    private NetworkObject LoadObject()
    {
        int id = _load.Value;
        if (id < 0) return null;
        NetworkObject nob = null;
        if (IsServerStarted) ServerManager.Objects.Spawned.TryGetValue(id, out nob);
        else if (IsClientStarted) ClientManager.Objects.Spawned.TryGetValue(id, out nob);
        return nob;
    }

    // ── 모두 ──
    private void Update()
    {
        _hShown = Mathf.MoveTowards(_hShown, _h.Value, Time.deltaTime * 0.35f);
        if (arm != null)
        {
            var p = arm.localPosition;
            arm.localPosition = new Vector3(p.x, Mathf.Lerp(armMinY, armMaxY, _hShown), p.z);
        }
        if (pump != null)
        {
            _pumpKick = Mathf.MoveTowards(_pumpKick, 0f, Time.deltaTime);
            pump.localRotation = Quaternion.Euler(-_pumpKick * 40f, 0f, 0f);
        }
        // 서버: 누가 손으로 집은 부품은 풀어 준다
        if (IsServerStarted && _load.Value >= 0)
        {
            var nob = LoadObject();
            if (nob == null) _load.Value = -1;
            else
            {
                var sg = nob.GetComponent<SyncGrab>();
                if (sg != null && sg.IsGrabbed) _load.Value = -1;
            }
        }
    }

    private void LateUpdate()
    {
        if (hook == null || _load.Value < 0) return;
        var nob = LoadObject();
        if (nob == null) return;
        var sg = nob.GetComponent<SyncGrab>();
        if (sg != null && sg.IsGrabbed) return;
        Vector3 center = nob.transform.position;
        float ext = 0.1f;
        if (EquipFx.LocalBounds(nob.transform, out var b))
        {
            center = nob.transform.TransformPoint(b.center);
            ext = b.extents.y * Mathf.Abs(nob.transform.lossyScale.y);
        }
        Vector3 want = hook.position + Vector3.down * (ext + 0.06f);
        nob.transform.position += want - center;
        var rb = nob.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
    }
}
