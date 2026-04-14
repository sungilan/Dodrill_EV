using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XRAirpotrSecurity;

public class MiniMapEntity{
	public bool showDetails = false;
	public Sprite icon;
	public bool rotateWithObject = true;
	public Vector3 upAxis;
	public float rotation;
	public Vector2 size;
	public bool clampInBorder;
	public float clampDist;
	public List<GameObject> mapObjects;
    public string objectName;
}

public class MiniMapComponent : MonoBehaviour
{
    [Header("아이콘 설정")]
    public Sprite icon;
    public Vector2 size = new Vector2(20, 20);
    [Tooltip("텍스트와 아이콘이 오브젝트를 따라 회전할지 여부")]
    public bool rotateWithObject = false;
    public Vector3 upAxis = new Vector3(0, 1, 0);
    public float initialIconRotation;

    [Header("클램프 설정")]
    public bool clampIconInBorder = true;
    public float clampDistance = 100;
    public string myMapName = "이름 입력";

    private MiniMapController miniMapController;
    private MiniMapEntity mme;
    private MapObject mmo;
    private bool _isRegistered = false;

    private void OnEnable()
    {
        miniMapController = Object.FindFirstObjectByType<MiniMapController>();
    }

    private IEnumerator Start()
    {
        if(miniMapController == null) yield break;

        // 1. 플레이어 이름 로딩 대기
        if(gameObject.CompareTag("Player"))
        {
            float timeout = 3.0f;
            while(string.IsNullOrEmpty(UserInfo.UserName) && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if(!string.IsNullOrEmpty(UserInfo.UserName)) myMapName = UserInfo.UserName;
        }

        RegisterToMap();
    }

    private void RegisterToMap()
    {
        if(_isRegistered) return;

        mme = new MiniMapEntity();
        mme.icon = icon;
        mme.rotation = initialIconRotation;
        mme.size = size;
        mme.upAxis = upAxis;
        mme.rotateWithObject = rotateWithObject;
        mme.clampInBorder = clampIconInBorder;
        mme.clampDist = clampDistance;
        mme.objectName = myMapName;

        mmo = miniMapController.RegisterMapObject(this.gameObject, mme);
        _isRegistered = true;
    }

    // ── 텍스트 및 아이콘 강제 고정 로직 ──────────────────────────────

    private void LateUpdate()
    {
        // 회전 고정 옵션이 꺼져있을 때만 실행
        if(!_isRegistered || rotateWithObject || mmo == null) return;

        // MapObject의 spr(Image)이 아이콘 객체입니다.
        if(mmo.spr != null)
        {
            // 부모의 회전 영향을 받지 않도록 월드 회전을 0으로 고정
            mmo.spr.transform.rotation = Quaternion.identity;
        }

        // MapObject의 labelText(TMP)가 텍스트 객체입니다.
        if(mmo.labelText != null)
        {
            // 텍스트도 항상 정방향을 유지하도록 고정
            mmo.labelText.transform.rotation = Quaternion.identity;
        }
    }

    private void OnDisable() { UnregisterFromMap(); }
    private void OnDestroy() { UnregisterFromMap(); }

    private void UnregisterFromMap()
    {
        if(_isRegistered && miniMapController != null)
        {
            miniMapController.UnregisterMapObject(mmo, this.gameObject);
            _isRegistered = false;
        }
    }
}
