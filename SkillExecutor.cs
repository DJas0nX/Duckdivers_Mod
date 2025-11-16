using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.Events;
using FMOD;
using FMODUnity;
using System.IO;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;
using System.Reflection;
using Cysharp.Threading.Tasks;
using static UnityEngine.Rendering.DebugUI.Table;
using System.Linq; 
using HarmonyLib;
using System.Reflection;
using UnityEngine.UIElements;
using TMPro;

namespace DuckDivers
{


    public class SkillExecutor : MonoBehaviour
    {

        [HarmonyPatch]
        public static class ExplosionFxScalerPatch
        {
            // 我们需要一个方法来启用和禁用补丁，通常在你的 Mod 主类中调用
            public static void ApplyPatch()
            {
                var harmony = new Harmony("explosionfxscaler"); // 替换为你的唯一Mod ID
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }

            public static void RemovePatch()
            {
                var harmony = new Harmony("explosionfxscaler");
                harmony.UnpatchAll("explosionfxscaler");
            }

            [HarmonyPatch(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Instantiate), typeof(UnityEngine.Object), typeof(Vector3), typeof(Quaternion))] 
            [HarmonyPostfix]
            public static void Postfix(UnityEngine.Object __result, UnityEngine.Object original) 
            {
                // __result 现在是 UnityEngine.Object 类型，将其转换为 GameObject
                GameObject instantiatedGameObject = __result as GameObject;
                float scaler = 3f;

                if (instantiatedGameObject != null && original != null)
                {
                    string originalName = original.name;

                    if (originalName.Contains("Explosion01") || originalName == "ExplosionFx" || originalName.Contains("Explode") || originalName.Contains("GrenadeZone"))
                    {
                        if (originalName.Contains("GrenadeZone")) { scaler = 2f; }
                        instantiatedGameObject.transform.localScale = Vector3.one * scaler; // 放大3倍
                        foreach (Transform childTransform in instantiatedGameObject.GetComponentsInChildren<Transform>(true))
                        {
                            ParticleSystem ps = childTransform.GetComponent<ParticleSystem>();
                            if (ps != null)
                            {
                                var main = ps.main;
                                if (main.startSizeMultiplier > 0)
                                {
                                    main.startSizeMultiplier *= scaler;
                                }
                                var shape = ps.shape;
                                if (shape.enabled)
                                {
                                    if (shape.shapeType == ParticleSystemShapeType.Sphere ||
                                        shape.shapeType == ParticleSystemShapeType.Hemisphere ||
                                        shape.shapeType == ParticleSystemShapeType.Cone)
                                    {
                                        shape.radius *= scaler;
                                    }
                                    else if (shape.shapeType == ParticleSystemShapeType.Box)
                                    {
                                        Vector3 scale = shape.scale;
                                        scale *= scaler;
                                        shape.scale = scale;
                                    }
                                }

                                if (childTransform != instantiatedGameObject.transform) 
                                {
                                    childTransform.localScale *= scaler; 
                                }
                            }


                            Projector projector = childTransform.GetComponent<Projector>();
                            if (projector != null)
                            {
                                projector.orthographicSize *= scaler; // 放大正交投影尺寸
                                projector.farClipPlane *= scaler; 
                            }
                        }
                    }
                }
            }
        }

        private GameObject smokeGrenadePrefabRef;   // 用于存储找到的烟雾弹Prefab引用
        private GameObject fireGrenadePrefabRef;    // 用于存储找到的火焰弹Prefab引用
        private GameObject poisonGrenadePrefabRef;  // 用于存储找到的毒气弹Prefab引用

        private bool smokePrefabInitialized = false; // 标记烟雾弹Prefab是否已找到并初始化
        private bool firePrefabInitialized = false;  // 标记火焰弹Prefab是否已找到并初始化
        private bool poisonPrefabInitialized = false; // 标记毒气弹Prefab是否已找到并初始化

        private const int SMOKE_GRENADE_ITEM_ID = 660;
        private const int FIRE_GRENADE_ITEM_ID = 941;
        private const int POISON_GRENADE_ITEM_ID = 933;
        private InputPanelManager _inputPanelManager;

        private GameObject _destroyerPrefab;
        private GameObject _activeDestroyerInstance;

        // Mod加载或场景加载时自动执行
        void Awake()
        {
            StartCoroutine(InitializeAllGrenadePrefabsDelayed());
            StartCoroutine(InitializeDestroyerPrefabDelayed());

            SceneManager.sceneLoaded += OnSceneLoaded;
            _inputPanelManager = FindObjectOfType<InputPanelManager>();
        }
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(HandleDestroyerOnSceneLoadDelayed(3f));
        }

        IEnumerator HandleDestroyerOnSceneLoadDelayed(float delay)
        {
            yield return new WaitForSeconds(delay); 

            if (_activeDestroyerInstance != null)
            {
                DestroyerController destroyerController = _activeDestroyerInstance.GetComponent<DestroyerController>();
                if (destroyerController != null)
                {
                    destroyerController.RelocateDestroyer();
                }
                else
                {
                    Destroy(_activeDestroyerInstance);
                    _activeDestroyerInstance = null;
                    StartCoroutine(SpawnDestroyerAtPlayerHeadDelayed());
                }
            }
            else
            {
                StartCoroutine(SpawnDestroyerAtPlayerHeadDelayed());
            }
        }


        IEnumerator InitializeDestroyerPrefabDelayed()
        {
            yield return null; 

            while (LevelManager.Instance == null || LevelManager.Instance.MainCharacter == null)
            {
                yield return null;
            }

            _destroyerPrefab = _assetManager.LoadAsset<GameObject>("Assets/Airborne/Destroyer.prefab");
            if (_destroyerPrefab == null)
            {
            }
            else
            {
            }
        }

        // 延迟生成 Destroyer
        IEnumerator SpawnDestroyerAtPlayerHeadDelayed()
        {
            while (LevelManager.Instance == null || LevelManager.Instance.MainCharacter == null)
            {
                yield return null;
            }

            while (_destroyerPrefab == null || !SceneManager.GetActiveScene().isLoaded)
            {
                yield return null;
            }

            if (_activeDestroyerInstance != null)
            { 
                DestroyerController existingController = _activeDestroyerInstance.GetComponent<DestroyerController>();
                if (existingController != null)
                {
                    existingController.RelocateDestroyer();
                }
                yield break;
            }


            CharacterMainControl player = LevelManager.Instance.MainCharacter;
            Vector3 spawnPosition = player.transform.position + Vector3.up * 60f;
            Quaternion spawnRotation = Quaternion.Euler(-90f, 0f, 0f);

            _activeDestroyerInstance = Instantiate(_destroyerPrefab, spawnPosition, spawnRotation);
            if (_activeDestroyerInstance != null)
            {
                // 移动到当前活动场景
                SceneManager.MoveGameObjectToScene(_activeDestroyerInstance, SceneManager.GetActiveScene());

                DestroyerController controller = _activeDestroyerInstance.GetComponent<DestroyerController>();
                if (controller == null)
                {
                    controller = _activeDestroyerInstance.AddComponent<DestroyerController>();
                }
                controller.Initialize(player.transform, 80f); // 初始化控制器
                transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

                foreach (Renderer r in _activeDestroyerInstance.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                }
                // 如果有碰撞体，也禁用
                foreach (Collider c in _activeDestroyerInstance.GetComponentsInChildren<Collider>(true))
                {
                    c.enabled = false;
                }

            }
            else
            {
            }
        }

        IEnumerator InitializeAllGrenadePrefabsDelayed()
        {
            // 等待一帧，确保所有Awake都被调用，并允许LevelManager等单例初始化
            yield return null;

            // 也可以等待直到某个关键系统（如LevelManager）可用
            while (LevelManager.Instance == null || LevelManager.Instance.MainCharacter == null)
            {
                yield return null;
            }


            // 初始化 Smoke Zone
            // 调用新的协程，它将返回找到的 GameObject 或 null
            IEnumerator smokeInitRoutine = InitializeGrenadeZonePrefab(SMOKE_GRENADE_ITEM_ID, "GrenadeZone_Smoke");
            yield return smokeInitRoutine;
            smokeGrenadePrefabRef = (GameObject)smokeInitRoutine.Current; // 获取协程的返回值
            smokePrefabInitialized = (smokeGrenadePrefabRef != null);


            // 初始化 Fire Zone
            IEnumerator fireInitRoutine = InitializeGrenadeZonePrefab(FIRE_GRENADE_ITEM_ID, "GrenadeZone_Fire");
            yield return fireInitRoutine;
            fireGrenadePrefabRef = (GameObject)fireInitRoutine.Current;
            firePrefabInitialized = (fireGrenadePrefabRef != null);


            // 初始化 poison Zone
            IEnumerator poisonInitRoutine = InitializeGrenadeZonePrefab(POISON_GRENADE_ITEM_ID, "GrenadeZone_Poison");
            yield return poisonInitRoutine;
            poisonGrenadePrefabRef = (GameObject)poisonInitRoutine.Current;
            poisonPrefabInitialized = (poisonGrenadePrefabRef != null);


            if (!smokePrefabInitialized)
            {
            }
            if (!firePrefabInitialized)
            {
            }
            if (!poisonPrefabInitialized)
            {
            }
        }

        // 修正后的 InitializeGrenadeZonePrefab 方法，返回 GameObject 而不是使用 ref
        IEnumerator InitializeGrenadeZonePrefab(int itemID, string zoneName)
        {

            GameObject foundPrefab = null; // 用于存储找到的 Prefab

            Grenade grenadeItemPrefab = GetGrenadePrefabFromItemID(itemID);
            if (grenadeItemPrefab != null)
            {
                Vector3 spawnPosition = new Vector3(0, -1000f, 0);
                Grenade tempGrenade = UnityEngine.Object.Instantiate(grenadeItemPrefab, spawnPosition, Quaternion.identity);

                if (tempGrenade.GetComponent<Rigidbody>() != null)
                {
                    tempGrenade.GetComponent<Rigidbody>().isKinematic = true;
                }
                if (tempGrenade.GetComponent<Collider>() != null)
                {
                    tempGrenade.GetComponent<Collider>().enabled = false;
                }
                tempGrenade.createExplosion = false;
                tempGrenade.isDangerForAi = false;
                tempGrenade.delayTime = 9999f;

                if (tempGrenade.createOnExlode != null && tempGrenade.createOnExlode.name == zoneName)
                {
                    foundPrefab = tempGrenade.createOnExlode; // 存储找到的 Prefab
                }
                else
                {
                }
                UnityEngine.Object.Destroy(tempGrenade.gameObject);
            }
            else
            {
            }

            // 关键：返回找到的 GameObject
            yield return foundPrefab;
        }

        private Grenade GetGrenadePrefabFromItemID(int itemID)
        {
            Grenade[] allGrenadeAssetsAndInstances = Resources.FindObjectsOfTypeAll<Grenade>();

            foreach (Grenade g in allGrenadeAssetsAndInstances)
            {
                if (string.IsNullOrEmpty(g.gameObject.scene.name)) // Prefab/Asset
                {
                    if (g.createOnExlode != null)
                    {
                        string targetZoneName = "";
                        if (itemID == SMOKE_GRENADE_ITEM_ID) targetZoneName = "GrenadeZone_Smoke";
                        else if (itemID == FIRE_GRENADE_ITEM_ID) targetZoneName = "GrenadeZone_Fire";
                        else if (itemID == POISON_GRENADE_ITEM_ID) targetZoneName = "GrenadeZone_Poison";

                        if (!string.IsNullOrEmpty(targetZoneName) && g.createOnExlode.name == targetZoneName)
                        {
                            return g;
                        }
                    }
                }
            }
            return null;
        }


        public class DestroyerController : MonoBehaviour
        {
            private Transform _playerTransform;
            private float _offsetY;
            private Vector3 _initialPlayerPosition; // 记录玩家初始位置

            public void Initialize(Transform player, float offsetY)
            {
                _playerTransform = player;
                _offsetY = offsetY;
                _initialPlayerPosition = player.position; // 记录玩家初始位置
                RelocateDestroyer(); // 首次初始化时定位
            }

            void Update()
            {

            }

            public void RelocateDestroyer()
            {
                if (_playerTransform != null)
                {
                    _initialPlayerPosition = _playerTransform.position; // 更新玩家初始位置
                    Vector3 newPosition = _playerTransform.position;
                    newPosition.y = _offsetY;
                    transform.position = newPosition;
                }
            }
        }

        private AssetManager _assetManager;
        private Dictionary<string, FMOD.Sound> _modSounds_FMOD;
        private System.Random _random;

        private GameObject _explosionNormalFxPfb;
        private Projectile _defaultBulletPfb; // 新增：用于飞鹰机枪扫射的默认子弹预制体

        private const int TARGET_ITEM_ID = 1257; // 飞鹰空袭的触发物品ID (即对应手雷)
        private const int HEAD_ITEM_ID = 45;
        private HashSet<Grenade> _processedGrenades = new HashSet<Grenade>();

        private Dictionary<Vector3, (GameObject bomb, GameObject replacedBallSyncObject)> _active500kgEffects = new Dictionary<Vector3, (GameObject bomb, GameObject replacedBallSyncObject)>();

        private Dictionary<Grenade, GameObject> _originalGrenadeModels = new Dictionary<Grenade, GameObject>();
        private Dictionary<Grenade, GameObject> _replacedGrenadeSyncObjects = new Dictionary<Grenade, GameObject>();

        // 新增：待生效的技能名称
        private string _pendingSkillName = null;


        public void Initialize(AssetManager assetManager, Dictionary<string, FMOD.Sound> modSounds, System.Random random)
        {
            _assetManager = assetManager;
            _modSounds_FMOD = modSounds;
            _random = random;
            // 尝试获取默认子弹预制体，通常在 GameplayDataSettings 中
            if (GameplayDataSettings.Prefabs != null)
            {
                _defaultBulletPfb = GameplayDataSettings.Prefabs.DefaultBullet;
            }
            if (_defaultBulletPfb == null)
            {
            }

            StartCoroutine(GrenadeDetectionCoroutine());
        }

        // 新增：设置待生效技能的方法
        public void SetPendingSkill(string skillName)
        {
            _pendingSkillName = skillName;
        }

        // 技能的实际执行方法，现在被GrenadeDetectionCoroutine调用
        private void ExecuteSkill(string skillName, Vector3 triggerPosition, DamageInfo originalDamageInfo, float originalShakeStrength, bool canHurtSelf, float originalDamageRange, Vector3 playerThrowPosition, GameObject replacedBallSyncObject)
        {
            if (_inputPanelManager != null)
            {
                _inputPanelManager.OnSkillCalled();
            }
            ExplosionManager currentExplosionManager = LevelManager.Instance?.ExplosionManager;
            if (currentExplosionManager == null)
            {
                return;
            }
            if (_explosionNormalFxPfb == null)
            {
                _explosionNormalFxPfb = currentExplosionManager.normalFxPfb;
                if (_explosionNormalFxPfb == null)
                {
                }
            }

            switch (skillName)
            {
                case "飞鹰空袭":
                    StartCoroutine(EagleAirstrike(
                        triggerPosition,
                        originalDamageInfo,
                        originalShakeStrength,
                        canHurtSelf,
                        originalDamageRange,
                        playerThrowPosition,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                case "飞鹰500kg炸弹":
                    Vector3 bombLandPosition = triggerPosition;
                    RaycastHit hit;
                    if (Physics.Raycast(triggerPosition + Vector3.up * 5f, Vector3.down, out hit, 100f, LayerMask.GetMask("Ground", "Default", "Terrain")))
                    {
                        bombLandPosition = hit.point;
                        // 修正落点高度
                        bombLandPosition.y += 0.2f; // 落点比先前高0.2f
                    }
                    else
                    {
                        if (bombLandPosition.y < 0) bombLandPosition.y = 0.1f;
                        // 修正落点高度
                        bombLandPosition.y += 0.2f; // 落点比先前高0.2f
                    }

                    StartCoroutine(_500kgBombSequence(bombLandPosition, originalDamageInfo.fromCharacter, currentExplosionManager, replacedBallSyncObject)); // 传递 replacedBallSyncObject
                    break;
                // 新增：飞鹰机枪扫射技能
                case "飞鹰机枪扫射":
                    Vector3 strafingTargetPosition = triggerPosition;
                    RaycastHit strafeHit;
                    if (Physics.Raycast(triggerPosition + Vector3.up * 5f, Vector3.down, out strafeHit, 100f, LayerMask.GetMask("Ground", "Default", "Terrain")))
                    {
                        strafingTargetPosition = strafeHit.point;
                        strafingTargetPosition.y += 0.01f; // 稍微抬高一点，防止陷入地面
                    }
                    else
                    {
                        if (strafingTargetPosition.y < 0) strafingTargetPosition.y = 0.01f;
                        strafingTargetPosition.y += 0.01f;
                    }
                    StartCoroutine(_EagleStrafingSequence(strafingTargetPosition, originalDamageInfo.fromCharacter, playerThrowPosition, replacedBallSyncObject));
                    break;
                case "飞鹰凝固汽油弹空袭":
                    StartCoroutine(_EagleNapalm(
                        triggerPosition,
                        originalDamageInfo,
                        originalShakeStrength,
                        canHurtSelf,
                        originalDamageRange,
                        playerThrowPosition,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                case "轨道精准攻击":
                    StartCoroutine(_OrbitalPrecisionStrike(
                        _activeDestroyerInstance.transform.position,
                        triggerPosition,
                        originalDamageInfo.fromCharacter,
                        currentExplosionManager,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                case "轨道毒气攻击":
                    StartCoroutine(_OrbitalGasStrike(
                        _activeDestroyerInstance.transform.position,
                        triggerPosition,
                        originalDamageInfo.fromCharacter,
                        currentExplosionManager,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                case "轨道烟雾攻击":
                    StartCoroutine(_OrbitalSmokeStrike(
                        _activeDestroyerInstance.transform.position,
                        triggerPosition,
                        originalDamageInfo.fromCharacter,
                        currentExplosionManager,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                case "轨道380MM高爆弹火力网":
                    StartCoroutine(Orbital380MMBarrage(
                        _activeDestroyerInstance.transform.position,
                        triggerPosition,
                        originalDamageInfo.fromCharacter,
                        currentExplosionManager,
                        replacedBallSyncObject // 传递 ballSyncObject
                    ));
                    break;
                default:

                    // 如果是未知技能，也应该清理球模型，以免残留
                    if (replacedBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = replacedBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(replacedBallSyncObject);
                        }
                    }
                    break;
            }
        }

        public void Cleanup()
        {
            StopAllCoroutines();
            _processedGrenades.Clear();

            foreach (var replacedSyncObject in _replacedGrenadeSyncObjects.Values)
            {
                if (replacedSyncObject != null)
                {
                    GrenadeModelSync syncComponent = replacedSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(replacedSyncObject);
                    }
                }
            }
            if (_activeDestroyerInstance != null)
            {
                Destroy(_activeDestroyerInstance);
                _activeDestroyerInstance = null;
            }
            _replacedGrenadeSyncObjects.Clear();

            _originalGrenadeModels.Clear();

            foreach (var kvp in _active500kgEffects)
            {
                var effects = kvp.Value;
                if (effects.bomb != null)
                {
                    Destroy(effects.bomb);
                }
                // 确保清理 500kg 炸弹关联的 replacedBallSyncObject
                if (effects.replacedBallSyncObject != null)
                {
                    GrenadeModelSync syncComponent = effects.replacedBallSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(effects.replacedBallSyncObject);
                    }
                }
            }
            _active500kgEffects.Clear();

            _explosionNormalFxPfb = null;
            _defaultBulletPfb = null; // 清理子弹预制体
            _destroyerPrefab = null; // 清理 Destroyer 预制体引用
            _modSounds_FMOD = null;
            _random = null;
            _pendingSkillName = null;
        }

        private IEnumerator EagleAirstrike(Vector3 originalExplosionPosition, DamageInfo originalDamageInfo, float explosionShakeStrength, bool canHurtSelf, float originalDamageRange, Vector3 playerThrowPosition, GameObject replacedBallSyncObject)
        {
            ExplosionManager explosionManager = LevelManager.Instance?.ExplosionManager;
            GameObject Beam = null;
            Vector3 beamEndPosition = originalExplosionPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            if (explosionManager == null)
            {
                yield break;
            }

            try
            {

                PlayRandomEagleSound();

                yield return new WaitForSeconds(3.3f);

                Vector3 directionToExplosion = beamEndPosition - playerThrowPosition;
                directionToExplosion.y = 0;
                if (directionToExplosion.sqrMagnitude == 0)
                {
                    directionToExplosion = Vector3.forward;
                }
                directionToExplosion.Normalize();

                Vector3 perpendicularDirection = Vector3.Cross(directionToExplosion, Vector3.up);
                perpendicularDirection.Normalize();

                if (perpendicularDirection.sqrMagnitude == 0)
                {
                    perpendicularDirection = Vector3.left;
                }

                float flyHeight = 10f;
                float flyDistance = 100f;
                Vector3 airplaneStartPos = beamEndPosition + perpendicularDirection * flyDistance + Vector3.up * flyHeight;
                Vector3 airplaneEndPos = beamEndPosition - perpendicularDirection * flyDistance + Vector3.up * flyHeight;

                GameObject airplaneGO = null;
                if (_assetManager != null)
                {
                    airplaneGO = _assetManager.CreateFromPath("Assets/Airborne/Eagle.prefab");
                    if (airplaneGO != null)
                    {
                        Scene activeScene = SceneManager.GetActiveScene();
                        if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, LevelManager.Instance.MainCharacter.gameObject.scene);
                        }
                        else if (activeScene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, activeScene);
                        }
                        else
                        {
                        }

                        SimpleAirPlane simpleAirPlane = airplaneGO.AddComponent<SimpleAirPlane>();
                        simpleAirPlane.BeginFly(airplaneStartPos, airplaneEndPos, 100f);

                    }
                    else
                    {
                    }
                }

                yield return new WaitForSeconds(0.7f);

                int numberOfExplosions = 5;
                float explosionSpacing = 3f;
                float secondaryExplosionRadius = originalDamageRange * 0.8f;

                DamageInfo customDamageInfo = new DamageInfo(originalDamageInfo.fromCharacter);
                customDamageInfo.damageValue = 50f;
                customDamageInfo.critDamageFactor = 1.5f;
                customDamageInfo.critRate = 0.2f;
                customDamageInfo.armorPiercing = 0.5f;
                customDamageInfo.isExplosion = true;
                customDamageInfo.damageType = DamageTypes.normal;
                customDamageInfo.buffChance = 0.3f;
                customDamageInfo.ignoreArmor = false;
                customDamageInfo.fromWeaponItemID = 0;

                float totalWidth = (numberOfExplosions - 1) * explosionSpacing;
                Vector3 startPosition = beamEndPosition + perpendicularDirection * (totalWidth / 2f);

                for (int i = 0; i < numberOfExplosions; i++)
                {
                    Vector3 currentExplosionPosition = startPosition - perpendicularDirection * (i * explosionSpacing);

                    explosionManager.CreateExplosion(
                        currentExplosionPosition,
                        secondaryExplosionRadius,
                        customDamageInfo,
                        ExplosionFxTypes.normal,
                        explosionShakeStrength * 0.5f,
                        canHurtSelf
                    );
                    if (_explosionNormalFxPfb != null)
                    {
                        UnityEngine.Object.Instantiate(_explosionNormalFxPfb, currentExplosionPosition, Quaternion.identity);
                    }


                    if (i < numberOfExplosions - 1)
                    {
                        yield return new WaitForSeconds(0.12f);
                    }
                }

            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }

                if (replacedBallSyncObject != null)
                {
                    GrenadeModelSync syncComponent = replacedBallSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(replacedBallSyncObject);
                    }
                }
            }
        }
        private IEnumerator _500kgBombSequence(Vector3 targetPosition, CharacterMainControl thrower, ExplosionManager explosionManager, GameObject passedBallSyncObject)
        {
            ExplosionFxScalerPatch.ApplyPatch();
            RaycastHit hitGround;
            Vector3 finalTargetPosition = targetPosition;
            GameObject Beam = null;
            Vector3 beamEndPosition = finalTargetPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            GameObject currentBomb = null;
            GameObject currentBallSyncObject = passedBallSyncObject;

            try
            {
                if (_modSounds_FMOD.TryGetValue("Eagle_500kg", out FMOD.Sound soundToPlay))
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
                }
                else
                {
                    PlayRandomEagleSound();
                }

                // 3. 3.6s后eagle飞机飞过
                yield return new WaitForSeconds(3.6f);

                Vector3 playerThrowPosition = thrower.transform.position;

                float flyHeight = 10f;
                float flyDistance = 80f;

                // 计算飞机飞行方向（沿玩家投射点至目标点的连线）
                Vector3 flightDirection = (targetPosition - playerThrowPosition);
                flightDirection.y = 0; // 忽略Y轴，只考虑水平方向
                if (flightDirection.sqrMagnitude == 0)
                {
                    flightDirection = Vector3.forward; // 如果在同一点，默认向前
                }
                flightDirection.Normalize();
                // 飞机的起始点和结束点将基于飞行方向和目标点
                Vector3 airplaneStartPos = targetPosition - flightDirection * flyDistance + Vector3.up * flyHeight;
                Vector3 airplaneEndPos = targetPosition + flightDirection * flyDistance + Vector3.up * flyHeight;



                GameObject airplaneGO = null;
                if (_assetManager != null)
                {
                    airplaneGO = _assetManager.CreateFromPath("Assets/Airborne/Eagle.prefab");
                    if (airplaneGO != null)
                    {
                        Scene activeScene = SceneManager.GetActiveScene();
                        if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, LevelManager.Instance.MainCharacter.gameObject.scene);
                        }
                        else if (activeScene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, activeScene);
                        }
                        else
                        {
                        }


                        SimpleAirPlane simpleAirPlane = airplaneGO.AddComponent<SimpleAirPlane>();
                        simpleAirPlane.BeginFly(airplaneStartPos, airplaneEndPos, 80f);

                    }
                    else
                    {
                    }
                }

                // 4. 从原地高处加载一个模型为bomb.prefab的素材落至光柱所在点后停住
                yield return new WaitForSeconds(0.5f);

                GameObject bombPrefabInstance = _assetManager.CreateFromPath("Assets/Airborne/bomb.prefab");
                if (bombPrefabInstance != null)
                {
                    // 计算飞机飞行方向
                    Vector3 airplaneFlyDirection = (airplaneEndPos - airplaneStartPos).normalized;
                    Vector3 startFallPosition = finalTargetPosition + Vector3.up * 30f + airplaneFlyDirection * -60f;
                    bombPrefabInstance.transform.position = startFallPosition;
                    // 将炸弹的Z轴（通常是模型的前方）对齐飞机的飞行方向
                    // Quaternion.LookRotation 会将物体的Z轴对准第一个参数，Y轴对准第二个参数（向上）
                    Quaternion targetRotation = Quaternion.LookRotation(airplaneFlyDirection, Vector3.up);
                    // 额外旋转，使炸弹向前倾斜
                    targetRotation *= Quaternion.Euler(120f, 0f, 0f); // 绕X轴旋转

                    bombPrefabInstance.transform.rotation = targetRotation; // 设置初始旋转

                    Scene activeScene = SceneManager.GetActiveScene();
                    if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(bombPrefabInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                    }
                    else if (activeScene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(bombPrefabInstance, activeScene);
                    }
                    else
                    {
                    }

                    foreach (Renderer r in bombPrefabInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = true;
                    }
                    bombPrefabInstance.SetActive(true);

                    // 动画控制
                    Rigidbody bombRb = bombPrefabInstance.GetComponent<Rigidbody>();
                    if (bombRb != null)
                    {
                        bombRb.isKinematic = true;
                        bombRb.useGravity = false;
                        Destroy(bombRb);
                    }

                    currentBomb = bombPrefabInstance;


                    // 动画下落逻辑
                    float fallDuration = 0.5f; // 炸弹下落动画持续时间
                    float elapsedTime = 0f;
                    Vector3 initialBombPosition = startFallPosition;
                    // 存储初始旋转，以便在下落过程中保持
                    Quaternion initialBombRotation = bombPrefabInstance.transform.rotation;

                    Vector3 fallDirection = (finalTargetPosition - initialBombPosition).normalized;

                    while (elapsedTime < fallDuration)
                    {
                        if (currentBomb == null) yield break; // 如果炸弹被销毁，则退出

                        float t = elapsedTime / fallDuration;

                        currentBomb.transform.position = Vector3.Lerp(initialBombPosition, finalTargetPosition, t * t);
                        // 确保在下落过程中保持初始旋转
                        currentBomb.transform.rotation = initialBombRotation;
                        elapsedTime += Time.deltaTime;
                        yield return null;
                    }

                    // 确保最终位置精确
                    if (currentBomb != null)
                    {
                        currentBomb.transform.position = finalTargetPosition;
                        // 确保最终位置也保持旋转
                        currentBomb.transform.rotation = initialBombRotation;
                    }
                    else
                    {
                        yield break;
                    }

                }
                else
                {
                    yield break;
                }

                // 5. 产生与当前一致的爆炸并且删除对应的bomb，ball以模型及光柱等
                yield return new WaitForSeconds(0.9f);

                if (explosionManager != null)
                {
                    DamageInfo bombDamageInfo = new DamageInfo(thrower);
                    bombDamageInfo.damageValue = 150f;
                    bombDamageInfo.critDamageFactor = 2.0f;
                    bombDamageInfo.critRate = 0.5f;
                    bombDamageInfo.armorPiercing = 1.0f;
                    bombDamageInfo.isExplosion = true;
                    bombDamageInfo.damageType = DamageTypes.normal;
                    bombDamageInfo.buffChance = 0.5f;
                    bombDamageInfo.ignoreArmor = true;
                    bombDamageInfo.fromWeaponItemID = 0;

                    float bombRadius = 10f;
                    float bombShakeStrength = 5f;

                    explosionManager.CreateExplosion(
                        finalTargetPosition,
                        bombRadius,
                        bombDamageInfo,
                        ExplosionFxTypes.normal,
                        bombShakeStrength,
                        true
                    );


                    if (currentBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(currentBallSyncObject);
                        }
                    }
                }
                else
                {
                }
            }
            finally
            {

                if (currentBomb != null)
                {
                    Destroy(currentBomb);
                }

                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }

                _active500kgEffects.Remove(finalTargetPosition);
            }
            yield return new WaitForSeconds(3f);
            ExplosionFxScalerPatch.RemovePatch();
        }

        // 飞鹰机枪扫射技能
        private IEnumerator _EagleStrafingSequence(Vector3 targetPosition, CharacterMainControl thrower, Vector3 playerThrowPosition, GameObject passedBallSyncObject)
        {

            Projectile bulletPrefab = _defaultBulletPfb; // 使用默认子弹预制体

            GameObject Beam = null;
            Vector3 beamEndPosition = targetPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            GameObject currentBallSyncObject = passedBallSyncObject;
            Vector3 BallLocation = currentBallSyncObject.transform.position;
            try
            {
                if (_modSounds_FMOD.TryGetValue("Eagle_Strafing", out FMOD.Sound soundToPlay))
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
                }
                else
                {
                    PlayRandomEagleSound();
                }


                yield return new WaitForSeconds(2.0f); // 预备时间

                // 计算飞机飞行方向（沿玩家投射点至目标点的连线）
                Vector3 flightDirection = (targetPosition - playerThrowPosition);
                flightDirection.y = 0; // 忽略Y轴，只考虑水平方向
                if (flightDirection.sqrMagnitude == 0)
                {
                    flightDirection = Vector3.forward; // 如果在同一点，默认向前
                }
                flightDirection.Normalize();

                float flyHeight = 15f; 
                float flyDistance = 100f; 

                // 飞机的起始点和结束点将基于飞行方向和目标点
                Vector3 airplaneStartPos = targetPosition - flightDirection * flyDistance + Vector3.up * flyHeight;
                Vector3 airplaneEndPos = targetPosition + flightDirection * flyDistance + Vector3.up * flyHeight;

                GameObject airplaneGO = null;
                if (_assetManager != null)
                {
                    airplaneGO = _assetManager.CreateFromPath("Assets/Airborne/Eagle.prefab");
                    if (airplaneGO != null)
                    {
                        Scene activeScene = SceneManager.GetActiveScene();
                        if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, LevelManager.Instance.MainCharacter.gameObject.scene);
                        }
                        else if (activeScene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, activeScene);
                        }
                        else
                        {
                        }

                        SimpleAirPlane simpleAirPlane = airplaneGO.AddComponent<SimpleAirPlane>();
                        simpleAirPlane.BeginFly(airplaneStartPos, airplaneEndPos, 120f); // 飞行速度
                    }
                    else
                    {
                        // 即使飞机加载失败，也要继续清理
                        if (currentBallSyncObject != null)
                        {
                            GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                            if (syncComponent != null)
                            {
                                syncComponent.DestroyReplacedModel();
                            }
                            else
                            {
                                Destroy(currentBallSyncObject);
                            }
                        }
                        yield break;
                    }
                }

                // 飞机飞行过程中扫射
                float strafeDuration = Vector3.Distance(airplaneStartPos, airplaneEndPos) / 120f; // 根据飞行速度计算扫射持续时间
                float bulletFireDelay = strafeDuration / 120f; // 每轮30发，总共60发                                                              
                float currentFlyTime = 0f;

                CharacterMainControl localPlayer = LevelManager.Instance.MainCharacter; // 获取本地玩家角色

                currentFlyTime = 0f;
                while (currentFlyTime < strafeDuration)
                {
                    if (airplaneGO == null) yield break; // 如果飞机在飞行中被销毁，则退出

                    Vector3 currentAirplanePos = Vector3.Lerp(airplaneStartPos, airplaneEndPos, currentFlyTime / strafeDuration);
                    airplaneGO.transform.position = currentAirplanePos; // 更新飞机位置
                    airplaneGO.transform.LookAt(currentAirplanePos + flightDirection); // 让飞机面向飞行方向

                    Vector3 currentStrafeTarget = currentAirplanePos - Vector3.up * flyHeight; // 飞机的地面投影点
                    currentStrafeTarget += flightDirection * 40f;                                  

                    // 计算左右偏移量
                    Vector3 rightOffset = Vector3.Cross(flightDirection, Vector3.up).normalized * 1.5f; // 1.5f 偏移距离

                    // 发射左侧子弹
                    ShootBulletFromPlane(currentAirplanePos + rightOffset, currentStrafeTarget, bulletPrefab, thrower, flightDirection);
                    ShootBulletFromPlane(currentAirplanePos + rightOffset/2, currentStrafeTarget, bulletPrefab, thrower, flightDirection);
                    // 发射右侧子弹
                    ShootBulletFromPlane(currentAirplanePos - rightOffset, currentStrafeTarget, bulletPrefab, thrower, flightDirection);
                    ShootBulletFromPlane(currentAirplanePos - rightOffset/2, currentStrafeTarget, bulletPrefab, thrower, flightDirection);
                    currentFlyTime += bulletFireDelay;
                    yield return new WaitForSeconds(bulletFireDelay);
                }

            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }

                if (currentBallSyncObject != null)
                {
                    GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(currentBallSyncObject);
                    }
                }
            }
        }

        private IEnumerator _EagleNapalm(Vector3 originalExplosionPosition, DamageInfo originalDamageInfo, float explosionShakeStrength, bool canHurtSelf, float originalDamageRange, Vector3 playerThrowPosition, GameObject replacedBallSyncObject)
        {

            ExplosionManager explosionManager = LevelManager.Instance?.ExplosionManager;
            if (explosionManager == null)
            {
                yield break;
            }

            // 确保 fireGrenadePrefabRef 已经初始化
            if (fireGrenadePrefabRef == null)
            {
                // 尝试等待并重新初始化，以防万一
                yield return InitializeAllGrenadePrefabsDelayed();
                if (fireGrenadePrefabRef == null)
                {
                    // 清理替换的球模型
                    if (replacedBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = replacedBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(replacedBallSyncObject);
                        }
                    }
                    yield break;
                }
            }

            Vector3 beamEndPosition = originalExplosionPosition;
            GameObject Beam = null;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;

            if (_modSounds_FMOD.TryGetValue("Eagle_Napalm", out FMOD.Sound soundToPlay))
            {
                FMOD.ChannelGroup masterSfxGroup;
                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                FMOD.Channel channel = default(FMOD.Channel);
                RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
            }
            else
            {
            }

            try
            {

                yield return new WaitForSeconds(3.3f);

                Vector3 directionToStrike = beamEndPosition - playerThrowPosition;
                directionToStrike.y = 0;
                if (directionToStrike.sqrMagnitude == 0)
                {
                    directionToStrike = Vector3.forward;
                }
                directionToStrike.Normalize();

                Vector3 perpendicularDirection = Vector3.Cross(directionToStrike, Vector3.up);
                perpendicularDirection.Normalize();

                if (perpendicularDirection.sqrMagnitude == 0)
                {
                    perpendicularDirection = Vector3.left;
                }

                float flyHeight = 10f;
                float flyDistance = 80f;
                Vector3 airplaneStartPos = beamEndPosition + perpendicularDirection * flyDistance + Vector3.up * flyHeight;
                Vector3 airplaneEndPos = beamEndPosition - perpendicularDirection * flyDistance + Vector3.up * flyHeight;

                GameObject airplaneGO = null;
                if (_assetManager != null)
                {
                    airplaneGO = _assetManager.CreateFromPath("Assets/Airborne/Eagle.prefab");
                    if (airplaneGO != null)
                    {
                        Scene activeScene = SceneManager.GetActiveScene();
                        if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, LevelManager.Instance.MainCharacter.gameObject.scene);
                        }
                        else if (activeScene.isLoaded)
                        {
                            SceneManager.MoveGameObjectToScene(airplaneGO, activeScene);
                        }
                        else
                        {
                        }

                        SimpleAirPlane simpleAirPlane = airplaneGO.AddComponent<SimpleAirPlane>();
                        simpleAirPlane.BeginFly(airplaneStartPos, airplaneEndPos, 80f);

                    }
                    else
                    {
                    }
                }

                yield return new WaitForSeconds(0.7f);

                int numberOfFireZones = 5;
                float fireZoneSpacing = 3f;

                float totalWidth = (numberOfFireZones - 1) * fireZoneSpacing;
                Vector3 startPosition = beamEndPosition + perpendicularDirection * (totalWidth / 2f);

                for (int i = 0; i < numberOfFireZones; i++)
                {
                    Vector3 currentFireZonePosition = startPosition - perpendicularDirection * (i * fireZoneSpacing);

                    // 放置 firegrenadezone
                    if (fireGrenadePrefabRef != null)
                    {
                        UnityEngine.Object.Instantiate(fireGrenadePrefabRef, currentFireZonePosition, Quaternion.identity);
                    }
                    else
                    {
                    }

                    // 制造一些小爆炸效果来模拟凝固汽油弹的冲击
                    explosionManager.CreateExplosion(
                        currentFireZonePosition,
                        originalDamageRange * 0.5f, // 较小的爆炸半径
                        originalDamageInfo, // 可以复用原始伤害信息，或者自定义
                        ExplosionFxTypes.normal, // 仍然使用普通爆炸特效
                        explosionShakeStrength * 0.3f, // 较小的震动强度
                        canHurtSelf
                    );
                    if (_explosionNormalFxPfb != null)
                    {
                        UnityEngine.Object.Instantiate(_explosionNormalFxPfb, currentFireZonePosition, Quaternion.identity);
                    }

                    if (i < numberOfFireZones - 1)
                    {
                        yield return new WaitForSeconds(0.12f);
                    }
                }
            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }
                // 在技能结束后清理 replacedBallSyncObject
                if (replacedBallSyncObject != null)
                {
                    GrenadeModelSync syncComponent = replacedBallSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(replacedBallSyncObject);
                    }
                }
            }
        }
        private IEnumerator _OrbitalPrecisionStrike(Vector3 destroyerPosition, Vector3 targetGroundPosition, CharacterMainControl thrower, ExplosionManager explosionManager, GameObject passedBallSyncObject)
        {
            ExplosionFxScalerPatch.ApplyPatch(); // 确保爆炸特效缩放补丁已应用

            GameObject currentBombInstance = null;
            GameObject currentBallSyncObject = passedBallSyncObject;

            GameObject Beam = null;
            Vector3 beamEndPosition = targetGroundPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            try
            {

                // 播放轨道攻击开始音效
                if (_modSounds_FMOD.TryGetValue("Orbital_Strike", out FMOD.Sound soundToPlay))
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);

                }
                else
                {
                }

                yield return new WaitForSeconds(3.2f); // 预备时间

                // 2. 从 Destroyer 位置向目标点发射 bomb.prefab
                currentBombInstance = _assetManager.CreateFromPath("Assets/Airborne/bomb.prefab");
                if (currentBombInstance != null)
                {
                    currentBombInstance.transform.position = destroyerPosition;

                    // 计算从 Destroyer 到目标点的方向
                    Vector3 bombFlightDirection = (targetGroundPosition - destroyerPosition).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(bombFlightDirection, Vector3.up);
                    targetRotation *= Quaternion.Euler(90f, 0f, 0f);
                    currentBombInstance.transform.rotation = targetRotation; // 设置初始旋转

                    Scene activeScene = SceneManager.GetActiveScene();
                    if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                    }
                    else if (activeScene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, activeScene);
                    }
                    else
                    {
                    }

                    // 确保渲染器可见
                    foreach (Renderer r in currentBombInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = true;
                    }
                    currentBombInstance.SetActive(true);

                    // 移除 Rigidbody 以便手动控制飞行
                    Rigidbody bombRb = currentBombInstance.GetComponent<Rigidbody>();
                    if (bombRb != null)
                    {
                        Destroy(bombRb);
                    }

                    float flightDuration = Vector3.Distance(destroyerPosition, targetGroundPosition) / 120f; // 假设飞行速度为 100m/s
                    float elapsedTime = 0f;
                    Vector3 initialBombPosition = destroyerPosition;
                    Quaternion initialBombRotation = currentBombInstance.transform.rotation; // 保持初始倾斜

                    while (elapsedTime < flightDuration)
                    {
                        if (currentBombInstance == null) yield break; // 如果炸弹被销毁，则退出

                        float t = elapsedTime / flightDuration;
                        currentBombInstance.transform.position = Vector3.Lerp(initialBombPosition, targetGroundPosition, t);
                        // 在飞行过程中保持初始的倾斜旋转
                        currentBombInstance.transform.rotation = initialBombRotation;

                        elapsedTime += Time.deltaTime;
                        yield return null;
                    }

                    // 确保最终位置精确
                    if (currentBombInstance != null)
                    {
                        currentBombInstance.transform.position = targetGroundPosition;
                        currentBombInstance.transform.rotation = initialBombRotation; // 保持最终旋转
                    }
                }
                else
                {
                    yield break;
                }

                // 3. 炸弹落地瞬间模型消失并产生爆炸

                if (currentBombInstance != null)
                {
                    Destroy(currentBombInstance); // 模型消失
                    currentBombInstance = null; // 清空引用
                }

                if (explosionManager != null)
                {
                    DamageInfo bombDamageInfo = new DamageInfo(thrower);
                    bombDamageInfo.damageValue = 100f; // 更高的伤害
                    bombDamageInfo.critDamageFactor = 2.5f;
                    bombDamageInfo.critRate = 0.6f;
                    bombDamageInfo.armorPiercing = 1.0f;
                    bombDamageInfo.isExplosion = true;
                    bombDamageInfo.damageType = DamageTypes.normal;
                    bombDamageInfo.buffChance = 0.8f;
                    bombDamageInfo.ignoreArmor = true;
                    bombDamageInfo.fromWeaponItemID = 0;

                    float bombRadius = 10f; // 更大的爆炸范围
                    float bombShakeStrength = 7f;

                    explosionManager.CreateExplosion(
                        targetGroundPosition,
                        bombRadius,
                        bombDamageInfo,
                        ExplosionFxTypes.normal, // 可以自定义特效类型
                        bombShakeStrength,
                        true
                    );


                    // 额外生成爆炸特效
                    if (_explosionNormalFxPfb != null)
                    {
                        UnityEngine.Object.Instantiate(_explosionNormalFxPfb, targetGroundPosition, Quaternion.identity);
                    }

                    // 清理替换的球模型
                    if (currentBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(currentBallSyncObject);
                        }
                    }
                }
                else
                {
                }
            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }
            }
            yield return new WaitForSeconds(3f);
            ExplosionFxScalerPatch.RemovePatch();
        }

        private IEnumerator Orbital380MMBarrage(Vector3 destroyerPosition, Vector3 targetGroundPosition, CharacterMainControl thrower, ExplosionManager explosionManager, GameObject passedBallSyncObject)
        {
            ExplosionFxScalerPatch.ApplyPatch(); // 确保爆炸特效缩放补丁已应用
            GameObject currentBallSyncObject = passedBallSyncObject;
            GameObject Beam = null;
            Vector3 beamEndPosition = targetGroundPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            try
            {

                // 播放 Orbital_380MM.wav 音效
                if (_modSounds_FMOD.TryGetValue("Orbital_380MM", out FMOD.Sound soundToPlay))
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);

                }
                else
                {
                    // 如果使用FMOD事件，可以使用RuntimeManager.PlayOneShot
                    RuntimeManager.PlayOneShot("event:/Sounds/Orbital_380MM", targetGroundPosition); // 假设路径
                }

                // 7s 后开始轰炸
                yield return new WaitForSeconds(7.0f);


                // 3发为一轮，重复6轮，共18发
                for (int round = 0; round < 6; round++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        yield return new WaitForSeconds(0.5f);
                        Vector3 bombTargetPosition = Quaternion.Euler(_random.Next(-5, 5), _random.Next(-5, 5), 0) * targetGroundPosition;       // 在目标点20m半径内随机一个轰炸位置
                        if (i == 0)
                        {
                            // 播放 Orbital_380MM_Bomb.wav 音效
                            if (_modSounds_FMOD.TryGetValue("Orbital_380MM_Bomb", out FMOD.Sound bombSoundToPlay))
                            {
                                FMOD.ChannelGroup masterSfxGroup;
                                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                                FMOD.Channel channel = default(FMOD.Channel);
                                float desiredVolume = 1.5f;
                                channel.setVolume(desiredVolume);
                                RuntimeManager.CoreSystem.playSound(bombSoundToPlay, masterSfxGroup, false, out channel);
                            }

                        }
                        GameObject currentBombInstance = null;

                        // 2. 从 Destroyer 位置向目标点发射 bomb.prefab
                        currentBombInstance = _assetManager.CreateFromPath("Assets/Airborne/bomb.prefab");
                        if (currentBombInstance != null)
                        {
                            currentBombInstance.transform.position = destroyerPosition;

                            // 计算从 Destroyer 到目标点的方向
                            Vector3 bombFlightDirection = (bombTargetPosition - destroyerPosition).normalized;
                            Quaternion targetRotation = Quaternion.LookRotation(bombFlightDirection, Vector3.up);
                            targetRotation *= Quaternion.Euler(90f, 0f, 0f);
                            currentBombInstance.transform.rotation = targetRotation; // 设置初始旋转

                            Scene activeScene = SceneManager.GetActiveScene();
                            if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                            {
                                SceneManager.MoveGameObjectToScene(currentBombInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                            }
                            else if (activeScene.isLoaded)
                            {
                                SceneManager.MoveGameObjectToScene(currentBombInstance, activeScene);
                            }
                            else
                            {
                            }

                            // 确保渲染器可见
                            foreach (Renderer r in currentBombInstance.GetComponentsInChildren<Renderer>(true))
                            {
                                r.enabled = true;
                            }
                            currentBombInstance.SetActive(true);

                            // 移除 Rigidbody 以便手动控制飞行
                            Rigidbody bombRb = currentBombInstance.GetComponent<Rigidbody>();
                            if (bombRb != null)
                            {
                                Destroy(bombRb);
                            }

                            float fixedFlightDuration = 0.5f; // 固定飞行时间
                            float elapsedTime = 0f;
                            Vector3 initialBombPosition = destroyerPosition;
                            Quaternion initialBombRotation = currentBombInstance.transform.rotation; // 保持初始倾斜
                            float flightDistance = Vector3.Distance(destroyerPosition, bombTargetPosition); // 计算总飞行距离

                            while (elapsedTime < fixedFlightDuration)
                            {
                                if (currentBombInstance == null) yield break; // 如果炸弹被销毁，则退出

                                float t = elapsedTime / fixedFlightDuration;

                                // 根据t值和总距离，计算当前帧需要移动的距离比例
                                // Vector3.Lerp 会在固定的时间t内，根据t从起点到终点插值
                                currentBombInstance.transform.position = Vector3.Lerp(initialBombPosition, bombTargetPosition, t);

                                // 在飞行过程中保持初始的倾斜旋转
                                currentBombInstance.transform.rotation = initialBombRotation;

                                elapsedTime += Time.deltaTime;
                                yield return null;
                            }

                            // 确保最终位置精确
                            if (currentBombInstance != null)
                            {
                                currentBombInstance.transform.position = bombTargetPosition;
                                currentBombInstance.transform.rotation = initialBombRotation; // 保持最终旋转
                            }
                        }
                        else
                        {
                            yield break;
                        }

                        if (currentBombInstance != null)
                        {
                            Destroy(currentBombInstance); // 模型消失
                            currentBombInstance = null; // 清空引用
                        }

                        if (explosionManager != null)
                        {
                            DamageInfo bombDamageInfo = new DamageInfo(thrower);
                            bombDamageInfo.damageValue = 100f; // 380MM 
                            bombDamageInfo.critDamageFactor = 3.0f;
                            bombDamageInfo.critRate = 0.7f;
                            bombDamageInfo.armorPiercing = 1.0f;
                            bombDamageInfo.isExplosion = true;
                            bombDamageInfo.damageType = DamageTypes.normal;
                            bombDamageInfo.buffChance = 0.9f;
                            bombDamageInfo.ignoreArmor = true;
                            bombDamageInfo.fromWeaponItemID = 0;

                            float bombRadius = 12f;
                            float bombShakeStrength = 7f;

                            explosionManager.CreateExplosion(
                                bombTargetPosition,
                                bombRadius,
                                bombDamageInfo,
                                ExplosionFxTypes.normal,
                                bombShakeStrength,
                                true
                            );

                            if (_explosionNormalFxPfb != null)
                            {
                                UnityEngine.Object.Instantiate(_explosionNormalFxPfb, bombTargetPosition, Quaternion.identity);
                            }
                        }
                        else
                        {
                        }

                        // 根据轰炸顺序等待
                        if (i != 2)
                        {
                            yield return new WaitForSeconds(0.6f);
                        }

                    }
                    yield return new WaitForSeconds(0.5f);
                }
                if (currentBallSyncObject != null)
                {
                    GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                    if (syncComponent != null)
                    {
                        syncComponent.DestroyReplacedModel();
                    }
                    else
                    {
                        Destroy(currentBallSyncObject);
                    }
                }
            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }
            }
            yield return new WaitForSeconds(3f); // 轰炸结束后等待一段时间清理
            ExplosionFxScalerPatch.RemovePatch();
        }

        private IEnumerator _OrbitalGasStrike(Vector3 destroyerPosition, Vector3 targetGroundPosition, CharacterMainControl thrower, ExplosionManager explosionManager, GameObject passedBallSyncObject)
        {
            ExplosionFxScalerPatch.ApplyPatch(); // 特效缩放补丁应用
            GameObject currentBombInstance = null;
            GameObject Beam = null;
            GameObject currentBallSyncObject = passedBallSyncObject;
            try
            {
                // 1. 目标点生成光柱
                Vector3 beamEndPosition = targetGroundPosition + Vector3.up * 0.1f;
                Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
                Beam.transform.position = beamEndPosition;

                // 播放轨道攻击开始音效
                if (_modSounds_FMOD.TryGetValue("Orbital_Gas", out FMOD.Sound soundToPlay)) // 假设有一个名为 "orbital_strike_sound.wav" 的音效
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
                }
                else
                {
                }

                yield return new WaitForSeconds(3.2f); // 预备时间

                // 2. 从 Destroyer 位置向目标点发射 bomb.prefab
                currentBombInstance = _assetManager.CreateFromPath("Assets/Airborne/bomb.prefab");
                if (currentBombInstance != null)
                {
                    currentBombInstance.transform.position = destroyerPosition;

                    // 计算从 Destroyer 到目标点的方向
                    Vector3 bombFlightDirection = (targetGroundPosition - destroyerPosition).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(bombFlightDirection, Vector3.up);
                    targetRotation *= Quaternion.Euler(90f, 0f, 0f);
                    currentBombInstance.transform.rotation = targetRotation; // 设置初始旋转

                    Scene activeScene = SceneManager.GetActiveScene();
                    if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                    }
                    else if (activeScene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, activeScene);
                    }
                    else
                    {
                    }

                    // 确保渲染器可见
                    foreach (Renderer r in currentBombInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = true;
                    }
                    currentBombInstance.SetActive(true);

                    // 移除 Rigidbody 以便手动控制飞行
                    Rigidbody bombRb = currentBombInstance.GetComponent<Rigidbody>();
                    if (bombRb != null)
                    {
                        Destroy(bombRb);
                    }

                    float flightDuration = Vector3.Distance(destroyerPosition, targetGroundPosition) / 120f; // 飞行速度
                    float elapsedTime = 0f;
                    Vector3 initialBombPosition = destroyerPosition;
                    Quaternion initialBombRotation = currentBombInstance.transform.rotation; // 保持初始倾斜

                    while (elapsedTime < flightDuration)
                    {
                        if (currentBombInstance == null) yield break; // 如果炸弹被销毁，则退出

                        float t = elapsedTime / flightDuration;
                        currentBombInstance.transform.position = Vector3.Lerp(initialBombPosition, targetGroundPosition, t);
                        // 在飞行过程中保持初始的倾斜旋转
                        currentBombInstance.transform.rotation = initialBombRotation;

                        elapsedTime += Time.deltaTime;
                        yield return null;
                    }

                    // 确保最终位置精确
                    if (currentBombInstance != null)
                    {
                        currentBombInstance.transform.position = targetGroundPosition;
                        currentBombInstance.transform.rotation = initialBombRotation; // 保持最终旋转
                    }
                }
                else
                {
                    yield break;
                }

                // 3. 炸弹落地瞬间模型消失并产生爆炸

                if (currentBombInstance != null)
                {
                    Destroy(currentBombInstance); // 模型消失
                    currentBombInstance = null; // 清空引用
                }

                if (poisonGrenadePrefabRef != null)
                {
                    UnityEngine.Object.Instantiate(poisonGrenadePrefabRef, targetGroundPosition, Quaternion.identity);
                    // 清理替换的球模型
                    if (currentBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(currentBallSyncObject);
                        }
                    }
                }
                else
                {
                }
            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }
            }
            yield return new WaitForSeconds(3f);
            ExplosionFxScalerPatch.RemovePatch();
        }

        private IEnumerator _OrbitalSmokeStrike(Vector3 destroyerPosition, Vector3 targetGroundPosition, CharacterMainControl thrower, ExplosionManager explosionManager, GameObject passedBallSyncObject)
        {
            ExplosionFxScalerPatch.ApplyPatch(); // 确保爆炸特效缩放补丁已应用
            GameObject currentBombInstance = null;
            GameObject currentBallSyncObject = passedBallSyncObject;
            GameObject Beam = null;
            Vector3 beamEndPosition = targetGroundPosition + Vector3.up * 0.1f;
            Beam = _assetManager.CreateFromPath("Assets/Airborne/beam.prefab");
            Beam.transform.position = beamEndPosition;
            try
            {

                // 播放轨道攻击开始音效
                if (_modSounds_FMOD.TryGetValue("Orbital_Smoke", out FMOD.Sound soundToPlay)) // 假设有一个名为 "orbital_strike_sound.wav" 的音效
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
                }
                else
                {
                }

                yield return new WaitForSeconds(3.2f); // 预备时间

                // 2. 从 Destroyer 位置向目标点发射 bomb.prefab
                currentBombInstance = _assetManager.CreateFromPath("Assets/Airborne/bomb.prefab");
                if (currentBombInstance != null)
                {
                    currentBombInstance.transform.position = destroyerPosition;

                    // 计算从 Destroyer 到目标点的方向
                    Vector3 bombFlightDirection = (targetGroundPosition - destroyerPosition).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(bombFlightDirection, Vector3.up);
                    targetRotation *= Quaternion.Euler(90f, 0f, 0f);
                    currentBombInstance.transform.rotation = targetRotation; // 设置初始旋转

                    Scene activeScene = SceneManager.GetActiveScene();
                    if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, LevelManager.Instance.MainCharacter.gameObject.scene);
                    }
                    else if (activeScene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(currentBombInstance, activeScene);
                    }
                    else
                    {
                    }

                    // 确保渲染器可见
                    foreach (Renderer r in currentBombInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = true;
                    }
                    currentBombInstance.SetActive(true);

                    // 移除 Rigidbody 以便手动控制飞行
                    Rigidbody bombRb = currentBombInstance.GetComponent<Rigidbody>();
                    if (bombRb != null)
                    {
                        Destroy(bombRb);
                    }

                    float flightDuration = Vector3.Distance(destroyerPosition, targetGroundPosition) / 120f;
                    float elapsedTime = 0f;
                    Vector3 initialBombPosition = destroyerPosition;
                    Quaternion initialBombRotation = currentBombInstance.transform.rotation; // 保持初始倾斜

                    while (elapsedTime < flightDuration)
                    {
                        if (currentBombInstance == null) yield break; // 如果炸弹被销毁，则退出

                        float t = elapsedTime / flightDuration;
                        currentBombInstance.transform.position = Vector3.Lerp(initialBombPosition, targetGroundPosition, t);
                        // 在飞行过程中保持初始的倾斜旋转
                        currentBombInstance.transform.rotation = initialBombRotation;

                        elapsedTime += Time.deltaTime;
                        yield return null;
                    }

                    // 确保最终位置精确
                    if (currentBombInstance != null)
                    {
                        currentBombInstance.transform.position = targetGroundPosition;
                        currentBombInstance.transform.rotation = initialBombRotation; // 保持最终旋转
                    }
                }
                else
                {
                    yield break;
                }

                // 3. 炸弹落地瞬间模型消失并产生爆炸

                if (currentBombInstance != null)
                {
                    Destroy(currentBombInstance); // 模型消失
                    currentBombInstance = null; // 清空引用
                }

                if (smokeGrenadePrefabRef != null)
                {
                    UnityEngine.Object.Instantiate(smokeGrenadePrefabRef, targetGroundPosition, Quaternion.identity);
                    // 清理替换的球模型
                    if (currentBallSyncObject != null)
                    {
                        GrenadeModelSync syncComponent = currentBallSyncObject.GetComponent<GrenadeModelSync>();
                        if (syncComponent != null)
                        {
                            syncComponent.DestroyReplacedModel();
                        }
                        else
                        {
                            Destroy(currentBallSyncObject);
                        }
                    }
                }
            }
            finally
            {
                if (Beam != null)
                {
                    Destroy(Beam); // 模型消失
                    Beam = null; // 清空引用
                }
            }
            yield return new WaitForSeconds(3f);
            ExplosionFxScalerPatch.RemovePatch();
        }

        // 从飞机发射子弹方法
        private void ShootBulletFromPlane(Vector3 airplanePosition, Vector3 targetGroundPosition, Projectile bulletPfb, CharacterMainControl fromCharacter, Vector3 flightDirection)
        {
            if (LevelManager.Instance?.BulletPool == null || bulletPfb == null)
            {
                return;
            }

            Projectile projectileInstance = LevelManager.Instance.BulletPool.GetABullet(bulletPfb);
            if (projectileInstance == null)
            {
                return;
            }

            // 子弹的起始位置在飞机下方一点，模拟从飞机射出
            Vector3 startBulletPosition = airplanePosition + Vector3.down * 1.5f + flightDirection * 3f;
            projectileInstance.transform.position = startBulletPosition;

            // 计算子弹射向地面的方向，稍微向下倾斜
            Vector3 shootDirection = (targetGroundPosition - startBulletPosition).normalized;
            // 增加一些随机散布
            shootDirection = Quaternion.Euler(_random.Next(-3, 3), _random.Next(-3, 3), 0) * shootDirection;
            shootDirection.Normalize();

            projectileInstance.transform.rotation = Quaternion.LookRotation(shootDirection, Vector3.up);

            ProjectileContext projectileContext = default(ProjectileContext);
            projectileContext.firstFrameCheck = true;
            projectileContext.firstFrameCheckStartPoint = startBulletPosition; // 或者更靠近飞机
            projectileContext.direction = shootDirection;
            projectileContext.speed = 300f; // 子弹速度
            projectileContext.team = Teams.player;
            projectileContext.distance = 100f; // 子弹射程
            projectileContext.halfDamageDistance = projectileContext.distance * 0.5f;
            projectileContext.penetrate = 5; // 穿透
            projectileContext.damage = 40f; // 单发子弹伤害
            projectileContext.critDamageFactor = 1.0f;
            projectileContext.critRate = 0.1f;
            projectileContext.armorPiercing = 0.2f;
            projectileContext.armorBreak = 0.1f;
            projectileContext.fromCharacter = fromCharacter;
            projectileContext.explosionRange = 0f;
            projectileContext.explosionDamage = 0f;
            projectileContext.element_Physics = 1f; // 物理伤害
            projectileContext.fromWeaponItemID = 0; // 没有特定武器ID
            projectileContext.ignoreHalfObsticle = false;
            projectileInstance.Init(projectileContext);
        }

        private IEnumerator GrenadeDetectionCoroutine()
        {
            yield return null;
            yield return new WaitUntil(() => LevelManager.Instance != null && LevelManager.Instance.ExplosionManager != null);

            if (LevelManager.Instance.ExplosionManager != null)
            {
                _explosionNormalFxPfb = LevelManager.Instance.ExplosionManager.normalFxPfb;
            }
            else
            {
            }

            while (true)
            {
                yield return new WaitForSeconds(0.3f);

                Grenade[] allGrenades = FindObjectsOfType<Grenade>();

                foreach (Grenade grenade in allGrenades)
                {
                    if (!_processedGrenades.Contains(grenade))
                    {
                        if (grenade.damageInfo.fromWeaponItemID == TARGET_ITEM_ID)
                        {
                            if (!string.IsNullOrEmpty(_pendingSkillName))
                            {
                                GameObject replacedModelSyncObject = HideAndReplaceGrenadeModel(grenade);
                                if (replacedModelSyncObject != null)
                                {
                                    _replacedGrenadeSyncObjects[grenade] = replacedModelSyncObject; // 存储替换模型以便后续清理
                                }

                                if (grenade.onExplodeEvent != null)
                                {
                                    CharacterMainControl throwerCharacter = null;
                                    if (grenade.damageInfo.fromCharacter != null)
                                    {
                                        throwerCharacter = grenade.damageInfo.fromCharacter;
                                    }

                                    string skillToExecute = _pendingSkillName;
                                    _pendingSkillName = null;

                                    GameObject capturedReplacedModelSyncObject = replacedModelSyncObject;

                                    grenade.onExplodeEvent.AddListener(() =>
                                    {
                                        Vector3 actualExplosionPosition = grenade.transform.position;
                                        RaycastHit hitExplosion;
                                        if (Physics.Raycast(grenade.transform.position + Vector3.up * 5f, Vector3.down, out hitExplosion, 100f, LayerMask.GetMask("Ground", "Default", "Terrain")))
                                        {
                                            actualExplosionPosition = hitExplosion.point;
                                            actualExplosionPosition.y += 0.01f; 
                                        }
                                        else
                                        {
                                            if (actualExplosionPosition.y < 0) actualExplosionPosition.y = 0.01f;
                                            actualExplosionPosition.y += 0.01f; 
                                        }


                                        ExecuteSkill(
                                            skillToExecute,
                                            actualExplosionPosition,
                                            grenade.damageInfo,
                                            grenade.explosionShakeStrength,
                                            true,
                                            grenade.damageRange,
                                            throwerCharacter != null ? throwerCharacter.transform.position : actualExplosionPosition,
                                            capturedReplacedModelSyncObject // 传递 ballSyncObject
                                        );

                                        if (_originalGrenadeModels.ContainsKey(grenade) && _originalGrenadeModels[grenade] != null)
                                        {
                                            _originalGrenadeModels[grenade].SetActive(false);
                                        }

                                        _originalGrenadeModels.Remove(grenade);
                                        _replacedGrenadeSyncObjects.Remove(grenade);
                                    });
                                    _processedGrenades.Add(grenade);
                                }
                                else
                                {
                                    if (replacedModelSyncObject != null)
                                    {
                                        GrenadeModelSync syncComponent = replacedModelSyncObject.GetComponent<GrenadeModelSync>();
                                        if (syncComponent != null)
                                        {
                                            syncComponent.DestroyReplacedModel();
                                        }
                                        else
                                        {
                                            Destroy(replacedModelSyncObject);
                                        }
                                    }
                                    _processedGrenades.Add(grenade);
                                }
                            }
                            else
                            {
                                _processedGrenades.Add(grenade);
                            }
                        }
                        else
                        {
                            _processedGrenades.Add(grenade);
                        }
                    }
                }
                _processedGrenades.RemoveWhere(g => g == null);
            }
        }
        private GameObject HideAndReplaceGrenadeModel(Grenade grenade)
        {
            GameObject originalGrenadeGO = grenade.gameObject;

            foreach (MeshRenderer mr in originalGrenadeGO.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.enabled = false;
            }
            foreach (SkinnedMeshRenderer smr in originalGrenadeGO.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = false;
            }
            _originalGrenadeModels[grenade] = originalGrenadeGO;

            GameObject ballPrefabInstance = _assetManager.CreateFromPath("Assets/Airborne/ball.prefab");
            if (ballPrefabInstance != null)
            {
                GameObject syncObject = new GameObject($"ReplacedGrenadeSync_{grenade.GetInstanceID()}"); 

                // 设置 syncObject 的初始位置，使其与手雷位置一致
                syncObject.transform.position = grenade.transform.position;
                syncObject.transform.rotation = grenade.transform.rotation;

                GrenadeModelSync grenadeModelSync = syncObject.AddComponent<GrenadeModelSync>();
                grenadeModelSync.Initialize(grenade.transform, ballPrefabInstance);


                Scene activeScene = SceneManager.GetActiveScene();
                if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.gameObject.scene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(syncObject, LevelManager.Instance.MainCharacter.gameObject.scene);
                    SceneManager.MoveGameObjectToScene(ballPrefabInstance, LevelManager.Instance.MainCharacter.gameObject.scene); 
                }
                else if (activeScene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(syncObject, activeScene);
                    SceneManager.MoveGameObjectToScene(ballPrefabInstance, activeScene);
                }
                else
                {
                }

                foreach (Renderer r in ballPrefabInstance.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                }
                ballPrefabInstance.SetActive(true);
                syncObject.SetActive(true);


                _replacedGrenadeSyncObjects[grenade] = syncObject; // 存储替换模型以便后续清理

                Rigidbody newBallRb = ballPrefabInstance.GetComponent<Rigidbody>();
                if (newBallRb != null)
                {
                    newBallRb.isKinematic = true;
                    newBallRb.useGravity = false;
                    Destroy(newBallRb);
                }
                return syncObject;
            }
            else
            {
                return null;
            }
        }

        public class GrenadeModelSync : MonoBehaviour
        {
            private Transform _targetGrenadeTransform;
            private bool _initialized = false;
            private GameObject _replacedModelInstance; // 存储实际替换的ball模型实例

            public void Initialize(Transform target, GameObject replacedModel)
            {
                _targetGrenadeTransform = target;
                _replacedModelInstance = replacedModel; // 在初始化时设置
                _initialized = true;
                // 初始位置和旋转
                if (_targetGrenadeTransform != null && _replacedModelInstance != null)
                {
                    // 让 _replacedModelInstance 的位置和旋转直接跟随 _targetGrenadeTransform
                    _replacedModelInstance.transform.position = _targetGrenadeTransform.position;
                    _replacedModelInstance.transform.rotation = _targetGrenadeTransform.rotation;

                    transform.position = _targetGrenadeTransform.position;
                    transform.rotation = _targetGrenadeTransform.rotation;
                }
            }
            void Update()
            {
                if (!_initialized || _targetGrenadeTransform == null || _replacedModelInstance == null)
                {
                    return;
                }

                _replacedModelInstance.transform.position = _targetGrenadeTransform.position;
                _replacedModelInstance.transform.rotation = _targetGrenadeTransform.rotation;

                transform.position = _targetGrenadeTransform.position;
                transform.rotation = _targetGrenadeTransform.rotation;
            }

            public void DestroyReplacedModel()
            {
                if (_replacedModelInstance != null)
                {
                    Destroy(_replacedModelInstance);
                    _replacedModelInstance = null;
                }
                // 销毁同步器所在的GameObject
                Destroy(gameObject);
            }

            // 获取实际被替换的模型实例
            public GameObject GetReplacedModel()
            {
                return _replacedModelInstance;
            }
        }

        private void PlayRandomEagleSound()
        {
            if (_modSounds_FMOD == null || _modSounds_FMOD.Count == 0)
            {
                return;
            }

            List<string> soundKeys = new List<string>(_modSounds_FMOD.Keys);
            if (soundKeys.Count == 0) return;

            List<string> eagleSoundKeys = new List<string>();
            foreach (string key in soundKeys)
            {
                if (key.StartsWith("Eagle_Airstrike", StringComparison.OrdinalIgnoreCase))
                {
                    eagleSoundKeys.Add(key);
                }
            }

            if (eagleSoundKeys.Count == 0)
            {
                int randomIndex = _random.Next(0, soundKeys.Count);
                string soundToPlayNameFallback = soundKeys[randomIndex];
                if (_modSounds_FMOD.TryGetValue(soundToPlayNameFallback, out FMOD.Sound soundToPlayFallback))
                {
                    FMOD.ChannelGroup masterSfxGroup;
                    RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                    FMOD.Channel channel = default(FMOD.Channel);
                    RuntimeManager.CoreSystem.playSound(soundToPlayFallback, masterSfxGroup, false, out channel);
                }
                return;
            }

            int randomEagleIndex = _random.Next(0, eagleSoundKeys.Count);
            string soundToPlayName = eagleSoundKeys[randomEagleIndex];

            if (_modSounds_FMOD.TryGetValue(soundToPlayName, out FMOD.Sound soundToPlay))
            {
                FMOD.ChannelGroup masterSfxGroup;
                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                FMOD.Channel channel = default(FMOD.Channel);
                RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
            }
            else
            {
            }
        }
    }
}