using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// ============================================================
//  HookRegistry.cs — 확장 지점(훅) 등록 자가 점검 (11/20)
//
//  이 프로젝트는 아래층(엔진·아바타·인터랙션)이 콘텐츠를 모르고, 콘텐츠가 시작할 때 훅(public static 델리게이트 필드)에
//  자기 기능을 등록한다. 등록이 빠지면 오류 없이 기능이 사라진다 — 예) 모듈 공급자 0개 = 시나리오 단계가 모두 '더미'로 대기,
//  이름표·손 뻗기·구역 검증 없음. 그래서 앱이 시작한 직후(첫 씬 로드 뒤) 훅이 채워졌는지 한 번 점검하고 로그로 남긴다.
//
//  사용(콘텐츠 쪽, 예: Content/App/Bindings/EvHookSelfCheck.cs):
//    HookRegistry.Verify("EV", new[] { typeof(ScenarioHooks), typeof(AvatarHooks), typeof(GrabHooks), ... }, criticalChecks);
//  · 점검 대상은 각 훅 클래스의 'public static 델리게이트 필드' — 훅이 새로 생기면 자동으로 점검에 들어간다.
//  · 비어 있는 훅은 경고, criticalChecks(예: 모듈 공급자 수 ≥ 1)가 어긋나면 오류로 남긴다.
//  · 일부러 비워 두는 훅은 [OptionalHook] 을 붙이면 점검에서 빠진다.
// ============================================================

/// <summary>콘텐츠가 비워 두어도 되는 훅(점검에서 제외)</summary>
[AttributeUsage(AttributeTargets.Field)]
public class OptionalHookAttribute : Attribute { }

public static class HookRegistry
{
    public struct Critical
    {
        public string name;
        public Func<bool> ok;
        public Critical(string name, Func<bool> ok) { this.name = name; this.ok = ok; }
    }

    /// <summary>마지막 점검에서 비어 있던 훅 이름("클래스.필드"). 디버그 화면 등에서 읽을 수 있다.</summary>
    public static IReadOnlyList<string> LastMissing => s_missing;
    private static readonly List<string> s_missing = new List<string>();

    /// <summary>훅 클래스들의 델리게이트 필드를 훑어 비어 있는 것을 로그로 알린다. 문제가 없으면 true.</summary>
    public static bool Verify(string owner, IEnumerable<Type> hookTypes, IEnumerable<Critical> critical = null)
    {
        s_missing.Clear();
        int total = 0;
        foreach (var t in hookTypes)
        {
            if (t == null) continue;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                if (f.GetCustomAttribute<OptionalHookAttribute>() != null) continue;
                total++;
                if (f.GetValue(null) == null) s_missing.Add(t.Name + "." + f.Name);
            }
        }

        var failed = new List<string>();
        if (critical != null)
            foreach (var c in critical)
            {
                bool ok;
                try { ok = c.ok == null || c.ok(); } catch (Exception e) { ok = false; Debug.LogException(e); }
                if (!ok) failed.Add(c.name);
            }

        if (failed.Count > 0)
            Debug.LogError($"[HookCheck:{owner}] 필수 등록 누락 {failed.Count}개 — {string.Join(", ", failed)}. 이 상태로는 콘텐츠가 동작하지 않습니다(바인딩 파일이 실행되지 않았거나 어셈블리가 빠졌는지 확인).");
        if (s_missing.Count > 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s_missing.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(s_missing[i]); }
            Debug.LogWarning($"[HookCheck:{owner}] 비어 있는 훅 {s_missing.Count}/{total}개 — {sb} (일부러 비웠다면 [OptionalHook] 을 붙이세요)");
        }
        if (failed.Count == 0 && s_missing.Count == 0)
            Debug.Log($"[HookCheck:{owner}] 훅 {total}개 모두 등록됨");
        return failed.Count == 0 && s_missing.Count == 0;
    }
}
