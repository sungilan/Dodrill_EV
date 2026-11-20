using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;
using XRAirpotrSecurity;

public static class UserInfo
{
    public static string UserName;
    public static string UserEmail;
    public static bool isGuest = false;
    public static PlatformType PlatformType;
    public static bool isDisconnected = false;
    public static bool isHost = false;
    public static string anchorGUID;
    public static bool useAnchor = false;
    public static bool anchorSetted = false;
    public static bool hintMode = true;
    public static string title = "";
    public static bool isLoggedin;
}