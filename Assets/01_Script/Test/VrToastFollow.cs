using UnityEngine;

// ============================================================
//  VrToastFollow.cs
//
//  VR 작업 알림 토스트('~을(를) 착용했습니다' · '~를 완료하였습니다') 전용 따라오기.
//  원래는 정면 UI(VRUIFollower)와 함께 천천히(followSpeed 2) 따라와 머리를 돌리면 한참 뒤에야 따라왔다 →
//  선임 정비사 패널(MentorDialogue.FollowHead)과 똑같은 공식(수평 시선만, k = 1-exp(-9dt))으로 같은 속도로 따라오게 한다. (11/19)
//  토스트 오브젝트(CanvasGroup)에 TaskProgressUI.ShowToast 가 붙인다. 켜질 때마다 현재 시선 앞으로 바로 옮겨 놓고 시작한다.
// ============================================================
public class VrToastFollow : MonoBehaviour
{
    private bool _init;
    private VRUIFollower _follower;

    public static void Attach(Transform t)
    {
        if (t == null || !VrProbeHandling.IsVRDevice) return;
        if (t.GetComponent<VrToastFollow>() == null) t.gameObject.AddComponent<VrToastFollow>();
    }

    private void OnEnable() { _init = false; }

    private void LateUpdate()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        var cam = Camera.main;
        if (cam == null) return;
        if (_follower == null) _follower = GetComponentInParent<VRUIFollower>();
        float dist = _follower != null ? _follower.distance : 1.0f;   // 정면 UI 와 같은 거리 → 크기도 그대로

        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 pos = cam.transform.position + fwd * dist;
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float k = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);   // 선임 정비사 패널과 같은 속도
        var tr = transform;
        if (!_init) { tr.SetPositionAndRotation(pos, rot); _init = true; }
        else tr.SetPositionAndRotation(Vector3.Lerp(tr.position, pos, k), Quaternion.Slerp(tr.rotation, rot, k));
    }
}
