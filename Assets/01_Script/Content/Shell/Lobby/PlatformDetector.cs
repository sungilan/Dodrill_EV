using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using XRAirpotrSecurity;

public class PlatformDetector : MonoBehaviour
{
    public Canvas canvas;
    public GameObject noVRCamera;
    public GameObject VRPlayer;

    // Start is called before the first frame update
    void Start()
    {
        UserInfo.PlatformType = Utils.GetPlatformType();
        if (UserInfo.PlatformType == PlatformType.VR)
        {
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Destroy(VRPlayer);
            Instantiate(noVRCamera);
        }
    }
}
