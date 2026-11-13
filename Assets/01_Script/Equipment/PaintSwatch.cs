using System.Collections.Generic;
using UnityEngine;

/// <summary>도색 색상판의 한 칸. PC 클릭·모바일 터치(장비를 안 든 상태)로 고른다. VR 은 건 노즐을 대면 고른다.</summary>
public class PaintSwatch : MonoBehaviour
{
    public static readonly List<PaintSwatch> All = new List<PaintSwatch>();
    public int index;
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public void OnPCClick()
    {
        PaintGun.LocalPick = index;
        foreach (var g in FindObjectsByType<PaintGun>(FindObjectsSortMode.None)) if (g.IsLocalHeld) g.Pick(index);
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
        EquipFx.Notice("paint_color", $"도색 색상: {PaintGun.PaletteNames[index]}", "도색 스프레이건을 들면 이 색으로 칠합니다", EquipFx.Info, 2f, 0f);
    }
    public string DisplayName => $"도색 색상 — {PaintGun.PaletteNames[index]}";
}
