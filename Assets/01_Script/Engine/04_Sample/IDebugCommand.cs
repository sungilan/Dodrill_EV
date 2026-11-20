// ============================================================
//  IDebugCommand.cs
//  모든 디버그 커맨드의 공통 인터페이스
//
//  새 콘텐츠 추가 시 이 인터페이스만 구현하면 됨
//  DebugCommandRegistry 에 수동 등록 후 자동으로 UI에 표시됨
// ============================================================

    public interface IDebugCommand
    {
        /// <summary>UI에 표시될 카테고리 (예: "Scenario", "Network")</summary>
        string Category { get; }

        /// <summary>UI에 표시될 커맨드 이름</summary>
        string Name { get; }

        /// <summary>커맨드 설명 (툴팁 또는 UI 서브텍스트)</summary>
        string Description { get; }

        /// <summary>
        /// 커맨드 실행
        /// </summary>
        /// <param name="args">텍스트 필드로 입력받은 추가 인자 (없으면 빈 배열)</param>
        /// <param name="callerId">호출한 클라이언트 ID (DebugLogger 기록용)</param>
        void Execute(string[] args, int callerId);
    }