using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  MeasureAnnounceNet.cs  (11/27)
//
//  (내부 시연 피드백) 전압·절연저항을 재도 잰 사람 화면에만 '기록됨' 알림이 떴다 → 다른 참가자도 값을 같이 볼 수 있게 공지한다.
//   · 잰 사람의 MeasurementLog 가 값을 기록하면 이름표·값·합격 여부를 서버로 보낸다(Report).
//   · 서버는 보낸 사람을 뺀 전원에게 다시 보낸다(보낸 사람은 이미 자기 알림을 봤다).
//   · 받은 쪽은 화면 위쪽 알림(WarningPopup)으로 'OOO님 측정 · 이름 — 값 합격/불합격' 을 띄운다.
//  판정·시나리오 진행은 건드리지 않는다(보이는 알림만).
// ============================================================
public struct MeasureAnnounceBroadcast : IBroadcast
{
    public string who;
    public string label;
    public string valueText;
    public bool pass;
}

public static class MeasureAnnounceNet
{
    private static FishNet.Managing.Server.ServerManager s_sm;
    private static FishNet.Managing.Client.ClientManager s_cm;
    private static string s_lastKey;
    private static float s_lastAt;

    private static void EnsureRegistered()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (sm != null && sm != s_sm)
        {
            if (s_sm != null) s_sm.UnregisterBroadcast<MeasureAnnounceBroadcast>(OnServer);
            sm.RegisterBroadcast<MeasureAnnounceBroadcast>(OnServer);
            s_sm = sm;
        }
        if (cm != null && cm != s_cm)
        {
            if (s_cm != null) s_cm.UnregisterBroadcast<MeasureAnnounceBroadcast>(OnClient);
            cm.RegisterBroadcast<MeasureAnnounceBroadcast>(OnClient);
            s_cm = cm;
        }
    }

    /// <summary>서버·클라이언트 수신 등록 — MeasurementLog 가 생길 때와 서버가 뜰 때 부른다.</summary>
    public static void Init() => EnsureRegistered();

    /// <summary>내가 측정값을 기록했을 때 부른다. 같은 값을 1.5초 안에 되풀이하면 한 번만 보낸다.</summary>
    public static void Report(string label, string valueText, bool pass)
    {
        if (!InstanceFinder.IsClientStarted) return;
        string key = label + "|" + valueText;
        if (key == s_lastKey && Time.unscaledTime - s_lastAt < 1.5f) return;
        s_lastKey = key; s_lastAt = Time.unscaledTime;
        EnsureRegistered();
        InstanceFinder.ClientManager.Broadcast(new MeasureAnnounceBroadcast
        {
            who = string.IsNullOrEmpty(UserInfo.UserName) ? "참가자" : UserInfo.UserName,
            label = label, valueText = valueText, pass = pass,
        });
    }

    private static void OnServer(NetworkConnection conn, MeasureAnnounceBroadcast msg, Channel channel)
    {
        InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true, Channel.Reliable);
    }

    private static void OnClient(MeasureAnnounceBroadcast msg, Channel channel)
    {
        WarningPopup.Show("measure_other_" + msg.who, $"{msg.who}님 측정 · {msg.label}",
            $"{msg.valueText}  {(msg.pass ? "합격" : "불합격")}",
            msg.pass ? new Color(0.08f, 0.35f, 0.18f, 0.92f) : new Color(0.45f, 0.1f, 0.08f, 0.92f), 3.5f, 2);
    }
}
