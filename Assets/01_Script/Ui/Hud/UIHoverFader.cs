using UnityEngine;

// ============================================================
//  UIHoverFader.cs — 호버 라벨 페이드 인/아웃 (FreeLookController.cs 에서 분리, 11/20)
//  PC·모바일 컨트롤러·ItemNameTag 가 함께 쓴다. 컨트롤러를 몰라도 되는 작은 부품이라 따로 뒀다.
// ============================================================
public class UIHoverFader : MonoBehaviour
{
    public float fadeSpeed = 10f; // 숫자가 클수록 빠름
    private CanvasGroup _cg;
    private TMPro.TextMeshPro _tmp3D;
    private bool _isFadingOut = false;

    void Awake()
    {
        // 1. 일반 UI용 CanvasGroup 세팅
        _cg = GetComponent<CanvasGroup>();
        if(_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

        // 2. 3D TextMeshPro용 세팅 (CanvasGroup이 먹히지 않으므로 직접 제어)
        _tmp3D = GetComponentInChildren<TMPro.TextMeshPro>();

        _cg.alpha = 0f;
        if(_tmp3D != null) _tmp3D.alpha = 0f;
    }

    void Update()
    {
        if(_isFadingOut)
        {
            bool isDone = true;
            if(_cg != null)
            {
                _cg.alpha -= Time.deltaTime * fadeSpeed;
                if(_cg.alpha > 0f) isDone = false;
            }
            if(_tmp3D != null)
            {
                _tmp3D.alpha -= Time.deltaTime * fadeSpeed;
                if(_tmp3D.alpha > 0f) isDone = false;
            }

            // 알파값이 완전히 0이 되면 스스로 파괴
            if(isDone) Destroy(gameObject);
        }
        else
        {
            // 나타날 때
            if(_cg != null && _cg.alpha < 1f) _cg.alpha += Time.deltaTime * fadeSpeed;
            if(_tmp3D != null && _tmp3D.alpha < 1f) _tmp3D.alpha += Time.deltaTime * fadeSpeed;
        }
    }

    public void FadeOut()
    {
        _isFadingOut = true;
    }
}
