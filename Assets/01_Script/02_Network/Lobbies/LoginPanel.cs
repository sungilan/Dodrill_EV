using MasterServerToolkit.Bridges;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using TMPro;
using UnityEngine.Events;

public class LoginPanel : UIView
{
    public TMP_InputField idData;
    public TMP_InputField passwordData;
    public string requestUrl = "http://150.183.179.73:8081";
    public TextMeshProUGUI warningText;
    public UIView next1, next2;
    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showSignInView, OnShowSignInEventHandler);
        Mst.Events.AddListener(MstEventKeys.hideSignInView, OnHideSignInEventHandler);
        Mst.Events.AddListener(MstEventKeys.hideSignInView, ShowNext);
    }

    private void OnShowSignInEventHandler(EventMessage message)
    {
        Show();
    }

    private void OnHideSignInEventHandler(EventMessage message)
    {
        Hide();
    }

    public void SignInGuest()
    {
        string randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
        UserInfo.UserName = "Guest_" + randomSuffix;
        //UserInfo.UserEmail = "buildTest3@gmail.com ";
        UserInfo.isGuest = true;
        AuthBehaviour.Instance.SignInAsGuest(() =>
        {
            warningText.gameObject.SetActive(true);
            warningText.text = "서버와의 연결을 확인하세요.";
        });
    }

    private void ShowNext(EventMessage message)
    {
        next1.Show();
        next2.Show();
    }

    public void SignIn()
    {
    }

    public void SignOut()
    {
        AuthBehaviour.Instance.SignOut();
    }

    public void Quit()
    {
        Mst.Runtime.Quit();
    }

    public void Exit()
    {
        Application.Quit();
    }
    public void OnQuitButtonClick()
    {
        Mst.Events.Invoke(MstEventKeys.showExitPopup);
    }
    protected override void OnShow()
    {
        base.OnShow();
        warningText.gameObject.SetActive(false);
    }

}