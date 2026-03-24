using UnityEngine;

public class Bolt : MonoBehaviour
{
    [Header("Animation Settings")]
    public float progress = 0f; // 0: 조임, 1: 풀림
    public float timeToComplete = 2.0f;
    public bool isloosened => progress >= 1.0f;

    [Header("Torque Settings")]
    public float currentTorque = 0f;   // 현재 조여진 힘 (Nm)
    public float requiredTorque = 52f; // 목표 규정 토크
    public bool isTightened = false;   // 최종 체결 완료 여부
    public bool isBroken = false;      // 과토크로 인한 파손 여부

    [Header("VFX & Mesh")]
    public Mesh brokenBoltMesh;        // 부러진 볼트 메쉬 (선택)
    public GameObject breakEffect;     // 파손 시 스파크/연기 (선택)

    public void InteractWithTool(float deltaProgress)
    {
        if(isBroken) return; // 부러진 볼트는 조작 불가

        // 볼트가 완전히 박힌 상태(progress 0)에서 조이려고 하면 무시
        if(progress <= 0.001f && deltaProgress < 0) return;

        progress += deltaProgress / timeToComplete;
        progress = Mathf.Clamp01(progress);

        UpdateBoltTransform();
    }

    public void ApplyFinalTorque(float torqueAmount)
    {
        if(isBroken || progress > 0.05f) return;

        currentTorque = torqueAmount;

        // 규정 토크 범위 확인 (오차 ±2Nm)
        isTightened = Mathf.Abs(currentTorque - requiredTorque) <= 2f;
    }

    public void BreakBoltByOverTorque()
    {
        if(isBroken) return;

        isBroken = true;
        isTightened = false;
        progress = 0f; // 박힌 채로 고정

        // 시각적 피드백
        if(breakEffect != null) breakEffect.SetActive(true);
        if(brokenBoltMesh != null) GetComponent<MeshFilter>().mesh = brokenBoltMesh;

        // 사운드 및 진동 로직을 여기에 추가 가능
        Debug.LogError($"[평가] {gameObject.name} 과토크로 파손됨! -20점");
        // ScoreManager.Instance.DeductScore(20, "부품 파손");
    }

    private void UpdateBoltTransform()
    {
        // 회전하며 위아래로 움직임 (나사산 연출)
        transform.localRotation = Quaternion.Euler(0, progress * 1000f, 0);
        transform.localPosition = new Vector3(0, progress * 0.02f, 0);
    }

    public void ResetBolt()
    {
        progress = 0f;
        currentTorque = 0f;
        isTightened = false;
        isBroken = false;
        // 메쉬 원복 로직 필요 시 추가
    }
}