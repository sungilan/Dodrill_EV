using System.Collections.Generic;
using EPOOutline;
using UnityEngine;

/// <summary>
/// 손에 든 물건의 강조 윤곽선(분홍·청록 깜빡임)을 끈다. 안내가 '이 도구를 집으세요'로 켜 둔 윤곽선이 집은 뒤에도 남아 있었다(VR 테스트 11/19).
/// 놓으면 원래 켜져 있던 것만 다시 켠다. 게임 시작 시 자동 설치, 서버(batchmode)에서는 안 한다.
/// </summary>
public class HeldOutlineSuppressor : MonoBehaviour
{
    private readonly Dictionary<Outlinable, bool> _hidden = new Dictionary<Outlinable, bool>();
    private float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<HeldOutlineSuppressor>() != null) return;
        var go = new GameObject("[HeldOutlineSuppressor]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<HeldOutlineSuppressor>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.15f;

        foreach (var ti in TaskItem.Registry.Values)
        {
            if (ti == null) continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg == null) continue;
            var o = ti.GetComponent<Outlinable>();
            if (o == null) continue;
            if (sg.IsGrabbed)
            {
                if (!_hidden.ContainsKey(o)) { _hidden[o] = o.enabled; }
                if (o.enabled) o.enabled = false;
            }
        }
        // 놓았으면 되돌린다
        List<Outlinable> back = null;
        foreach (var kv in _hidden)
        {
            var o = kv.Key;
            if (o == null) { (back ?? (back = new List<Outlinable>())).Add(o); continue; }
            var sg = o.GetComponent<SyncGrab>();
            if (sg != null && !sg.IsGrabbed) { o.enabled = kv.Value; (back ?? (back = new List<Outlinable>())).Add(o); }
        }
        if (back != null) foreach (var o in back) _hidden.Remove(o);
    }
}
