// ============================================================
//  VrDevice.cs — XR 장치(헤드셋)가 켜져 있는지 (모듈화 2단계, 2026-11-20)
//  예전에는 Test/VrProbeHandling.IsVRDevice 에 있었다. 아래층(집기·아바타)도 쓰므로 공통으로 뺐다.
// ============================================================
public static class VrDevice
{
    public static bool IsActive
    {
        get
        {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            var d = UnityEngine.XR.XRSettings.loadedDeviceName;
            if (!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
            return false;
        }
    }
}
