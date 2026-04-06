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

    public void InteractWithTool(float deltaProgress)
    {
        if(isBroken || isloosened) return;

        // ★ prevProgress 정의 추가
        float prevProgress = _progress;
        _progress += deltaProgress / timeToComplete;
        _progress = Mathf.Clamp01(_progress);

        // UI 업데이트
        if(InteractionProgressBarUI.Instance != null)
        {
            InteractionProgressBarUI.Instance.ShowProgress(_progress, "볼트 해제 중...", gameObject.name);
        }

        // 프로그레스가 1이 되는 순간 딱 한 번 이벤트 발생
        if(prevProgress < 1.0f && _progress >= 1.0f)
        {
            OnBoltLoosened?.Invoke(this);
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