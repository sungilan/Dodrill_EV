using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEditor;
using FishNet.Object;
#endif

// ============================================================
//  ItemPrefabSetup.cs
//  에디터 전용 유틸 — 아이템 프리팹에 NetworkObject +
//  LocalNetworkTransform 일괄 추가
//
//  사용 방법:
//    1. 씬의 빈 GameObject에 이 컴포넌트 추가
//    2. Inspector에서 대상 프리팹 폴더 경로 입력
//       (기본값: Assets/03_3D)
//    3. "① 프리팹 스캔" 버튼 → 목록 확인
//    4. "② NetworkObject 일괄 추가" 버튼 실행
//    5. 완료 후 이 GameObject 삭제해도 무방
// ============================================================

namespace DoDrill.Training
{
    public class ItemPrefabSetup : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("검색 설정")]
        [Tooltip("프리팹을 검색할 폴더 경로 (Assets 기준)")]
        [FolderPath]
        [SerializeField] private string _searchFolder = "Assets/03_3D";

        [Tooltip("Item_ 또는 SM_ 접두사가 붙은 프리팹만 대상으로 할지 여부")]
        [SerializeField] private bool _onlyItemPrefix = true;

        [Header("스캔 결과 (읽기 전용)")]
        [ReadOnly]
        [SerializeField] private List<GameObject> _foundPrefabs = new();

        // ── 버튼 ────────────────────────────────────────────

        [Button("① 프리팹 스캔", ButtonSizes.Large), GUIColor(0.4f, 0.8f, 1f)]
        private void ScanPrefabs()
        {
            _foundPrefabs.Clear();

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { _searchFolder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                if (_onlyItemPrefix &&
                    !go.name.StartsWith("Item_", System.StringComparison.OrdinalIgnoreCase) &&
                    !go.name.StartsWith("SM_", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                _foundPrefabs.Add(go);
            }

            Debug.Log($"[ItemPrefabSetup] 스캔 완료 — {_foundPrefabs.Count}개 발견 ({_searchFolder})");
        }

        [Button("② NetworkObject + LocalNetworkTransform 일괄 추가", ButtonSizes.Large), GUIColor(0.4f, 1f, 0.6f)]
        private void AddNetworkComponents()
        {
            if (_foundPrefabs.Count == 0)
            {
                Debug.LogWarning("[ItemPrefabSetup] 먼저 ① 스캔을 실행하세요.");
                return;
            }

            int addedNetObj = 0, addedLNT = 0, skipped = 0;

            foreach (var prefabAsset in _foundPrefabs)
            {
                if (prefabAsset == null) continue;

                string path = AssetDatabase.GetAssetPath(prefabAsset);
                using var scope = new PrefabUtility.EditPrefabContentsScope(path);
                var root = scope.prefabContentsRoot;

                bool dirty = false;

                // NetworkObject 추가
                if (root.GetComponent<NetworkObject>() == null)
                {
                    root.AddComponent<NetworkObject>();
                    addedNetObj++;
                    dirty = true;
                }
                else
                {
                    skipped++;
                }

                // LocalNetworkTransform 추가
                if (root.GetComponent<LocalNetworkTransform>() == null)
                {
                    root.AddComponent<LocalNetworkTransform>();
                    addedLNT++;
                    dirty = true;
                }

                if (dirty)
                    Debug.Log($"[ItemPrefabSetup] 컴포넌트 추가: {prefabAsset.name}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ItemPrefabSetup] 완료 — NetworkObject 추가:{addedNetObj}, " +
                      $"LocalNetworkTransform 추가:{addedLNT}, 이미 있음:{skipped}");
        }

        [Button("③ 스캔 목록 초기화", ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.4f)]
        private void ClearList()
        {
            _foundPrefabs.Clear();
        }
#endif
    }
}
