using System.Collections.Generic;
using FishNet;
using UnityEngine;

// ============================================================
//  CarryBasket.cs
//
//  노란 플라스틱 바스켓. 들고 다니며 안에 공구·부품을 담아 옮긴다.
//  · 바구니 콜라이더는 바닥 + 네 벽으로 만든다(속이 비어 물건이 안에 들어간다) — 프리팹 빌더가 만든다
//  · 서버: 바구니 안(내부 상자)에 놓인 '아무도 안 잡은' 장비를 담긴 것으로 기억하고, 바구니가 움직이면 같이 옮긴다
//    담긴 물건을 누가 집으면 목록에서 빠진다. 위치는 각 장비의 네트워크 트랜스폼으로 모두에게 보인다.
// ============================================================
[DefaultExecutionOrder(1100)]   // 네트워크 보간·SyncGrab 이 위치를 정한 뒤에 담긴 물건을 바구니에 붙인다
public class CarryBasket : MonoBehaviour
{
    [Tooltip("내부 공간(이 물체 로컬) — 이 안에 중심이 있으면 담긴 것")] public Bounds inner = new Bounds(new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.24f, 0.35f));

    private class Item { public SyncGrab sg; public Vector3 lp; public Quaternion lr; }
    private readonly List<Item> _items = new List<Item>();
    private SyncGrab _self;
    private float _nextScan;
    private Vector3 _lastPos;
    private Quaternion _lastRot;

    public int Count => _items.Count;

    private void Awake() { _self = GetComponent<SyncGrab>(); }

    private void LateUpdate()
    {
        // 서버만 옮기면 다른 화면·바구니를 든 사람 화면에서는 담긴 물건이 네트워크 보간으로 늦게 따라와 바구니와 따로 노는 것처럼 보였다
        // → 서버·클라이언트 모두 같은 규칙으로 각자 화면에서 담긴 물건을 바구니에 즉시 붙인다(LateUpdate, 네트워크 보간 뒤에 실행).
        if (!InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted) return;
        bool serverOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;

        bool moved = (transform.position - _lastPos).sqrMagnitude > 1e-6f || Quaternion.Angle(transform.rotation, _lastRot) > 0.1f;
        _lastPos = transform.position; _lastRot = transform.rotation;

        // 담긴 물건 따라오게(누가 집었거나 사라졌으면 뺀다)
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.sg == null || !it.sg.IsSpawned || it.sg.IsGrabbed) { _items.RemoveAt(i); continue; }
            if (serverOnly && !moved) continue;   // 서버는 움직일 때만(네트워크로 퍼짐), 화면이 있는 쪽은 매 프레임 붙인다
            it.sg.transform.SetPositionAndRotation(transform.TransformPoint(it.lp), transform.rotation * it.lr);
            var rb = it.sg.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        if (Time.time < _nextScan) return;
        _nextScan = Time.time + 0.5f;
        Scan();
    }

    private void Scan()
    {
        foreach (var sg in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (sg == null || sg == _self || sg.IsGrabbed || !sg.IsSpawned) continue;
            if (sg.GetComponent<CarryBasket>() != null || sg.GetComponent<FloorDragProp>() != null) continue;
            Vector3 lp = transform.InverseTransformPoint(sg.transform.position);
            bool inside = inner.Contains(lp);
            int idx = _items.FindIndex(x => x.sg == sg);
            if (inside && idx < 0) _items.Add(new Item { sg = sg, lp = lp, lr = Quaternion.Inverse(transform.rotation) * sg.transform.rotation });
            else if (inside && idx >= 0 && !(_self != null && _self.IsGrabbed)) { _items[idx].lp = lp; _items[idx].lr = Quaternion.Inverse(transform.rotation) * sg.transform.rotation; }
            else if (!inside && idx >= 0 && !(_self != null && _self.IsGrabbed)) _items.RemoveAt(idx);
        }
    }
}
