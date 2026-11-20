using System;
using UnityEngine;

// ============================================================
//  AvatarHooks.cs — 아바타·플레이어 모듈의 "확장 지점" (모듈화 2단계, 2026-11-20)
//
//  아바타(PcMobileAvatarController·AvatarBodyFit 등)는 EV 장비(문·사다리·임팩트 렌치·알림 토스트)를 직접 알지 못한다.
//  콘텐츠(EV)가 시작할 때(Test/EvAvatarBinding.cs) 여기에 자기 기능을 등록한다.
//  등록이 없으면 아래 기본 동작(아무것도 안 함 / 높이 보정 0)으로 안전하게 동작한다.
// ============================================================

/// <summary>밟고 올라설 수 있는 면(사다리 발판 등)의 표시용 인터페이스. 이 컴포넌트가 달린 면은 접지가 바닥으로 보지 않는다.</summary>
public interface IClimbSurface { }

public static class AvatarHooks
{
    // ── 씬 고정 오브젝트(문·해치 등)를 내가 직접 눌렀을 때: 누른 지점으로 손을 뻗는다 ──
    public static event Action<Vector3> WorldClicked;

    public static void RaiseWorldClicked(Vector3 worldPoint)
    {
        try { WorldClicked?.Invoke(worldPoint); } catch (Exception e) { Debug.LogException(e); }
    }

    // ── 작업 공구(드릴·렌치)가 지금 작동 중인가 — 작동 중에는 반대 손 뻗기를 생략 ──
    public static Func<bool> ToolBusy;
    public static bool IsToolBusy => ToolBusy != null && ToolBusy();

    // ── 이 자리에서 접지 높이에 더할 값(사다리 발판 높이 등) ──
    public static Func<Vector3, float> GroundBonus;
    public static float GroundBonusAt(Vector3 worldPos) => GroundBonus != null ? GroundBonus(worldPos) : 0f;

    // ── PC·모바일 높이 조절 키(Q/E·서기/앉기 버튼)를 눌렀을 때 콘텐츠가 대신 처리할 한 칸 이동(사다리 등) ──
    //    (root, +1 오르기 / -1 내려오기) → 처리했으면 true
    public static Func<Transform, int, bool> ClimbStep;

    // ── 화면 알림(키 맞추기 안내 등): (key, title, message, warn, showSeconds, cooldownSeconds) ──
    public static Action<string, string, string, bool, float, float> NoticeHandler;

    public static void Notice(string key, string title, string message, bool warn, float show = 2.5f, float cooldown = 4f)
    {
        if (NoticeHandler != null) NoticeHandler(key, title, message, warn, show, cooldown);
        else Debug.Log($"[Avatar] {title}: {message}");
    }
}
