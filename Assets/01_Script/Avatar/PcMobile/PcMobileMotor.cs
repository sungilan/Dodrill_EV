using UnityEngine;

// ============================================================
//  PcMobileMotor.cs — PC·모바일 1인칭 이동/시점/카메라 공통부 (모듈화 ①, 2026-11-20)
//
//  FreeLookController(PC)·FreeLockControllerMobile(모바일)에 똑같이 들어 있던
//  수평 이동·벽 충돌·자유 비행 수직 이동·시점 회전(루트 yaw / 피벗 pitch)·내 카메라 켜기를 한곳으로 모았다.
//  입력을 읽는 방식(키보드·마우스 / 조이스틱·터치)과 전기차 상호작용(클릭·잡기·리프트 키)은 각 컨트롤러에 남는다.
//  인스펙터 값(moveSpeed·collisionRadius 등)은 프리팹 값을 지키려고 컨트롤러 필드 그대로 두고 인자로 넘긴다.
//  사다리 한 칸 오르기처럼 콘텐츠가 가진 이동 동작은 AvatarHooks.ClimbStep 으로 받는다.
// ============================================================
public static class PcMobileMotor
{
    /// <summary>입력(x=좌우, y=앞뒤)을 몸이 바라보는 수평면 기준 방향으로 바꾼다(길이는 입력 크기 그대로)</summary>
    public static Vector3 PlanarDirection(Transform root, Vector2 input)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        return root.right * input.x + flatForward * input.y;
    }

    /// <summary>수평 이동량(이번 프레임)을 벽·가구 충돌을 적용해 옮기고, 실제로 움직인 수평 속도(m/s)를 돌려준다(걷기 애니메이션용)</summary>
    public static Vector3 MoveWithCollision(Transform root, Vector3 wantDelta, bool collide, float radius, float height, float avatarScale, float moveSpeed)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        if (!collide)
        {
            root.position += wantDelta;
            return wantDelta / dt;
        }

        Vector3 before = root.position;
        Vector3 after = PlayerCollision.Move(before, wantDelta, radius * avatarScale, height * avatarScale);
        root.position = after;

        Vector3 actual = after - before; actual.y = 0f;
        Vector3 vel = actual / dt;
        float max = Mathf.Max(moveSpeed, 0.01f) * 1.5f;   // 겹침 해소로 튄 값이 걷기 속도를 부풀리지 않게
        return vel.magnitude > max ? vel.normalized * max : vel;
    }

    /// <summary>아바타 없는 구 프리팹용 자유 비행 수직 이동(desired: +위 / -아래 / 0 정지)</summary>
    public static void StepFreeVertical(Transform root, ref float currentVelocity, float desired, float moveSpeed, float smoothTime)
    {
        currentVelocity = Mathf.Lerp(currentVelocity, desired * moveSpeed, Time.deltaTime / Mathf.Max(0.0001f, smoothTime));
        root.position += Vector3.up * (currentVelocity * Time.deltaTime);
    }

    /// <summary>시점 적용. 아바타 리그면 루트는 좌우(yaw)만, 상하(pitch)는 카메라 피벗만 돌려 몸이 기울지 않게 한다</summary>
    public static void ApplyLook(Transform root, Transform pivot, bool useAvatarRig, float pitch, float yaw)
    {
        if (useAvatarRig && pivot != null)
        {
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        else
        {
            root.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    /// <summary>콘텐츠가 가진 '한 칸 오르기/내려오기'(사다리 등). 처리했으면 true — 그러면 높이 조절 입력은 넘긴다</summary>
    public static bool TryClimbStep(Transform root, int dir) => AvatarHooks.ClimbStep != null && AvatarHooks.ClimbStep(root, dir);

    // ── 카메라 ─────────────────────────────────────────

    /// <summary>플레이어 프리팹의 카메라를 모두 끈다(남의 플레이어·초기화 전)</summary>
    public static void DisableChildCameras(Component root)
    {
        foreach (var cam in root.GetComponentsInChildren<Camera>(true))
        {
            // 루트 오브젝트에 카메라가 있는 구버전 프리팹: 오브젝트를 끄면 플레이어 전체(아바타 포함)가 사라진다 → 컴포넌트만 끔
            if (cam.gameObject == root.gameObject) cam.enabled = false;
            else cam.gameObject.SetActive(false);
            cam.tag = "Untagged";
        }
    }

    /// <summary>내 카메라를 켜고 MainCamera 로 지정한다. 이름에 'UI' 가 들어간 보조 카메라도 함께 켠다. 없으면 null</summary>
    public static Camera ActivateOwnCamera(Component root, float nearClip)
    {
        Camera found = root.GetComponentInChildren<Camera>(true);
        if (found == null) return null;

        found.gameObject.SetActive(true);
        found.enabled = true;
        found.tag = "MainCamera";
        // 프리팹 루트 카메라의 near clip(0.3m) 때문에 손에 든 물체·가까운 모델이 잘려 보이는 문제 방지
        if (nearClip > 0f && found.nearClipPlane > nearClip) found.nearClipPlane = nearClip;

        foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            if (cam.gameObject.name.Contains("UI")) cam.gameObject.SetActive(true);
        return found;
    }

    /// <summary>XR 기기가 켜져 있는가</summary>
    public static bool IsVRDevice()
    {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
        if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
        var d = UnityEngine.XR.XRSettings.loadedDeviceName;
        if (!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
        return false;
    }
}
