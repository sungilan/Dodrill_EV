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
    /// <summary>이 클라이언트에서 도구로 직접 돌려 진행도가 바뀔 때마다(매 프레임) 발생 — BoltGroupCounter 가 다른 플레이어에게 중계한다. 원격 적용(ApplyRemoteProgress)에서는 발생하지 않는다.</summary>
    public event Action<Bolt> OnProgressChanged;
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
        if (_progress != prevProgress) OnProgressChanged?.Invoke(this);

        if (prevProgress < 1.0f && _progress >= 1.0f)
        {
            if (isAssembleMode) { MarkDone(true); WorldSfx.TorqueClick(transform.position); }   // 체결 완료: 초록 아웃라인 + 토크렌치 '딸깍'
            OnBoltLoosened?.Invoke(this);
        }
    }

    /// <summary>
    /// 다른 플레이어가 돌리고 있는 볼트의 진행도를 받아 같은 모습으로 맞춘다(움직임·체결 완료 시 초록 아웃라인).
    /// 예전에는 진행도·위치·아웃라인이 드릴을 쥔 사람의 클라이언트에서만 계산돼, 다른 플레이어에게는 볼트가 전혀 안 움직이고 초록 표시도 안 보였다.
    /// 되감기는 무시한다(늦게 온 옛 값이 덮어쓰지 않게).
    /// </summary>
    public void ApplyRemoteProgress(float progress)
    {
        if (isBroken) return;
        progress = Mathf.Clamp01(progress);
        if (progress <= _progress) return;

        SaveOrigin();
        float prev = _progress;
        _progress = progress;
        AnimateBoltPosition();

        if (prev < 1.0f && _progress >= 1.0f && isAssembleMode) { MarkDone(true); WorldSfx.TorqueClick(transform.position); }
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
            // 조일 때도 돌아가게 (풀 때와 반대로)
            transform.localRotation = _originLocalRot * Quaternion.Euler(0f, (1f - _progress) * 720f, 0f);
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

    /// <summary>체결 단계 시작 모습(덜 조인 위치)으로 둔다.</summary>
    public void ShowAssembleStart()
    {
        SaveOrigin();
        isAssembleMode = true;
        _progress = 0f;
        AnimateBoltPosition();
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

    // ── 체결 완료 표시 (초록 아웃라인) ─────────────────────────────
    public static Color DoneOutlineColor = new Color(0.2f, 1f, 0.3f, 1f);
    private EPOOutline.Outlinable _doneOutline;
    public bool IsMarkedDone => _doneOutline != null;
    private bool _doneOutlineCreated;

    /// <summary>
    /// 체결이 끝난 볼트를 초록 아웃라인으로 표시한다. 가이드가 볼트 그룹 전체에 건 깜빡이는 아웃라인에서는 이 볼트를 뺀다.
    /// </summary>
    public void MarkDone(bool on)
    {
        var myRenderers = GetComponentsInChildren<Renderer>(true);
        if (on)
        {
            // 부모(볼트 그룹)에 걸린 가이드 아웃라인에서 이 볼트 렌더러 제외
            foreach (var parentOutline in GetComponentsInParent<EPOOutline.Outlinable>(true))
            {
                if (parentOutline.gameObject == gameObject) continue;
                var targets = parentOutline.OutlineTargets;
                for (int i = targets.Count - 1; i >= 0; i--)
                    if (System.Array.IndexOf(myRenderers, targets[i].Renderer) >= 0) parentOutline.RemoveTarget(targets[i]);
            }
            if (_doneOutline == null)
            {
                // 볼트에 이미 붙은 Outlinable(임팩트 렌치가 노란 표시용으로 붙인 것 등)이 있으면 그걸 쓴다.
                // 따로 하나 더 붙이면 렌치가 손을 뗄 때 기존 것을 흰색으로 되돌려 초록 위에 덮어 버린다.
                _doneOutline = GetComponent<EPOOutline.Outlinable>();
                _doneOutlineCreated = _doneOutline == null;
                if (_doneOutlineCreated) _doneOutline = gameObject.AddComponent<EPOOutline.Outlinable>();
                if (_doneOutline.OutlineTargets.Count == 0)
                    foreach (var r in myRenderers)
                        if (r.GetComponent<MeshFilter>() != null || r is SkinnedMeshRenderer) _doneOutline.AddRenderer(r);
            }
            _doneOutline.RenderStyle = EPOOutline.RenderStyle.Single;
            _doneOutline.OutlineParameters.Color = DoneOutlineColor;
            _doneOutline.enabled = true;
        }
        else if (_doneOutline != null)
        {
            if (_doneOutlineCreated) Destroy(_doneOutline);
            else _doneOutline.enabled = false;
            _doneOutline = null;
        }
    }

    public void ReactivateForAssemble()
    {
        MarkDone(false);
        // 체결 애니메이션(AnimateBoltPosition)의 시작 위치와 같은 쪽으로 빼 둔다.
        // 예전에는 반대쪽(+Y)에 두어, 체결을 시작하는 순간 볼트가 위로 툭 올라갔다가 내려가 보였다.
        transform.localPosition = _originLocalPos + (Vector3.up * -ejectLocalY);
        transform.localRotation = _originLocalRot;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

        _progress = 0f;
        gameObject.SetActive(true);
    }
}