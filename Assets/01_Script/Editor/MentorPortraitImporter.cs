using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Resources/Dialogue/Mentor*.png (선임 정비사 초상·표정 그림)을 넣으면 자동으로 Sprite(2D)·밉맵 끔·최대 1024 로 가져온다.
/// 표정 그림 이름: Mentor_praise / Mentor_serious / Mentor_warn / Mentor_alarm / Mentor_grave / Mentor_sigh (기본: MentorPortrait)
/// </summary>
public class MentorPortraitImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.Contains("/Resources/Dialogue/Mentor")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.maxTextureSize = 1024;   // 화면에는 124 px 안팎으로 나온다
    }
}
