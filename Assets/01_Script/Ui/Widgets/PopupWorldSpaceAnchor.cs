using UnityEngine;
using System.Collections;

namespace DoDrill
{
    public class PopupWorldSpaceAnchor : MonoBehaviour
    {
        [Header("VR 배치 설정")]
        [SerializeField] private float _zDistance = 2.5f;
        [SerializeField] private float _verticalOffset = 0f;

        private Canvas _canvas;
        private RectTransform _rect;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _rect = GetComponent<RectTransform>();

            if (IsVRDevice() && _canvas != null)
                _canvas.renderMode = RenderMode.WorldSpace;
        }

        private void OnEnable()
        {
            if (!IsVRDevice()) return;
            StopAllCoroutines();
            StartCoroutine(DelayedPositioning());
        }

        private IEnumerator DelayedPositioning()
        {
            yield return new WaitForEndOfFrame();

            var cam = Camera.main;
            if (cam == null) yield break;

            if (_canvas != null && _canvas.worldCamera == null)
                _canvas.worldCamera = cam;

            Vector3 targetPos = cam.transform.position + (cam.transform.forward * _zDistance);
            targetPos.y += _verticalOffset;
            transform.position = targetPos;

            transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward, cam.transform.rotation * Vector3.up);

            if (_rect != null) _rect.anchoredPosition3D = Vector3.zero;

            Debug.Log($"[PopupAnchor] {gameObject.name} → 카메라 앞 {_zDistance}m");
        }

        private static bool IsVRDevice()
        {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
            return UnityEngine.XR.XRSettings.isDeviceActive;
#endif
            return false;
        }
    }
}
