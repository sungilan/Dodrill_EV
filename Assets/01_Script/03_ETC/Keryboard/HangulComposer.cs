using System;
using System.Linq;

public class HangulComposer
{
    private static readonly char[] Chosung =
        { 'ぁ','あ','い','ぇ','え','ぉ','け','げ','こ','さ','ざ','し','じ','す','ず','せ','ぜ','そ','ぞ' };
    private static readonly char[] Jungsung =
        { 'た','だ','ち','ぢ','っ','つ','づ','て','で','と','ど','な','に','ぬ','ね','の','は','ば','ぱ','ひ','び' };
    private static readonly char[] Jongsung =
        { '\0','ぁ','あ','ぃ','い','ぅ','う','ぇ','ぉ','お','か','が','き','ぎ','く','ぐ','け','げ','ご','さ','ざ','し','じ','ず','せ','ぜ','そ','ぞ' };

    private char? cho = null;
    private char? jung = null;
    private char? jong = null;

    // 歯 越切 脊径
    public void Input(char c, string lastChar = "")
    {
        if (!CanCombineWithLast(c, lastChar))
        {
            Reset(); // 歯 越切 獣拙
        }

        if (Chosung.Contains(c))
        {
            if (!cho.HasValue) cho = c;
            else if (cho.HasValue && jung.HasValue && !jong.HasValue) jong = c;
            else cho = c;
        }
        else if (Jungsung.Contains(c))
        {
            if (!jung.HasValue) jung = c;
            else { Reset(); jung = c; } // 歯 越切
        }
        else
        {
            Reset();
        }
    }

    private bool CanCombineWithLast(char c, string lastChar)
    {
        if (string.IsNullOrEmpty(lastChar)) return false;
        char last = lastChar[0];
        return (last >= 0xAC00 && last <= 0xD7A3);
    }

    public string GetComposed()
    {
        if (cho.HasValue && jung.HasValue)
        {
            int choIdx = Array.IndexOf(Chosung, cho.Value);
            int jungIdx = Array.IndexOf(Jungsung, jung.Value);
            int jongIdx = jong.HasValue ? Array.IndexOf(Jongsung, jong.Value) : 0;
            int unicode = 0xAC00 + (choIdx * 21 * 28) + (jungIdx * 28) + jongIdx;
            return char.ConvertFromUtf32(unicode);
        }
        else if (cho.HasValue) return cho.Value.ToString();
        else if (jung.HasValue) return jung.Value.ToString();
        return "";
    }

    public void Reset() { cho = null; jung = null; jong = null; }

    public void Backspace() { Reset(); }
}
