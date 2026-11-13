using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  TapeMeasure.cs
//
//  줄자. 누르고 있는 동안(VR 트리거 / PC F / 모바일 작업 버튼) 테이프가 앞으로 뻗어 나가 닿는 곳까지의 거리를 보여 준다.
//  놓으면 '촤르륵' 감겨 들어간다. 모든 플레이어에게 테이프가 보인다(쓰는 중 상태는 EquipmentBase 가 동기화).
//  · PC·모바일: 테이프 방향 = 화면 가운데(카메라 앞)
// ============================================================
public class TapeMeasure : EquipmentBase
{
    protected override string MobileActionName => "길이 재기";

    [Tooltip("테이프가 나오는 곳(앞쪽 = 로컬 +Z)")] public Transform mouth;
    [Tooltip("테이프 모양(길이 1m 기준 Z 로 늘린다)")] public Transform blade;
    public float maxLength = 5f;
    public float extendSpeed = 3f;

    private float _len;
    private GameObject _label;
    private WorldHoverLabel _labelComp;
    private float _nextTick;

    protected override void Update()
    {
        base.Update();
        var m = mouth != null ? mouth : transform;
        float want = 0f;
        Vector3 dir = m.forward;
        if (Using)
        {
            if (!IsVR && IsLocalHeld && Camera.main != null) dir = Camera.main.transform.forward;
            want = maxLength;
            if (Physics.Raycast(m.position, dir, out var hit, maxLength, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform)) want = hit.distance;
        }
        float prev = _len;
        _len = Mathf.MoveTowards(_len, want, (want > _len ? extendSpeed : extendSpeed * 2.5f) * Time.deltaTime);
        if (Mathf.Abs(_len - prev) > 0.0001f && Time.time >= _nextTick)
        {
            _nextTick = Time.time + 0.05f;
            if (IsLocalHeld) HapticAll(0.01f, 0.15f);
        }

        if (blade != null)
        {
            blade.gameObject.SetActive(_len > 0.005f);
            blade.position = m.position + dir * (_len * 0.5f);
            blade.rotation = Quaternion.LookRotation(dir, m.up);
            blade.localScale = new Vector3(blade.localScale.x, blade.localScale.y, Mathf.Max(0.001f, _len) / Mathf.Max(1e-4f, transform.lossyScale.z));
        }
        UpdateLabel(m.position + dir * _len, Using && _len > 0.02f);
    }

    private void UpdateLabel(Vector3 tip, bool show)
    {
        if (!show || !IsLocalHeld) { if (_label != null && _label.activeSelf) _label.SetActive(false); return; }
        if (_label == null)
        {
            var prefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (prefab == null) return;
            _label = Instantiate(prefab);
            _label.name = "[TapeLabel]";
            foreach (var t in _label.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
            _labelComp = _label.GetComponent<WorldHoverLabel>();
        }
        if (!_label.activeSelf) _label.SetActive(true);
        _label.transform.position = tip + Vector3.up * 0.12f;
        if (_labelComp != null) _labelComp.SetText($"{_len * 100f:0.0} cm", _len >= maxLength - 0.01f ? "최대 길이" : "줄자");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_label != null) Destroy(_label);
    }
}
