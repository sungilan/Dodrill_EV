using UnityEngine;
using System;
using FishNet.Object;

public class Bolt : MonoBehaviour
{
    [Header("풀리는 시간 (초)")]
    public float timeToComplete = 2.0f;

    [Header("토크 설정")]
    public float requiredTorque = 52f;
    public float currentTorque = 0f;
    public bool isTightened = false;
    public bool isBroken = false;

    [Header("이동 연출")]
    public float ejectLocalY = 0.03f;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private float _progress = 0f;

    private Vector3 _originLocalPos;
    private Quaternion _originLocalRot;
    private bool _originSaved;

    public event Action<Bolt> OnBoltLoosened;
    public float Progress => _progress;
    public bool isloosened => _progress >= 1.0f;
    public bool isAssembleMode = false;

    // 도구의 방향을 전달받기 위한 변수 추가
    private Vector3 _currentToolDirection;

    private void Awake() => SaveOrigin();

    public void SaveOrigin()
    {
        if (_originSaved) return;
        _originLocalPos = transform.localPosition;
        _originLocalRot = transform.localRotation;
        _originSaved = true;
    }

    public void InteractWithTool(float deltaProgress, Vector3 toolForward)
    {
        if (isBroken) return;

        _currentToolDirection = toolForward; // 렌치가 바라보는 방향 저장

        float prevProgress = _progress;
        _progress += deltaProgress;
        _progress = Mathf.Clamp01(_progress);

        AnimateBoltPosition();

        if (prevProgress < 1.0f && _progress >= 1.0f)
            OnBoltLoosened?.Invoke(this);
    }

    //private void AnimateBoltPosition()
    //{
    //    if (!_originSaved) return;

    //    // 렌치가 위를 향하고 있다면(차량 하부), 분해 시 아래로(-Y) 이동
    //    // 렌치가 아래를 향하고 있다면(바닥 배터리), 분해 시 위로(+Y) 이동
    //    // 렌치의 Forward와 볼트의 Local Up 사이의 관계를 계산
    //    float dot = Vector3.Dot(_currentToolDirection, transform.up);
    //    float directionMultiplier = dot > 0 ? 1f : -1f;

    //    if (isAssembleMode)
    //    {
    //        // 조립: 렌치 방향 반대쪽에서 원위치로 들어옴
    //        float yOffset = (1f - _progress) * ejectLocalY * directionMultiplier;
    //        transform.localPosition = _originLocalPos + new Vector3(0, yOffset, 0);
    //    }
    //    else
    //    {
    //        // 분해: 원위치에서 렌치 방향(반대 방향)으로 밀려남
    //        float yOffset = _progress * ejectLocalY * directionMultiplier;
    //        transform.localPosition = _originLocalPos + new Vector3(0, yOffset, 0);

    //        // 회전 효과 (나사 풀리는 연출)
    //        float angle = _progress * 360f * 2f;
    //        transform.localRotation = _originLocalRot * Quaternion.Euler(0f, angle, 0f);
    //    }
    //}

    private void AnimateBoltPosition()
    {
        if(!_originSaved) return;

        // 도구 방향(dot)에 의존하지 않고, 볼트의 로컬 Up 방향을 기준으로 고정합니다.
        // 보통 볼트 모델은 Y축이 머리 방향이므로 +1f을 기본값으로 씁니다.
        // 만약 반대로 움직인다면 이 값을 -1f로 수정하세요.
        float directionMultiplier = -1f;

        if(isAssembleMode)
        {
            // 조립: 바깥쪽(ejectLocalY)에서 원위치(0)로 들어옴
            float yOffset = (1f - _progress) * ejectLocalY * directionMultiplier;
            // transform.up 방향(로컬 Y축)으로 이동 처리
            transform.localPosition = _originLocalPos + (Vector3.up * yOffset);
        }
        else
        {
            // 분해: 원위치(0)에서 바깥쪽(ejectLocalY)으로 밀려남
            float yOffset = _progress * ejectLocalY * directionMultiplier;
            transform.localPosition = _originLocalPos + (Vector3.up * yOffset);

            // 회전 효과 (나사 풀리는 연출)
            float angle = _progress * 360f * 2f;
            transform.localRotation = _originLocalRot * Quaternion.Euler(0f, angle, 0f);
        }
    }

    public void PlayFallEffect()
    {
        var rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.AddForce(Vector3.down * 2f, ForceMode.Impulse);
    }

    public void Deactivate()
    {
        Debug.Log($"<color=cyan>[Bolt]</color> {gameObject.name} 비활성화 실행");
        gameObject.SetActive(false);
    }

    public void ReactivateForAssemble()
    {
        transform.localPosition = _originLocalPos + (Vector3.up * ejectLocalY);
        transform.localRotation = _originLocalRot;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

        _progress = 0f;
        gameObject.SetActive(true);
    }
}