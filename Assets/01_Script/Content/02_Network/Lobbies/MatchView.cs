using MasterServerToolkit.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MatchView : MonoBehaviour
{
    public Image thumbnail;
    public TextMeshProUGUI matchName;
    // 프리팹(LobyElement)에는 이 칸이 'matchHostName' 으로 저장돼 있어 필드 이름이 바뀐 뒤 연결이 끊겨 '호스트 이름' 문구가 그대로 보였다.
    // 이 칸에 '호스트 이름 · 콘텐츠 이름' 한 줄을 표시한다.
    [FormerlySerializedAs("matchHostName")]
    public TextMeshProUGUI matchContentsName;
    public TextMeshProUGUI matchPlayers;
    public Button matchJoinButton;
    public TextMeshProUGUI isPlayingText;
}