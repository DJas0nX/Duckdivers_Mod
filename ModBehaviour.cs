using UnityEngine;
using FMODUnity;
using System.IO;
using System.Collections.Generic;
using Duckov.Modding; // 确保包含此命名空间
using Duckov.Scenes; // 确保包含此命名空间

namespace DuckDivers
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private InputPanelManager _inputPanelManager;
        private SkillExecutor _skillExecutor;
        private KillStreakDisplay _killStreakDisplay;

        private static Dictionary<string, FMOD.Sound> _modSounds_FMOD = new Dictionary<string, FMOD.Sound>();
        private static readonly string[] _audioNames = { "Eagle_Airstrike_1", "Eagle_Airstrike_2", "Orbital_Gas", "Orbital_Smoke", "Eagle_Strafing", "Orbital_Strike", "Eagle_500kg", "Orbital_380MM_Bomb", "Orbital_380MM", "Eagle_Napalm", "Hellpod_Drop", "Strat_Deactivate", "Strat_Input", "Strat_Success", "Strat_Activate" }; 
        private System.Random _random = new System.Random();

        protected override void OnAfterSetup()
        {
            base.OnAfterSetup();
            UnityEngine.Debug.Log("DuckDivers ModBehaviour setup complete and loaded!");

            if (!AssetManager.Instance.IsInitialized)
            {
                // 注意：如果你的AssetManager需要一个路径参数，你应该在这里传入正确的DLL路径
                AssetManager.Instance.Initialize(); // 你的AssetManager会扫描DLL路径
            }
            LoadSounds(); // 加载FMOD声音

            _skillExecutor = gameObject.AddComponent<SkillExecutor>();
            _skillExecutor.Initialize(AssetManager.Instance, _modSounds_FMOD, _random);

            _inputPanelManager = gameObject.AddComponent<InputPanelManager>();
            _inputPanelManager.Initialize(_skillExecutor.SetPendingSkill, _modSounds_FMOD);

            LevelManager.OnAfterLevelInitialized += OnLevelInitializedForHellpod;
            
            _killStreakDisplay = gameObject.AddComponent<KillStreakDisplay>(); // 添加击杀提示组件
            _killStreakDisplay.InitializeKillstreak();

        }

        protected override void OnBeforeDeactivate()
        {
            base.OnBeforeDeactivate();
            LevelManager.OnAfterLevelInitialized -= OnLevelInitializedForHellpod;

            if (_inputPanelManager != null)
            {
                _inputPanelManager.Cleanup();
                Destroy(_inputPanelManager);
                _inputPanelManager = null;
            }
            if (_killStreakDisplay != null)
            {
                Destroy(_killStreakDisplay);
                _killStreakDisplay = null;
            }
            if (_skillExecutor != null)
            {
                _skillExecutor.Cleanup();
                Destroy(_skillExecutor);
                _skillExecutor = null;
            }

            if (AssetManager.Instance.IsInitialized)
            {
                AssetManager.Instance.Uninitialize();
            }

            foreach (var sound in _modSounds_FMOD.Values)
            {
                sound.release();
            }
            _modSounds_FMOD.Clear();
        }

        private string GetDllDirectory()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private void LoadSounds()
        {
            string dllDirectory = GetDllDirectory();

            foreach (string audioName in _audioNames)
            {
                string audioPath = Path.Combine(dllDirectory,"audio",audioName + ".wav");
                if (File.Exists(audioPath))
                {
                    FMOD.Sound sound;
                    FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(audioPath, FMOD.MODE.DEFAULT, out sound);
                    if (result == FMOD.RESULT.OK)
                    {
                        _modSounds_FMOD.Add(audioName, sound);
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"Failed to load sound {audioPath}: {result.ToString()}");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError($"Sound file not found: {audioPath}");
                }
            }
        }

        public static void PlaySoundStatic(string soundName, Dictionary<string, FMOD.Sound> loadedSounds, System.Random randomizer)
        {
            if (loadedSounds.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No Eagle sounds loaded to play.");
                return;
            }

            List<string> soundKeys = new List<string>(loadedSounds.Keys);
            if (soundKeys.Count == 0) return;

            int randomIndex = randomizer.Next(0, soundKeys.Count);
            string soundToPlayName = soundKeys[randomIndex];

            if (loadedSounds.TryGetValue(soundToPlayName, out FMOD.Sound soundToPlay))
            {
                FMOD.ChannelGroup masterSfxGroup;
                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                FMOD.Channel channel = default(FMOD.Channel);
                RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
            }
            else
            {
                UnityEngine.Debug.LogError($"Could not find sound '{soundToPlayName}' in loaded sounds.");
            }
        }

        // ==== Hellpod出生点逻辑：当关卡初始化完成后调用 ====
        private void OnLevelInitializedForHellpod()
        {
            string mapId = MultiSceneCore.Instance.SceneInfo.ID;

            // 检查是否为室外地图（参考案例中的IsParachuteCompatible逻辑）
            if (!IsHellpodCompatible(mapId))
            {
                return;
            }

            CharacterMainControl player = LevelManager.Instance.MainCharacter;
            if (player == null)
            {
                Debug.LogError("DuckDivers: MainCharacter is null after level initialization. Cannot start Hellpod drop.");
                return;
            }

            // 获取玩家的初始出生点位置
            Vector3 originalSpawnPoint = player.transform.position;
            Vector3 hellpodStartPosition = originalSpawnPoint + Vector3.up * 80f;

            // 创建一个GameObject来挂载HellpodDrop组件
            GameObject hellpodDropGo = new GameObject("HellpodDropManager");
            HellpodDrop hellpodDrop = hellpodDropGo.AddComponent<HellpodDrop>();
            // 将HellpodDropManager移动到玩家场景，确保它在正确的场景中被管理
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hellpodDropGo, player.gameObject.scene);

            hellpodDrop.InitializeDrop(player, hellpodStartPosition, _modSounds_FMOD);
        }

        // 简化版的地图兼容性检查，你可以根据需要进一步完善
        private bool IsHellpodCompatible(string sceneID)
        {
            // 复制并修改学习案例中的MapHelper.IsParachuteCompatible逻辑
            // 假设我们希望在非基地和非室内地图进行Hellpod掉落
            bool isBaseLevel = LevelConfig.IsBaseLevel;
            if (isBaseLevel)
            {
                return false;
            }

            // 你可以定义自己的室内地图列表
            string[] INDOOR_MAPS = new string[]
            {
                "Base",
                "Level_JLab_Main" // 示例室内地图ID
                // 添加更多你的室内地图ID
            };

            if (System.Linq.Enumerable.Contains<string>(INDOOR_MAPS, sceneID))
            {
                return false;
            }

            // 检查MultiSceneCore的SubSceneInfo是否标记为室内
            MultiSceneCore instance = MultiSceneCore.Instance;
            SubSceneEntry subSceneInfo = (instance != null) ? instance.GetSubSceneInfo() : null;
            if (subSceneInfo != null && subSceneInfo.IsInDoor)
            {
                return false;
            }

            return true; // 默认兼容
        }
        // ==== Hellpod出生点逻辑结束 ====
    }
    public class SimpleAirPlane : MonoBehaviour
    {
        private Vector3 m_StartPoint;
        private Vector3 m_EndPoint;
        private float m_FlySpeed;
        private bool m_IsFlying = false;

        void Update()
        {
            if (!m_IsFlying)
                return;

            var dir = (m_EndPoint - m_StartPoint).normalized;
            transform.position += dir * Time.deltaTime * m_FlySpeed;

            if (Vector3.Distance(transform.position, m_EndPoint) <= 1f)
            {
                m_IsFlying = false;
                Destroy(gameObject, 1f);
            }
        }

        public void BeginFly(Vector3 startPos, Vector3 endPos, float speed)
        {
            m_StartPoint = startPos;
            m_EndPoint = endPos;
            m_FlySpeed = speed;
            transform.position = startPos;
            transform.LookAt(endPos, Vector3.up);
            m_IsFlying = true;
        }
    }
}