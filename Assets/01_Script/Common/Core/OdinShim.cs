// ============================================================
//  OdinShim.cs — Odin Inspector 가 없는 프로젝트용 빈 어트리뷰트 (모듈화, 2026-11-20)
//
//  모듈 코드(SyncGrab·ScenarioSpawner·Ui 위젯 등)는 인스펙터 버튼에만 Odin 의 [Button]·[GUIColor]·[ReadOnly] 를 쓴다.
//  Odin 이 설치돼 있으면 ODIN_INSPECTOR 가 정의되므로 이 파일은 컴파일되지 않고 진짜 Odin 을 쓴다.
//  Odin 이 없으면 같은 이름의 아무 일도 하지 않는 어트리뷰트가 있어 코드를 고치지 않고도 컴파일된다(인스펙터 버튼만 사라진다).
//  모듈에서 Odin 어트리뷰트를 더 쓰게 되면 아래에 같은 모양으로 추가한다.
// ============================================================
#if !ODIN_INSPECTOR
using System;

namespace Sirenix.OdinInspector
{
    public enum ButtonSizes { Small = 22, Medium = 31, Large = 41, Gigantic = 62 }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ButtonAttribute : Attribute
    {
        public ButtonAttribute() { }
        public ButtonAttribute(string name) { }
        public ButtonAttribute(ButtonSizes size) { }
        public ButtonAttribute(string name, ButtonSizes size) { }
        public ButtonAttribute(int buttonSize) { }
        public ButtonAttribute(string name, int buttonSize) { }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class GUIColorAttribute : Attribute
    {
        public GUIColorAttribute(float r, float g, float b, float a = 1f) { }
        public GUIColorAttribute(string getColor) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class FoldoutGroupAttribute : Attribute
    {
        public FoldoutGroupAttribute(string groupName) { }
        public FoldoutGroupAttribute(string groupName, bool expanded) { }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class HorizontalGroupAttribute : Attribute
    {
        public HorizontalGroupAttribute(string group) { }
        public HorizontalGroupAttribute(string group, float width) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath, RequireExistingPath, UseBackslashes;
        public string ParentFolder;
    }
}
#endif
