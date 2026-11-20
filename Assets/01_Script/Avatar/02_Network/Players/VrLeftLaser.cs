using Autohand;
using Autohand.Demo;
using UnityEngine;

// ============================================================
//  VrLeftLaser.cs
//
//  본인 VR 손 중 오른손에만 거리 잡기 레이저(HandDistanceGrabber, 그립=레이저 / 트리거=당겨 잡기)가 있어서
//  오른손에 도구를 든 채 왼손으로 다른 물건을 집을 방법이 손을 직접 뻗는 것뿐이었다(양손에 하나씩 들기 불가).
//  오른손 GrabPointer 를 복제해 왼손에 좌우 대칭으로 붙이고, 입력은 왼손 컨트롤러 링크의 그립/트리거로 바꾼다.
//  · 위치·회전은 손바닥(palmTransform) 기준으로 좌우 반전 — AutoHand 의 좌/우 손바닥 축은 같은 의미로 맞춰져 있다
//  · 레이어 마스크는 오른손 것을 그대로 따라간다(SyncGrab.EnsureLaserHitsLayer 가 나중에 추가해도 반영)
// ============================================================
public class VrLeftLaser : MonoBehaviour
{
    private HandDistanceGrabber _src, _dst;
    private int _tries;

    public static void Ensure(GameObject player)
    {
        if (player != null && player.GetComponent<VrLeftLaser>() == null) player.AddComponent<VrLeftLaser>();
    }

    private void Update()
    {
        if (_dst != null)
        {
            if (_src != null && _dst.layers.value != _src.layers.value) _dst.layers = _src.layers;
            return;
        }
        if (++_tries > 600) { enabled = false; return; }   // 10초쯤 찾다가 포기
        if (_tries % 15 == 0) TrySetup();
    }

    private void TrySetup()
    {
        Hand left = null, right = null;
        foreach (var h in GetComponentsInChildren<Hand>(false))
        {
            if (h == null || !h.isActiveAndEnabled) continue;
            if (h.left) { if (left == null) left = h; } else if (right == null) right = h;
        }
        if (left == null || right == null || left.palmTransform == null || right.palmTransform == null) return;

        HandDistanceGrabber src = null;
        foreach (var g in GetComponentsInChildren<HandDistanceGrabber>(true))
        {
            if (g == null || g.primaryHand == null) continue;
            if (g.primaryHand == left) return;                // 이미 왼손 레이저가 있다
            if (g.primaryHand == right) src = g;
        }
        if (src == null) return;

        OpenXRHandControllerLink leftLink = null;
        foreach (var l in GetComponentsInChildren<OpenXRHandControllerLink>(true))
            if (l != null && l.hand == left) { leftLink = l; break; }
        if (leftLink == null) return;

        // 손바닥 기준 자세를 좌우 반전
        Transform rp = right.palmTransform, lp = left.palmTransform;
        Vector3 lpos = rp.InverseTransformPoint(src.transform.position);
        Quaternion lrot = Quaternion.Inverse(rp.rotation) * src.transform.rotation;
        lpos.x = -lpos.x;
        lrot = new Quaternion(lrot.x, -lrot.y, -lrot.z, lrot.w);

        var parent = left.transform;
        var go = Instantiate(src.gameObject, parent);
        go.name = "GrabPointer_L";
        go.transform.SetPositionAndRotation(lp.TransformPoint(lpos), lp.rotation * lrot);

        var dst = go.GetComponent<HandDistanceGrabber>();
        dst.primaryHand = left;
        dst.secondaryHand = right;
        dst.layers = src.layers;

        var link = go.GetComponent<OpenXRHandPointGrabLink>();
        if (link != null)
        {
            // OnEnable 에서 입력을 구독하므로 끄고 바꾼 뒤 다시 켠다
            link.enabled = false;
            link.pointGrab = dst;
            link.pointAction = leftLink.grabAction;
            link.stopPointAction = leftLink.releaseAction;
            link.selectAction = leftLink.squeezeAction;
            link.stopSelectAction = leftLink.stopSqueezeAction;
            link.enabled = true;
        }
        _src = src; _dst = dst;
        Debug.Log("[VrLeftLaser] 왼손 거리 잡기 레이저 추가");
    }
}
