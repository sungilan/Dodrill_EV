using UnityEngine;

// ============================================================
//  StayOnRelease.cs
//
//  VR 에서 손을 놓아도 떨어지지 않고 그 자리에 그대로 서 있는 장비.
//  · 작업등: 자석 받침 — 차체·리프트 기둥 어디에 대고 놓든 그 자세로 붙어 있다
//  · 바닥 끌기 장비(오일 받이·배터리 리프트): 놓아도 기울거나 넘어지지 않고 바닥 위치 그대로
//  SyncGrab.VrReleaseInterceptors 로 기본 놓기(서버 중력 낙하)를 가로채 ReleaseAndPlace 로 현재 자세에 고정한다.
// ============================================================
[RequireComponent(typeof(SyncGrab))]
public class StayOnRelease : MonoBehaviour
{
    [Tooltip("놓을 때 세로축 회전만 남기고 바로 세운다(바닥 장비)")] public bool keepUpright;

    private static bool s_registered;

    private void OnEnable()
    {
        if (s_registered) return;
        s_registered = true;
        SyncGrab.VrReleaseInterceptors.Add(Intercept);
    }

    private static bool Intercept(SyncGrab sg, Autohand.Hand hand)
    {
        if (sg == null) return false;
        var me = sg.GetComponent<StayOnRelease>();
        if (me == null || !me.isActiveAndEnabled) return false;

        var t = sg.transform;
        Quaternion rot = t.rotation;
        if (me.keepUpright)
        {
            Vector3 f = t.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(f, Vector3.up);
        }
        var rb = sg.GetComponent<Rigidbody>();
        if (rb != null) { if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } rb.isKinematic = true; }
        t.rotation = rot;
        sg.ReleaseAndPlace(t.position, rot);
        return true;
    }
}
