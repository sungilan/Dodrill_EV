using UnityEngine;

/// <summary>정비 매뉴얼 게시판의 ◀ / ▶ 버튼. PC 클릭·모바일 터치는 FreeLookController 가, VR 은 ManualBoard 가 부른다.</summary>
public class ManualBoardButton : MonoBehaviour
{
    public ManualBoard board;
    public int dir = 1;

    public void OnPCClick() { if (board != null) board.Flip(dir); }
    public string DisplayName => "정비 매뉴얼 게시판";
    public string HoverMessage(string verb) => dir > 0 ? $"{verb}하여 다음 페이지" : $"{verb}하여 이전 페이지";
}
