using System;
using UnityEngine;

// ============================================================
//  LiquidGauge.cs
//
//  오일류(PourContainer)·오일 받이(FloorDragProp catchLiquid)의 현재 용량을 물체 위 작은 패널로 보여 준다.
//  · 들고 있거나 가까이(1.6m) 있을 때, 또는 양이 바뀌는 동안 보인다 — 여러 개면 하나만(들고 있는 것 우선)
//  · '0.6 / 1.0 L (60%)' + 막대, 비었으면 '비었음', 가득이면 '꽉 참' 을 함께 표시
//  패널은 이름표와 같은 ItemNameTagLabel(WorldHoverLabel) 프리팹을 쓴다.
// ============================================================
public class LiquidGauge : MonoBehaviour
{
    public Func<float> amount;
    public float capacity = 1f;
    public string title = "오일";
    public Func<bool> held;

    private GameObject _label;
    private WorldHoverLabel _comp;
    private float _last = -1f, _changedAt = -10f;
    private string _shown;

    public static LiquidGauge Attach(GameObject go, string title, float capacity, Func<float> amount, Func<bool> held)
    {
        var g = go.GetComponent<LiquidGauge>() ?? go.AddComponent<LiquidGauge>();
        g.title = title; g.capacity = Mathf.Max(0.001f, capacity); g.amount = amount; g.held = held;
        return g;
    }

    // 오일병이 여러 개 모인 선반 앞에서 패널이 겹쳐 뜨던 문제(11/16) — 한 번에 하나만 보인다.
    // 들고 있는 것 > 양이 바뀌는 중인 것 > 카메라에서 가장 가까운 것 순으로 하나를 고른다.
    private static readonly System.Collections.Generic.List<LiquidGauge> s_all = new System.Collections.Generic.List<LiquidGauge>();
    private float _score = float.MaxValue;   // 작을수록 우선, MaxValue = 보일 조건 아님
    private float _a;

    private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }

    private void Update()
    {
        _score = float.MaxValue;
        if (amount == null) return;
        _a = amount();
        if (!Mathf.Approximately(_a, _last)) { _last = _a; _changedAt = Time.time; }

        var cam = Camera.main;
        float d2 = cam != null ? (cam.transform.position - transform.position).sqrMagnitude : float.MaxValue;
        bool isHeld = held != null && held();
        bool changing = Time.time - _changedAt < 2f;
        if (isHeld) _score = -2f;
        else if (changing && d2 < 4f * 4f) _score = -1f + d2 * 0.0001f;
        else if (d2 < 1.6f * 1.6f) _score = d2;
    }

    private bool IsWinner()
    {
        if (_score == float.MaxValue) return false;
        foreach (var g in s_all)
        {
            if (g == null || g == this || !g.isActiveAndEnabled) continue;
            if (g._score < _score || (g._score == _score && g.GetInstanceID() < GetInstanceID())) return false;
        }
        return true;
    }

    private void LateUpdate()
    {
        if (amount == null) return;
        float a = _a;
        if (!IsWinner()) { if (_label != null && _label.activeSelf) _label.SetActive(false); return; }

        if (_label == null)
        {
            var prefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (prefab == null) return;
            _label = Instantiate(prefab);
            _label.name = "[LiquidGauge]";
            foreach (var t in _label.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
            _comp = _label.GetComponent<WorldHoverLabel>();
        }
        if (!_label.activeSelf) _label.SetActive(true);

        EquipFx.LocalBounds(transform, out var lb);
        _label.transform.position = transform.TransformPoint(lb.center) + Vector3.up * (lb.extents.y + 0.2f);

        float k = Mathf.Clamp01(a / capacity);
        string state = k <= 0.001f ? "<color=#FF6B6B>비었음</color>" : k >= 0.999f ? "<color=#FFB000>꽉 참</color>" : $"{k * 100f:0}%";
        const int bars = 20;
        int on = Mathf.RoundToInt(k * bars);
        string bar = "<color=#3DBBFF>" + new string('|', on) + "</color><color=#55606E>" + new string('|', bars - on) + "</color>";
        string text = $"{title} {a:0.0} / {capacity:0.0} L  ·  {state}";
        string key = text + bar;
        if (key != _shown && _comp != null) { _shown = key; _comp.SetText(text, bar); }
    }

    private void OnDisable() { s_all.Remove(this); if (_label != null) _label.SetActive(false); }
    private void OnDestroy() { if (_label != null) Destroy(_label); }
}
