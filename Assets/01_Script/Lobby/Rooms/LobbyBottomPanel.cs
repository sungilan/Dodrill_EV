using MasterServerToolkit.Bridges;
using MasterServerToolkit.UI;
using UnityEngine;

public class LobbyBottomPanel : UIView
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
