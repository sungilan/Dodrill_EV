using MasterServerToolkit.MasterServer;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientState : MonoBehaviour
{
    #region INSPECTOR
    [Header("Settings"), SerializeField]
    private bool changeStatusColor = true;

    [Header("Status Colors"), SerializeField]
    private Color unauthorizedStatusColor = Color.red;
    [SerializeField]
    private Color authorizedStatusColor = Color.green;
    [SerializeField]
    private Color guestStatusColor = Color.yellow;

    [Header("Components"), SerializeField]
    private Image statusImage;
    [SerializeField]
    private TextMeshProUGUI statusText;
    #endregion

    protected virtual void Update()
    {
        if (Mst.Client.Auth.IsSignedIn)
        {
            if (Mst.Client.Auth.AccountInfo != null)
            {
                if (!Mst.Client.Auth.AccountInfo.IsGuest)
                {
                    RepaintStatus(UserInfo.UserName, authorizedStatusColor);
                }
                else
                {
                    RepaintStatus(UserInfo.UserName, guestStatusColor);
                }
            }
            else
            {
                RepaintStatus($"{Mst.Localization["authStatusUnauthorized"]}", unauthorizedStatusColor);
            }
        }
        else
        {
            RepaintStatus($"{Mst.Localization["authStatusUnauthorized"]}", unauthorizedStatusColor); ;
        }
    }

    private void RepaintStatus(string statusMsg, Color statusColor)
    {
        if (changeStatusColor && statusImage != null)
            statusImage.color = statusColor;

        if (statusText != null)
            statusText.text = statusMsg;
    }
}
