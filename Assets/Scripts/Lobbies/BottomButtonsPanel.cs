using MasterServerToolkit.Bridges;
using MasterServerToolkit.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BottomButtonsPanel : UIView
{
    public UIView CreateRoomView;
    public UIView RoomListView;
    public UIView ContentsListView;

    public void ViewContentsList()
    {
        ContentsListView.Show();
        RoomListView.Hide();
        CreateRoomView.Hide();
        this.Show();
    }

    public void CreateRoom()
    {
        RoomListView.Hide();
        CreateRoomView.Show();
        ContentsListView.Hide();
        this.Hide();
    }

    public void JoinRoom()
    {
        RoomListView.Show();
        CreateRoomView.Hide();
        ContentsListView.Hide();
    }
}
