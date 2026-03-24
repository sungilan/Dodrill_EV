using UnityEngine;

public enum Polarity { Positive, Negative }

public class MeasurableNode : MonoBehaviour
{
    public Polarity nodePolarity; // 이 지점이 +인지 -인지 설정
    public float voltageValue = 3.8f; // 이 노드가 가진 전압값 (모듈 기준)
}