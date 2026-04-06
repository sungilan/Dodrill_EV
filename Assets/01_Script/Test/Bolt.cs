using UnityEngine;
using System;

public class Bolt : MonoBehaviour
{
    [Header("풀리는 시간 (초)")]
    public float timeToComplete = 2.0f;

    [Header("토크 설정")]
    public float requiredTorque = 52f;
    public float currentTorque = 0f;
    public bool isTightened = false;
    public bool isBroken = false;

    [Header("파손 VFX")]
    public Mesh brokenBoltMesh;
    public GameObject breakEffect;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private float _progress = 0f;

    // 이벤트: Counter가 구독함
    public event Action<Bolt> OnBoltLoosened;

    public float Progress => _progress;
    public bool isloosened => _progress >= 1.0f;

    public bool isAssembleMode = false; // 조립 단계인가?

    public void InteractWithTool(float deltaProgress)
    {
        if(isBroken) return;

        float prevProgress = _progress;

        if(isAssembleMode)
        {
            // 조립: 0(풀림) -> 1(체결)
            _progress += deltaProgress;
        }
        else
        {
            // 분해: 0(체결) -> 1(탈거)
            _progress += deltaProgress;
        }

        _progress = Mathf.Clamp01(_progress);

        // UI 표시 (조립/분해 문구만 변경)
        string msg = isAssembleMode ? "볼트 체결 중..." : "볼트 해제 중...";
        InteractionProgressBarUI.Instance?.ShowProgress(_progress, msg, gameObject.name);

        if(prevProgress < 1.0f && _progress >= 1.0f)
        {
            OnBoltLoosened?.Invoke(this); // 이벤트명은 유지하되 완료 의미로 사용
        }
    }

    public void ApplyFinalTorque(float torqueAmount)
    {
        if(isBroken || _progress > 0.05f) return;
        currentTorque = torqueAmount;
        isTightened = Mathf.Abs(currentTorque - requiredTorque) <= 2f;
    }

    public void BreakBoltByOverTorque()
    {
        if(isBroken) return;
        isBroken = true;
        isTightened = false;
        _progress = 0f;
        if(breakEffect != null) breakEffect.SetActive(true);
        if(brokenBoltMesh != null) GetComponent<MeshFilter>().mesh = brokenBoltMesh;
        Debug.LogError($"[Bolt] {gameObject.name} 과토크 파손!");
    }

    public void ResetBolt()
    {
        _progress = 0f;
        currentTorque = 0f;
        isTightened = false;
        isBroken = false;
        if(breakEffect != null) breakEffect.SetActive(false);
    }
}