using System.Collections.Generic;
using Autohand;
using FishNet.Object;
using TMPro;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  MountedPartPrompt.cs — 끼우기만 한(볼트를 아직 안 조인) 부품·휠 안내 (11/19)
//
//  떼어낸 부품·휠을 제자리에 대면 '장착(체결 전)' 상태가 되는데, 겉으로는 다 조립된 것처럼 보여 조임을 잊기 쉬웠다.
//  · 장착 상태인 부품·휠 위에 주황색 '조립 필요 · 볼트 조임 n/m' 배지를 띄운다(8 m 안).
//  · 조인 볼트가 아직 하나도 없으면 다시 뺄 수도 있다 — 잘못 끼웠거나 순서를 바꾸고 싶을 때.
//      PC   : 배지를 겨누고 U 키
//      모바일: 배지를 겨누면 '다시 빼기' 버튼
//      VR   : 손을 가까이 대면 배지 아래 '다시 빼기' 버튼(검지로 누름)
//    다시 빼면 그 자리에 부품(휠)이 다시 생긴다. 볼트를 하나라도 규정 토크까지 조였으면 먼저 풀어야 한다(배지에 안내).
//  서버 처리: CarPartService.Tray.cs / TireService.Tray.cs 의 RequestUnmount.
// ============================================================
public class MountedPartPrompt : MonoBehaviour
{
    public const KeyCode UnmountKey = KeyCode.U;
    private const float ShowDist = 8f, AimAngle = 9f, AimDist = 4.5f, VrReach = 0.8f;
    private const float BadgeW = 0.30f, BadgeH = 0.082f;
    private static readonly Color BadgeColor = new Color(0.55f, 0.3f, 0.02f, 0.93f);
    private static readonly Color AimColor = new Color(0.75f, 0.42f, 0.05f, 0.96f);
    private static readonly Color BtnColor = new Color(0.13f, 0.45f, 0.8f, 0.95f);

    private class Badge
    {
        public VrPartTray.PokeSurface s;
        public TextMeshPro text;
        public Material bg;
        public GameObject btn;
        public bool seen;
    }

    private readonly Dictionary<int, Badge> _badges = new Dictionary<int, Badge>();
    private readonly List<int> _mounted = new List<int>();
    private readonly List<int> _drop = new List<int>();
    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextScan, _nextHandScan;
    private TMP_FontAsset _font;
    private int _aimed = -1, _hintedFor = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<MountedPartPrompt>() != null) return;
        var go = new GameObject("[MountedPartPrompt]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<MountedPartPrompt>();
    }

    private void OnDestroy()
    {
        foreach (var b in _badges.Values) b.s?.Destroy();
        _badges.Clear();
    }

    private void LateUpdate()
    {
        var cam = Camera.main;
        var svc = CarPartService.Instance;
        if (cam == null || svc == null || !svc.Ready) { HideAll(); return; }
        bool vr = Utils.GetPlatformType() == PlatformType.VR;
        bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;

        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 0.25f; Collect(cam.transform.position); }
        if (vr && Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }

        // 겨눈 배지(PC·모바일) — 시선에 가장 가까운 것
        int aimed = -1;
        if (!vr)
        {
            float best = AimAngle;
            foreach (int code in _mounted)
            {
                Vector3 d = Anchor(code) - cam.transform.position;
                if (d.magnitude > AimDist) continue;
                float a = Vector3.Angle(cam.transform.forward, d);
                if (a < best) { best = a; aimed = code; }
            }
        }
        _aimed = aimed;

        foreach (var b in _badges.Values) b.seen = false;
        foreach (int code in _mounted) UpdateBadge(code, cam.transform, vr);
        _drop.Clear();
        foreach (var kv in _badges) if (!kv.Value.seen) _drop.Add(kv.Key);
        foreach (int code in _drop) { _badges[code].s?.Destroy(); _badges.Remove(code); }

        // 다시 빼기 — PC U 키 / 모바일 버튼
        bool can = aimed >= 0 && CanUnmount(aimed);
        if (!vr)
        {
            if (can && _hintedFor != aimed)
            {
                _hintedFor = aimed;
                EquipFx.Notice("unmount_hint", $"{PartTray.Name(aimed)} — 끼우기만 한 상태",
                    mobile ? "렌치로 볼트를 조이면 조립 · '다시 빼기' 버튼으로 뺄 수 있습니다" : "렌치로 볼트를 조이면 조립 · U 키: 다시 빼기", EquipFx.Info, 2.5f, 8f);
            }
            if (aimed < 0) _hintedFor = -1;
            if (can && !mobile && Input.GetKeyDown(UnmountKey)) Unmount(aimed);
            int code = aimed;
            MobileQuickButtons.Set("unmount", mobile && can, "다시 빼기", MobileQuickButtons.SlotLeft, () => Unmount(code));
        }
    }

    private void Collect(Vector3 eye)
    {
        _mounted.Clear();
        var svc = CarPartService.Instance;
        for (int p = 0; p < CarPartService.Defs.Length; p++)
            if (svc.StateOf(p) == (int)CarPartService.PartState.Mounted && (svc.PartCenter(p) - eye).sqrMagnitude < ShowDist * ShowDist) _mounted.Add(p);
        var ts = TireService.Instance;
        if (ts != null)
            for (int w = 0; w < TireService.Wheels; w++)
                if (ts.IsWheelMounted(w) && (ts.HubCenter(w) - eye).sqrMagnitude < ShowDist * ShowDist) _mounted.Add(PartTray.WheelBase + w);
    }

    private static Vector3 Anchor(int code)
    {
        if (PartTray.IsWheel(code)) return TireService.Instance.HubCenter(code - PartTray.WheelBase) + Vector3.up * 0.48f;
        return CarPartService.Instance.PartCenter(code) + Vector3.up * 0.22f;
    }

    private static bool CanUnmount(int code)
    {
        if (PartTray.IsWheel(code)) { var ts = TireService.Instance; return ts != null && ts.CanUnmount(code - PartTray.WheelBase); }
        var svc = CarPartService.Instance;
        return svc != null && svc.CanUnmount(code);
    }

    private static void Progress(int code, out int done, out int total)
    {
        if (PartTray.IsWheel(code))
        {
            done = TireService.Instance.NutsTightened(code - PartTray.WheelBase);
            total = TireService.NutsPerWheel;
            return;
        }
        done = CarPartService.Instance.BoltsTightened(code);
        total = CarPartService.Defs[code].bolts.Length;
    }

    private void UpdateBadge(int code, Transform cam, bool vr)
    {
        if (!_badges.TryGetValue(code, out var b)) { b = MakeBadge(code); _badges[code] = b; }
        b.seen = true;
        Progress(code, out int done, out int total);
        bool can = CanUnmount(code);
        bool aimed = code == _aimed;
        string tail = can
            ? (vr ? "렌치로 조이기 · 손을 대면 다시 빼기" : aimed ? (Utils.GetPlatformType() == PlatformType.Mobile ? "렌치로 조이기 · '다시 빼기' 버튼" : "렌치로 조이기 · <b>U</b> 다시 빼기") : "렌치로 볼트를 조이세요")
            : "렌치로 나머지 볼트를 조이세요";
        string txt = $"<b>조립 필요</b> · {(PartTray.IsWheel(code) ? "너트" : "볼트")} 조임 {done}/{total}\n<size=72%>{tail}</size>";
        if (b.text.text != txt) b.text.text = txt;
        b.bg.color = aimed ? AimColor : BadgeColor;

        Vector3 pos = Anchor(code);
        Vector3 look = pos - cam.position;
        if (look.sqrMagnitude < 1e-6f) look = cam.forward;
        b.s.root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));
        float dist = look.magnitude;
        b.s.root.transform.localScale = Vector3.one * (vr ? Mathf.Clamp(dist / 1.2f, 0.7f, 1.6f) : Mathf.Clamp(dist / 1.6f, 0.8f, 2.4f));
        if (!b.s.root.activeSelf) b.s.root.SetActive(true);

        // VR: 손이 가까우면 '다시 빼기' 버튼
        if (vr)
        {
            Transform tip = NearestTip(pos);
            bool showBtn = can && tip != null;
            if (b.btn.activeSelf != showBtn) { b.btn.SetActive(showBtn); if (showBtn) b.s.Arm(); }
            if (showBtn && b.s.Update(tip, out Hand by) == "unmount")
            {
                if (by != null) { try { by.PlayHapticVibration(0.03f, 0.5f); } catch { } }
                WorldSfx.PlayAt("UI_Click", pos, 0.6f);
                Unmount(code);
            }
        }
    }

    private Badge MakeBadge(int code)
    {
        if (_font == null)
        {
            var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
            var t = tagPrefab != null ? tagPrefab.GetComponentInChildren<TMP_Text>(true) : null;
            if (t != null) _font = t.font;
        }
        var b = new Badge { s = new VrPartTray.PokeSurface("[MountedBadge_" + code + "]", _font) };
        var q = b.s.AddQuad(Vector2.zero, new Vector2(BadgeW, BadgeH), BadgeColor, 0.001f, 3985);
        b.bg = q.GetComponent<MeshRenderer>().sharedMaterial;
        b.text = b.s.AddText("", Vector2.zero, new Vector2(BadgeW - 0.02f, BadgeH - 0.012f), TextAlignmentOptions.Center, Color.white);
        b.text.textWrappingMode = TextWrappingModes.NoWrap;
        b.btn = b.s.AddButton("unmount", "다시 빼기", new Vector2(0f, -BadgeH * 0.5f - 0.026f), new Vector2(0.13f, 0.038f), BtnColor).go;
        b.btn.SetActive(false);
        return b;
    }

    private void Unmount(int code)
    {
        if (!CanUnmount(code)) return;
        if (PartTray.IsWheel(code)) TireService.Instance.RequestUnmount(code - PartTray.WheelBase);
        else CarPartService.Instance.RequestUnmount(code);
        WarningPopup.Show("unmount", $"{PartTray.Name(code)} 다시 뺌", "그 자리에 내려놓았습니다 — 다시 들어서 옮기거나 보관함에 넣으세요", EquipFx.Info, 2.2f);
        _hintedFor = -1;
        _nextScan = 0f;
    }

    private void HideAll()
    {
        foreach (var b in _badges.Values) if (b.s != null && b.s.root != null && b.s.root.activeSelf) b.s.root.SetActive(false);
        MobileQuickButtons.Set("unmount", false);
    }

    // ── VR 손 ──
    private void ScanHands()
    {
        _hands.RemoveAll(h => h == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Contains(hand) || hand.name.StartsWith("HighlightHand")) continue;
            var nob = hand.GetComponentInParent<NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
            _hands.Add(hand);
        }
    }

    private Transform NearestTip(Vector3 at)
    {
        Transform best = null; float bestD = VrReach;
        foreach (var h in _hands)
        {
            if (h == null || !h.isActiveAndEnabled || h.fingers == null) continue;
            if (h.GetHeld() != null || h.holdingObj != null) continue;   // 물건을 든 손은 누르지 않는다
            foreach (var f in h.fingers)
            {
                if (f == null || f.fingerType != FingerEnum.index || f.tip == null) continue;
                float d = Vector3.Distance(f.tip.position, at);
                if (d < bestD) { bestD = d; best = f.tip; }
                break;
            }
        }
        return best;
    }
}
