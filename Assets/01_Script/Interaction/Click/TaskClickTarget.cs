using UnityEngine;

// ============================================================
//  TaskClickTarget.cs — 누르면 시나리오 신호를 내는 범용 클릭 대상 (모듈화, 2026-11-20)
//
//  버튼·스위치·레버처럼 '누르기'만 하는 물체에 붙인다. 콜라이더가 있어야 한다.
//  누르면 InteractionEvents.FireZoneActivated(targetId, "") → 서버로 전달 → 범용 Confirm 단계가 완료된다.
//  JSON: { "type": "Confirm", "targetObjName": "<targetId>" }
// ============================================================
[RequireComponent(typeof(Collider))]
public class TaskClickTarget : MonoBehaviour, IWorldClickable
{
    [Tooltip("JSON 의 targetObjName 과 같게. 비우면 오브젝트 이름")]
    public string targetId;
    public string displayName;
    public bool vrPokeable = true;

    [Tooltip("누를 때 살짝 들어가는 연출 거리(m). 0 이면 없음")]
    public float pressDepth = 0.01f;

    public string TargetId => string.IsNullOrEmpty(targetId) ? name : targetId;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? TargetId : displayName;
    public bool VrPokeable => vrPokeable;
    public string HoverMessage(bool touch) => touch ? "터치해서 누르기" : "클릭해서 누르기";

    private float _pressedUntil;
    private Vector3 _restLocal;

    private void Awake() => _restLocal = transform.localPosition;

    public void OnPCClick()
    {
        if (pressDepth > 0f)
        {
            Vector3 down = -transform.up * pressDepth;
            if (transform.parent != null) down = transform.parent.InverseTransformVector(down);
            transform.localPosition = _restLocal + down;
            _pressedUntil = Time.time + 0.15f;
        }
        InteractionEvents.FireZoneActivated(TargetId, string.Empty);
    }

    private void Update()
    {
        if (_pressedUntil > 0f && Time.time >= _pressedUntil)
        {
            transform.localPosition = _restLocal;
            _pressedUntil = 0f;
        }
    }
}
