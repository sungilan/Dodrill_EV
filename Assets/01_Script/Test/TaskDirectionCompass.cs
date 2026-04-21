using DG.Tweening;
using FishNet.Object;
using UnityEngine;

public class TaskDirectionCompass : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _playerRoot;
    [SerializeField] private Transform _head;
    [SerializeField] private GameObject _arrowPrefab;

    [Header("Display")]
    [SerializeField] private float _showDistance = 2.5f;
    [SerializeField] private float _hideDistance = 1.6f;
    [SerializeField] private Vector3 _anchorOffset = new Vector3(0f, -0.2f, 0.65f);
    [SerializeField] private bool _followHeadYaw = true;
    [SerializeField] private Vector3 _arrowRotationOffsetEuler = new Vector3(-90f, 180f, 180f);

    [SerializeField] private bool _blinkArrow = true;
    [SerializeField] private float _blinkDuration = 0.6f;

    private Tween _blinkTween;
    private Renderer[] _arrowRenderers;

    private GameObject _arrowInstance;
    private Transform _currentTarget;
    private bool _isVisible;

    private NetworkObject _ownerNetworkObject;

    private void Update()
    {
        TryResolveRuntimeReferences();

        if(!ShouldRenderForLocalPlayer())
        {
            SetArrowVisible(false);
            return;
        }

        var guide = GuideSystem.Instance;

        // 🔥 GuideSystem 기준으로만 판단
        if(guide == null || !guide.HasGuideTarget())
        {
            SetArrowVisible(false);
            return;
        }

        _currentTarget = guide.GetCurrentGuideTransform();

        if(_currentTarget == null)
        {
            SetArrowVisible(false);
            return;
        }

        UpdateArrow();
    }

    // ==============================
    // 🔥 핵심 로직 (방향 계산만)
    // ==============================

    private void UpdateArrow()
    {
        Vector3 origin = GetOriginPosition();
        Vector3 toTarget = _currentTarget.position - origin;

        float distance = new Vector2(toTarget.x, toTarget.z).magnitude;

        bool shouldShow = _isVisible
            ? distance >= _hideDistance
            : distance >= _showDistance;

        if(!shouldShow)
        {
            SetArrowVisible(false);
            return;
        }

        EnsureArrowInstance();

        if(_arrowInstance == null)
            return;

        SetArrowVisible(true);

        // 위치
        ResolveAnchorBasis(out Vector3 right, out Vector3 up, out Vector3 forward);

        Vector3 worldPos = origin
            + right * _anchorOffset.x
            + up * _anchorOffset.y
            + forward * _anchorOffset.z;

        _arrowInstance.transform.position = worldPos;

        // 방향 (Y축 기준)
        Vector3 dir = new Vector3(toTarget.x, 0f, toTarget.z);

        if(dir.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _arrowInstance.transform.rotation = look * Quaternion.Euler(_arrowRotationOffsetEuler);
        }
    }

    // ==============================
    // 🔧 유틸
    // ==============================

    private void TryResolveRuntimeReferences()
    {
        if(_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if(_playerRoot == null)
            _playerRoot = transform.root;

        if(_head == null && Camera.main != null)
            _head = Camera.main.transform;
    }

    private bool ShouldRenderForLocalPlayer()
    {
        if(_ownerNetworkObject == null)
            return true;

        if(!_ownerNetworkObject.IsSpawned || !_ownerNetworkObject.Owner.IsValid)
            return false;

        return _ownerNetworkObject.Owner.IsLocalClient;
    }

    private Vector3 GetOriginPosition()
    {
        if(_head != null) return _head.position;
        if(_playerRoot != null) return _playerRoot.position;
        return transform.position;
    }

    private void ResolveAnchorBasis(out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        if(_followHeadYaw && _head != null)
        {
            forward = _head.forward;
            forward.y = 0f;
            forward.Normalize();

            right = Vector3.Cross(Vector3.up, forward).normalized;
            up = Vector3.up;
            return;
        }

        forward = _playerRoot.forward.normalized;
        right = _playerRoot.right.normalized;
        up = Vector3.up;
    }

    private void EnsureArrowInstance()
    {
        if(_arrowInstance != null) return;

        _arrowInstance = Instantiate(_arrowPrefab);
        _arrowInstance.name = $"{_arrowPrefab.name}_Compass";

        // ⭐ 이거 빠져있음 (핵심 원인)
        _arrowRenderers = _arrowInstance.GetComponentsInChildren<Renderer>(true);

        _arrowInstance.SetActive(false);

        GuideSystem.Instance?.SetOutline(_arrowInstance, true);
    }

    private void SetArrowVisible(bool visible)
    {
        if(_isVisible == visible) return; // ⭐ 중요

        _isVisible = visible;

        if(_arrowInstance != null)
            _arrowInstance.SetActive(visible);

        if(visible)
            StartBlink();
        else
            StopBlink();
    }

    private void StartBlink()
    {
        if(!_blinkArrow || _arrowRenderers == null) return;

        StopBlink();

        _blinkTween = DG.Tweening.DOTween.To(
            () => 1f,
            x => SetArrowAlpha(x),
            0.2f,
            _blinkDuration
        )
        .SetLoops(-1, DG.Tweening.LoopType.Yoyo)
        .SetEase(DG.Tweening.Ease.InOutSine);
    }

    private void StopBlink()
    {
        if(_blinkTween != null)
        {
            _blinkTween.Kill();
            _blinkTween = null;
        }

        SetArrowAlpha(1f);
    }

    private void SetArrowAlpha(float alpha)
    {
        if(_arrowRenderers == null) return;

        foreach(var r in _arrowRenderers)
        {
            foreach(var mat in r.materials)
            {
                if(mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    mat.color = c;
                }
            }
        }
    }
}