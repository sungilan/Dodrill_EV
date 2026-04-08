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

public class MiniMapComponent : MonoBehaviour {
	[Tooltip("Set the icon of this gameobject")]
	public Sprite icon;
	[Tooltip("Set size of the icon")]
	public Vector2 size = new Vector2(20,20);
	[Tooltip("Set true if the icon rotates with the gameobject")]
	public bool rotateWithObject = false;
	[Tooltip("Adjust the rotation axis according to your gameobject. Values of each axis can be either -1,0 or 1")]
	public Vector3 upAxis = new Vector3(0,1,0);
	[Tooltip("Adjust initial rotation of the icon")]
	public float initialIconRotation;
	[Tooltip("If true the icons will be clamped in the border")]
	public bool clampIconInBorder = true;
	[Tooltip("Set the distance from target after which the icon will not be shown. Setting it 0 will always show the icon.")]
	public float clampDistance = 100;
    public string myMapName = "이름 입력";

    MiniMapController miniMapController;
	MiniMapEntity mme;
	MapObject mmo;

	void OnEnable(){
        miniMapController = Object.FindFirstObjectByType<MiniMapController>();
        mme = new MiniMapEntity ();
		mme.icon = icon;
		mme.rotation = initialIconRotation;
		mme.size = size;
		mme.upAxis = upAxis;
		mme.rotateWithObject = rotateWithObject;
		mme.clampInBorder = clampIconInBorder;
		mme.clampDist = clampDistance;

        // 1. 플레이어라면 플랫폼 정보를 확인하여 아이콘 결정
        if(gameObject.CompareTag("Player"))
        {
            // UserName 반영 로직 (이전 작업 내용)
            if(!string.IsNullOrEmpty(UserInfo.UserName))
            {
                myMapName = UserInfo.UserName;
            }
        }
        else
        {
            // 플레이어가 아닌 경우 기존처럼 단일 아이콘 사용 (기존 변수 활용 시)
            mme.icon = icon;
        }

        mme.objectName = myMapName;
        mmo = miniMapController.RegisterMapObject(this.gameObject, mme);
	}

	void OnDisable(){
		miniMapController.UnregisterMapObject (mmo,this.gameObject);
	}

	void OnDestroy(){
		miniMapController.UnregisterMapObject (mmo,this.gameObject);
	}

}
