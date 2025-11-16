using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System.Collections;
using Cysharp.Threading.Tasks; // 确保包含此命名空间
using UnityEngine.SceneManagement;

namespace DuckDivers
{
    public class DestroyerManager : MonoBehaviour
    {
        public static DestroyerManager Instance { get; private set; }

        private GameObject _destroyerInstance; // 存储 Destroyer 的实例
        private AssetManager _assetManager;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // 让这个管理器在场景加载时不被销毁

            // 可以在这里初始化 AssetManager，如果它尚未初始化
            if (!AssetManager.Instance.IsInitialized)
            {
                AssetManager.Instance.Initialize();
            }
            _assetManager = AssetManager.Instance;

            // 确保 Harmony 补丁应用
            DestroyerPlacementPatch.ApplyPatch();

        }

        public GameObject GetDestroyer()
        {
            if (_destroyerInstance == null)
            {
            }
            return _destroyerInstance;
        }

        // 异步加载并放置 Destroyer
        public async UniTask PlaceDestroyerAsync(Vector3 playerPosition)
        {
            if (_destroyerInstance != null)
            {
                // 如果已经存在，直接更新位置
                _destroyerInstance.transform.position = playerPosition + Vector3.up * 30f;
                _destroyerInstance.SetActive(true); // 确保激活
                return;
            }


            GameObject loadedPrefab = await _assetManager.LoadAssetAsync<GameObject>("Assets/DuckDivers/Destroyer.prefab");
            if (loadedPrefab != null)
            {
                _destroyerInstance = Instantiate(loadedPrefab, playerPosition + Vector3.up * 30f, Quaternion.identity);
                // 移动到当前活动场景，或者如果玩家在主场景，则移动到主场景
                Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_destroyerInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                }
                else if (activeScene.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_destroyerInstance, activeScene);
                }
                else
                {
                }


                _destroyerInstance.SetActive(true);
                // 可能需要移除或禁用 Rigidbody、Collider 等组件，以确保它停留在空中
                Rigidbody rb = _destroyerInstance.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    Destroy(rb); // 如果不需要物理模拟，直接移除
                }
                Collider col = _destroyerInstance.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                }

            }
            else
            {
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                // 清理 Destroyer 实例
                if (_destroyerInstance != null)
                {
                    Destroy(_destroyerInstance);
                    _destroyerInstance = null;
                }
                DestroyerPlacementPatch.RemovePatch(); // 移除补丁
            }
        }
    }

    // Harmony 补丁，用于在玩家生成时触发 Destroyer 的放置
    [HarmonyPatch(typeof(CharacterMainControl), "Awake")] // 假设CharacterMainControl的Awake在玩家生成时调用
    public static class DestroyerPlacementPatch
    {
        public static void ApplyPatch()
        {
            var harmony = new Harmony("destroyerplacement"); // 你的Mod ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static void RemovePatch()
        {
            var harmony = new Harmony("destroyerplacement");
            harmony.UnpatchAll("destroyerplacement");
        }

        [HarmonyPostfix]
        public static void Postfix(CharacterMainControl __instance)
        {
            if (true)
            {
                // 确保 DestroyerManager 已初始化
                if (DestroyerManager.Instance == null)
                {
                    new GameObject("DestroyerManager").AddComponent<DestroyerManager>();
                }
                // 异步放置 Destroyer
                DestroyerManager.Instance.PlaceDestroyerAsync(__instance.transform.position).Forget(); // 使用 .Forget() 处理 UniTask 的 async void
            }
        }
    }
}