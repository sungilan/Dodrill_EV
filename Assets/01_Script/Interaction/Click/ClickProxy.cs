using System;
using UnityEngine;

// (11/20) VehicleSync.cs 에서 분리 — 안전 모듈(사고 연출)도 쓰는 범용 클릭 대상이라 인터랙션 층으로 내림
/// <summary>눌렀을 때 동작을 델리게이트로 지정하는 간단한 클릭 대상(경적·냉각수 뚜껑 등).</summary>
public class ClickProxy : MonoBehaviour, IWorldClickable
{
    public string displayName = "";
    public Action onClick;
    public Func<bool, string> hover;
    public bool vrPokeable = true;

    public string DisplayName => displayName;
    public bool VrPokeable => vrPokeable;
    public string HoverMessage(bool touch) => hover != null ? hover(touch) : (touch ? "터치" : "클릭");
    public void OnPCClick() { onClick?.Invoke(); }
}
