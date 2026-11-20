using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VrLieView.cs
//
//  VR 에서 크리퍼에 누웠을 때 시야를 위로 젖힌다.
//  · heightOffset 으로 눈 높이만 내리면 고개는 그대로 정면을 봐서 '쪼그려 앉은' 시점이 됐다
//  · AutoHand 의 trackingContainer(TrackerOffsets) 아래에 기울기용 빈 오브젝트를 하나 끼우고
//    머리·컨트롤러를 그 밑으로 옮긴 뒤, 머리 위치를 중심으로 앞쪽을 위로 젖힌다
//    (머리 위치는 그대로라 AutoHand 의 몸-머리 맞춤, 이동에는 영향이 없다)
//  · Avatars(본인 눈에 보이는 몸)는 옮기지 않는다 — 눕는 자세는 VrLyingPose 가 따로 처리
//  · 일어나면 천천히 원래대로 세운 뒤 자식들을 원래 자리로 돌려놓는다
// ============================================================
[DefaultExecutionOrder(10000)]   // AutoHandPlayer.LateUpdate(트래킹 위치 갱신) 뒤에 돈다
public class VrLieView : MonoBehaviour
{
    private static VrLieView s_cur;

    private Transform _container;
    private Transform _tilt;
    private Transform _head;
    private readonly List<Transform> _moved = new List<Transform>();
    private readonly List<int> _movedIndex = new List<int>();
    private float _target;
    private float _angle;
    private float _yaw;
    private const float Speed = 90f;   // 도/초

    /// <summary>누웠을 때 호출. pitch = 위로 젖힐 각도(도)</summary>
    public static void Begin(AutoHandPlayer player, float pitch)
    {
        if (player == null || player.trackingContainer == null || player.headCamera == null) return;
        if (s_cur != null && s_cur._container != player.trackingContainer) s_cur.Restore();
        if (s_cur == null)
        {
            s_cur = player.trackingContainer.gameObject.AddComponent<VrLieView>();
            if (!s_cur.Setup(player)) { Destroy(s_cur); s_cur = null; return; }
        }
        s_cur._target = Mathf.Clamp(pitch, 0f, 85f);
        // 누운 방향 = 누울 때 고개가 향한 방향(트래킹 공간 기준)
        Vector3 f = s_cur._head.localRotation * Vector3.forward; f.y = 0f;
        s_cur._yaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
    }

    /// <summary>일어날 때 호출 — 천천히 세운 뒤 정리한다</summary>
    public static void End()
    {
        if (s_cur != null) s_cur._target = 0f;
    }

    private bool Setup(AutoHandPlayer player)
    {
        _container = player.trackingContainer;
        _head = player.headCamera.transform;
        if (_head.parent != _container) return false;   // 구조가 다르면 건드리지 않는다

        var go = new GameObject("[CreeperLieTilt]");
        _tilt = go.transform;
        _tilt.SetParent(_container, false);
        _tilt.localPosition = Vector3.zero;
        _tilt.localRotation = Quaternion.identity;
        _tilt.localScale = Vector3.one;

        for (int i = _container.childCount - 1; i >= 0; i--)
        {
            var c = _container.GetChild(i);
            if (c == _tilt || c.name == "Avatars") continue;
            _moved.Add(c);
            _movedIndex.Add(i);
        }
        foreach (var c in _moved) c.SetParent(_tilt, false);   // 로컬 자세 그대로(트래킹 값이 로컬이라)
        return true;
    }

    private void LateUpdate()
    {
        if (_tilt == null || _head == null) { Restore(); return; }
        _angle = Mathf.MoveTowards(_angle, _target, Speed * Time.deltaTime);
        Vector3 h = _head.localPosition;   // 기울기 오브젝트 기준 = 트래킹 공간 기준 머리 위치
        Quaternion r = Quaternion.AngleAxis(-_angle, Quaternion.Euler(0f, _yaw, 0f) * Vector3.right);
        _tilt.localRotation = r;
        _tilt.localPosition = h - r * h;   // 머리 위치를 중심으로 회전
        if (_target <= 0f && _angle <= 0.01f) Restore();
    }

    private void Restore()
    {
        if (_container != null)
        {
            for (int k = _moved.Count - 1; k >= 0; k--)
            {
                var c = _moved[k];
                if (c == null) continue;
                c.SetParent(_container, false);
                c.SetSiblingIndex(Mathf.Min(_movedIndex[k], _container.childCount - 1));
            }
        }
        _moved.Clear();
        _movedIndex.Clear();
        if (_tilt != null) Destroy(_tilt.gameObject);
        _tilt = null;
        if (s_cur == this) s_cur = null;
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (s_cur == this) s_cur = null;
    }
}
