using System;
using System.Linq;

public class HangulComposer
{
    private static readonly char[] Chosung =
        { 'ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };
    private static readonly char[] Jungsung =
        { 'ㅏ','ㅐ','ㅑ','ㅒ','ㅓ','ㅔ','ㅕ','ㅖ','ㅗ','ㅘ','ㅙ','ㅚ','ㅛ','ㅜ','ㅝ','ㅞ','ㅟ','ㅠ','ㅡ','ㅢ','ㅣ' };
    private static readonly char[] Jongsung =
        { '\0','ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

    private char? cho = null;
    private char? jung = null;
    private char? jong = null;

    // 새 글자 입력
    public void Input(char c, string lastChar = "")
    {
        if (!CanCombineWithLast(c, lastChar))
        {
            Reset(); // 새 글자 시작
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
            else { Reset(); jung = c; } // 새 글자
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
