using MasterServerToolkit.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerInfos : MonoBehaviour
{
    public UILable playerName;
    public UILable playerType;
    [SerializeField] private Image readyImage;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Sprite unReadySprite;



    public void SetReadyStatus(bool ready)
    {
        if (ready == true)
            readyImage.sprite = readySprite;
        else
            readyImage.sprite = unReadySprite;
    }
}
