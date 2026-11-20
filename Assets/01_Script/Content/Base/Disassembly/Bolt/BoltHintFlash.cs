using UnityEngine;

// ============================================================
//  BoltHintFlash.cs
//
//  볼트 윤곽선 잠깐 색 바꾸기 (EquipFx.cs 에서 분리, 11/20)
// ============================================================

/// <summary>볼트 윤곽선을 잠깐 다른 색으로 바꿨다가 원래대로 돌린다(BoltHint 노랑 표시용).</summary>
public class BoltHintFlash : MonoBehaviour
{
    private EPOOutline.Outlinable _o;
    private bool _created, _prevEnabled, _active;
    private Color _prevColor;
    private float _until;

    public void Flash(Color c, float seconds)
    {
        if (_o == null)
        {
            _o = GetComponent<EPOOutline.Outlinable>();
            _created = _o == null;
            if (_created) { _o = gameObject.AddComponent<EPOOutline.Outlinable>(); _o.AddAllChildRenderersToRenderingList(); _o.enabled = false; }
        }
        if (!_active)
        {
            _active = true;
            _prevEnabled = _o.enabled;
            _prevColor = _o.OutlineParameters.Color;
        }
        _o.enabled = true;
        _o.OutlineParameters.Enabled = true;
        _o.OutlineParameters.Color = c;
        _until = Time.unscaledTime + seconds;
    }

    private void Update()
    {
        if (!_active || _o == null) return;
        // 깜빡임(0.25초 주기) — 정지된 노랑보다 눈에 띈다
        float a = Mathf.PingPong(Time.unscaledTime * 4f, 1f) > 0.5f ? 1f : 0.35f;
        var col = _o.OutlineParameters.Color; col.a = a; _o.OutlineParameters.Color = col;
        if (Time.unscaledTime < _until) return;
        _active = false;
        _o.OutlineParameters.Color = _prevColor;
        _o.enabled = _prevEnabled;
    }

    private void OnDisable()
    {
        if (_active && _o != null) { _o.OutlineParameters.Color = _prevColor; _o.enabled = _prevEnabled; }
        _active = false;
    }
}
