

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Autohand;
using System.Linq;

namespace Autohand {

    public class AutoHandUpdateDataWizard : EditorWindow {
        public Texture autohandlogo;
        private SerializedObject serializedObject;

        public List<Hand> hands = new List<Hand>();
        public List<HandPoseDataContainer> scenePoses = new List<HandPoseDataContainer>();
        public List<HandPoseDataContainer> prefabPoses = new List<HandPoseDataContainer>();

        private SerializedProperty handsProperty;
        private SerializedProperty scenePosesProperty;
        private SerializedProperty prefabPosesProperty;


        private Vector2 scrollPosition;
        static bool loaded = false;
        public static AutoHandUpdateDataWizard window;

        bool validHandData;
        bool didValidationFail;

        static AutoHandSettings _handSettings = null;
        static AutoHandSettings handSettings {
            get {
                if(_handSettings == null)
                    _handSettings = Resources.Load<AutoHandSettings>("AutoHandSettings");
                return _handSettings;
            }
        }


        [UnityEditor.InitializeOnLoadMethod]
        public static void CheckSceneForOldPoses() {
            // 이 메서드는 도메인 리로드마다, 그리고 AssetImportWorker 같은 보조 Unity 프로세스에서도 실행된다.
            // 거기서 창을 만들면 닫히지 않은 빈 창이 프로세스마다 계속 쌓여 작업 표시줄이 Unity 창으로 가득 찬다.
            // → 사람이 조작하는 메인 에디터에서만, 리로드가 끝난 뒤(delayCall)에 한 번 검사한다.
            if(Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess())
                return;
            EditorApplication.delayCall += CheckSceneForOldPosesDeferred;
        }

        static void CheckSceneForOldPosesDeferred() {
            if(loaded) return;
            loaded = true;   // 예외가 나도 다음 리로드 전까지 다시 열지 않는다

            try {
                if(window != null)
                    window.Close();

                if(EditorWindow.HasOpenInstances<AutoHandUpdateDataWizard>())
                    return;

                window = GetWindow<AutoHandUpdateDataWizard>("Update Pose Data");
                if(handSettings != null && handSettings.quality == -1 && !handSettings.ignoreSetup)
                    window.FindPrefabPoses();

                if(window.FindScenePoses() == 0 && (window.prefabPoses.Count) == 0)
                    window.Close();
            }
            catch(System.Exception e) {
                Debug.LogWarning("[AutoHand] 포즈 데이터 검사 중 오류 — 창을 닫습니다: " + e.Message);
                if(window != null) window.Close();
            }
        }

        [MenuItem("Window/Autohand/Pose Data Updater")]
        public static void ShowWindow() {
            if(window != null)
                window.Close();

            if(EditorWindow.HasOpenInstances<AutoHandUpdateDataWizard>())
                return;

            window = GetWindow<AutoHandUpdateDataWizard>(true);
            window.minSize = new Vector2(320, 440);
            window.maxSize = new Vector2(360, 500);
            window.titleContent = new GUIContent("Auto Hand Setup");
        }

        void OnEnable() {
            serializedObject = new SerializedObject(this);
            handsProperty = serializedObject.FindProperty("hands");
            scenePosesProperty = serializedObject.FindProperty("scenePoses");
            prefabPosesProperty = serializedObject.FindProperty("prefabPoses");
        }




        void OnGUI() {
            serializedObject.Update();
            ShowTitle();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            ShowHandReferences();
            ShowPoseProperties();
            EditorGUILayout.EndScrollView();

            GUILayout.Space(4f);
            if(GUI.Button(qualitySliderRect, "Find Scene Poses"))
                FindScenePoses();

            if(GUI.Button(qualitySliderRect, "Find Prefab Poses"))
                FindPrefabPoses();

            ShowUpdateDataButton();
            GUILayout.Space(8f);

            serializedObject.ApplyModifiedProperties();
        }



        void ShowUpdateDataButton() {
            GUILayout.Space(8f);
            if(!validHandData || didValidationFail) {
                GUI.backgroundColor = Color.red / 4f;
                GUI.Button(qualitySliderRect, "Invalid Hand References");
            }
            else if(GUI.Button(qualitySliderRect, "Update Poses")) {
                if(hands.Count == 0) {
                    Debug.LogError("AUTO HAND: No hands to update poses with, please include at the hand(s) that was used to create the poses");
                    return;
                }
                else {
                    foreach(var hand in hands) {
                        foreach(var finger in hand.fingers) {
                            finger.UpdateDepricatedValues();
                        }
                    }
                }

                foreach(var pose in prefabPoses) {
                    foreach(var hand in hands) {
                        if(hand.poseIndex == pose.poseIndex) {
                            pose.UpdateDepricatedData(hand);
                            EditorUtility.SetDirty(pose);
                        }
                    }
                }

                AssetDatabase.Refresh();
#if UNITY_2020_1_OR_NEWER
                AssetDatabase.RefreshSettings();
#endif
                prefabPoses.Clear();

                foreach(var pose in scenePoses) {
                    foreach(var hand in hands) {
                        if(hand.poseIndex == pose.poseIndex) {
                            pose.UpdateDepricatedData(hand);
                            EditorUtility.SetDirty(pose);
                        }
                    }
                }

                AssetDatabase.Refresh();
#if UNITY_2020_1_OR_NEWER
                AssetDatabase.RefreshSettings();
#endif
                scenePoses.Clear();

            }
        }

        void ShowPoseProperties() {
            EditorGUILayout.LabelField(new GUIContent("Poses to Update"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(prefabPosesProperty, new GUIContent("Prefab Poses"), true);
            EditorGUILayout.PropertyField(scenePosesProperty, new GUIContent("Scene Poses"), true);
        }

        void ShowTitle() {
            if(autohandlogo == null) {
                var assets = AssetDatabase.FindAssets("AutohandLogo");
                if(assets.Length > 0)
                    autohandlogo = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(assets[0]));
            }

            var rect = EditorGUILayout.GetControlRect();
            rect.height *= 5;
            GUI.Label(rect, autohandlogo, AutoHandExtensions.LabelStyle(TextAnchor.MiddleCenter, FontStyle.Normal, 25));

            EditorGUILayout.Space(70f);
            EditorGUILayout.LabelField("POSE DATA UPDATER", AutoHandExtensions.LabelStyle(TextAnchor.MiddleCenter, FontStyle.Normal, 24));
            EditorGUILayout.Space(8f);
        }

        void ShowHandReferences() {
            EditorGUILayout.LabelField(new GUIContent("Refence Hands", "These hand will try to be automatically updated if not updated, then they will be used as a reference to update the data in all the given pose objects below if they have the same pose index as the given hand"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(handsProperty, new GUIContent("Hands"), true);
            didValidationFail = false;
            validHandData = true;

            foreach(var hand in hands) {
                if(hand == null)
                    continue;

                foreach(var finger in hand.fingers) {
                    if(!finger.isMissingReferences && !finger.isDataDepricated)
                        continue;
                    else if(finger.UpdateDepricatedValues())
                        continue;

                    if(finger.isMissingReferences && (didValidationFail || !finger.UpdateDepricatedValues())) {
                        didValidationFail = true;
                        EditorGUILayout.HelpBox("Finger data is missing required references, as of V3.3 fingers require manual connects to each of three joints", MessageType.Error);
                        break;
                    }
                    else if(finger.isDataDepricated && ((validHandData && !didValidationFail) || !finger.UpdateDepricatedValues())) {
                        validHandData = false;
                        EditorGUILayout.HelpBox("Finger data is depricated - please try manually updating the finger references on your hand and try again", MessageType.Warning);
                        break;
                    }
                }
            }

            GUILayout.Space(8f);
        }

        public void UpdatePoses() {
            foreach(var hand in hands) {
                foreach(var finger in hand.fingers) {
                    if(!finger.UpdateDepricatedValues()) {
                        Debug.LogError("AUTO HAND: Finger data is depricated - please try manually updating the finger references on your hand and try again", finger);
                        return;
                    }
                }

                foreach(var pose in prefabPoses) {
                    if(hand.poseIndex == pose.poseIndex)
                        pose.UpdateDepricatedData(hand);
                    else
                        Debug.Log("Pose not marked as depricated or mismatching pose index", pose);
                }

                foreach(var pose in scenePoses) {
                    if(hand.poseIndex == pose.poseIndex)
                        pose.UpdateDepricatedData(hand);
                    else
                        Debug.Log("Pose not marked as depricated or mismatching pose index", pose);
                }
            }
        }

        public int FindScenePoses() {
            scenePoses.Clear();
            scenePoses = FindObjectsByType<HandPoseDataContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
            Debug.Log("Found " + scenePoses.Count + " scene poses");
            for(int i = scenePoses.Count - 1; i >= 0; i--) {
                if(!scenePoses[i].isDataDepricated)
                    scenePoses.RemoveAt(i);
            }

            return scenePoses.Count;
        }

        public int FindPrefabPoses() {
            prefabPoses.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach(var guid in guids) {
                var assetObject = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var poses = assetObject.GetComponentsInChildren<HandPoseDataContainer>(true);
                foreach(var pose in poses)
                    if(pose != null && pose.isDataDepricated)
                        prefabPoses.Add(pose);
            }

            return prefabPoses.Count;
        }

        public Rect qualitySliderRect {
            get {
                var _qualitySlider = EditorGUILayout.GetControlRect();
                _qualitySlider.x += _qualitySlider.width / 20f;
                _qualitySlider.width *= 9 / 10f;
                return _qualitySlider;
            }
        }

        public Rect qualityLabelRect {
            get {
                var _qualitySlider = EditorGUILayout.GetControlRect();
                _qualitySlider.height *= 1.5f;
                return _qualitySlider;
            }
        }
    }
}