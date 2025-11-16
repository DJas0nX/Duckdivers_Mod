using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Collections.Generic;
using Duckov.Modding; // 仍然需要访问AssetManager等
using DG.Tweening; // 引入DOTween库，用于实现动画

namespace DuckDivers
{
    // !!! 修改这里：继承自 MonoBehaviour !!!
    public class KillStreakDisplay : MonoBehaviour
    {
        // UI元素
        private static GameObject _uiRoot; // 整体UI的根GameObject
        private static Image _killStreakIcon; // 击杀连击图标
        private static Text _killCountText; // 击杀计数文本
        private static CanvasGroup _canvasGroup; // 用于控制整体UI的淡入淡出

        // 击杀连击相关数据
        private static int _currentKillStreak = 0;
        private static float _lastKillTime = 0f;
        private const float KILLSTREAK_DURATION = 10f; // 击杀连击持续时间

        // 纹理字典
        private static Dictionary<string, Texture2D> _killStreakIcons = new Dictionary<string, Texture2D>();

        // 资源路径
        private static readonly string[] KILLSTREAK_ICON_NAMES = { "Killstreak_1", "Killstreak_2", "Killstreak_3" };
        private const string ICONS_PATH_PREFIX = "icons/"; // 假设图标在DLL同级目录的icons文件夹下

        // 动画参数
        private const float ICON_SCALE_ANIM_DURATION = 0.15f;
        private const float ICON_SCALE_BOOST = 1.1f; // 放大10%

        public void InitializeKillstreak()
        {

            // 确保AssetManager已初始化（在ModBehaviour中已经处理）
            if (!AssetManager.Instance.IsInitialized)
            {
                // 如果在Awake时AssetManager未初始化，可能需要考虑DuckDivers.ModBehaviour的初始化时机
                // 但通常主ModBehaviour会在所有子组件Awake之前完成自己的OnAfterSetup
                AssetManager.Instance.Initialize();
            }

            // 加载击杀连击图标
            LoadKillStreakIcons();

            // 创建UI
            CreateKillStreakUI();

            // 订阅击杀事件 (假设有一个可以订阅的击杀事件)
            Health.OnDead += OnCharacterDead;

            // 隐藏UI
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        // !!! 修改这里：使用 OnDestroy() 代替 OnBeforeDeactivate() !!!
        void OnDestroy()
        {

            // 取消订阅事件
            Health.OnDead -= OnCharacterDead;

            // 清理UI
            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
                _killStreakIcon = null;
                _killCountText = null;
                _canvasGroup = null;
            }

            // 释放纹理资源
            foreach (var texture in _killStreakIcons.Values)
            {
                Destroy(texture); // Texture2D是Unity对象，需要销毁
            }
            _killStreakIcons.Clear();

            _currentKillStreak = 0;
            _lastKillTime = 0f;
        }

        void Update()
        {
            // 处理连击计时器和淡出
            if (_currentKillStreak > 0 && Time.time - _lastKillTime > KILLSTREAK_DURATION)
            {
                FadeOutKillStreak();
            }
        }

        private void LoadKillStreakIcons()
        {
            string dllDirectory = GetDllDirectory();

            foreach (string iconName in KILLSTREAK_ICON_NAMES)
            {
                string iconPath = Path.Combine(dllDirectory, ICONS_PATH_PREFIX + iconName + ".png");
                if (File.Exists(iconPath))
                {
                    byte[] fileData = File.ReadAllBytes(iconPath);
                    Texture2D texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, fileData))
                    {
                        // !!! 这里也添加检查，防止重复添加，尽管现在不应该出现 !!!
                        if (!_killStreakIcons.ContainsKey(iconName))
                        {
                            _killStreakIcons.Add(iconName, texture);
                        }
                        else
                        {
                            Destroy(texture); // 销毁重复加载的纹理
                        }
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
        }

        private string GetDllDirectory()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private void CreateKillStreakUI()
        {
            HUDManager hudManager = FindObjectOfType<HUDManager>();
            if (hudManager == null)
            {
                return;
            }

            if (_uiRoot != null) // 防止重复创建UI
            {
                return;
            }

            _uiRoot = new GameObject("KillStreakDisplayUI");
            RectTransform rootRect = _uiRoot.AddComponent<RectTransform>();
            _uiRoot.transform.SetParent(hudManager.transform);
            _uiRoot.transform.SetAsLastSibling();
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.anchoredPosition = new Vector2(0f, 200f);
            rootRect.sizeDelta = new Vector2(300f, 100f);

            _canvasGroup = _uiRoot.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            GameObject iconGo = new GameObject("KillStreakIcon");
            RectTransform iconRect = iconGo.AddComponent<RectTransform>();
            iconGo.transform.SetParent(rootRect);
            _killStreakIcon = iconGo.AddComponent<Image>();
            _killStreakIcon.preserveAspect = true;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-10f, 0f);
            iconRect.sizeDelta = new Vector2(64f, 64f);

            GameObject textGo = new GameObject("KillCountText");
            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textGo.transform.SetParent(rootRect);
            _killCountText = textGo.AddComponent<Text>();
            _killCountText.font = Font.CreateDynamicFontFromOSFont("Arial", 36);
            _killCountText.fontSize = 36;
            _killCountText.alignment = TextAnchor.MiddleLeft;
            _killCountText.color = Color.yellow;
            _killCountText.text = "";
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.anchoredPosition = new Vector2(10f, 0f);
            textRect.sizeDelta = new Vector2(100f, 50f);
        }

        private void OnCharacterDead(Health health, DamageInfo damageInfo)
        {
            //if (health != null && !health.IsMainCharacterHealth && damageInfo.fromCharacter == LevelManager.Instance.MainCharacter)
            if (health != null && !health.IsMainCharacterHealth)
            {
                IncrementKillStreak();
            }
        }

        private void IncrementKillStreak()
        {
            float currentTime = Time.time;

            if (currentTime - _lastKillTime <= KILLSTREAK_DURATION)
            {
                _currentKillStreak++;
            }
            else
            {
                _currentKillStreak = 1;
            }

            _lastKillTime = currentTime;
            UpdateKillStreakDisplay();
        }

        private void UpdateKillStreakDisplay()
        {
            if (_uiRoot == null || _killStreakIcon == null || _killCountText == null || _canvasGroup == null)
            {
                // 如果UI不存在，则尝试创建（可能是在订阅事件后才加载mod）
                CreateKillStreakUI(); // 尝试再次创建UI，但应该被防止重复创建的逻辑拦截
                if (_uiRoot == null) return;
            }

            string iconKey = "";
            if (_currentKillStreak >= 1 && _currentKillStreak <= 3)
            {
                iconKey = "Killstreak_1";
            }
            else if (_currentKillStreak >= 4 && _currentKillStreak <= 6)
            {
                iconKey = "Killstreak_2";
            }
            else if (_currentKillStreak >= 7)
            {
                iconKey = "Killstreak_3";
            }

            if (_killStreakIcons.TryGetValue(iconKey, out Texture2D iconTexture))
            {
                _killStreakIcon.sprite = Sprite.Create(iconTexture, new Rect(0f, 0f, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f));
            }
            else
            {
            }

            _killCountText.text = $"x{_currentKillStreak}";

            _killStreakIcon.transform.DOKill(true);
            _killCountText.transform.DOKill(true);

            _killStreakIcon.transform.DOScale(ICON_SCALE_BOOST, ICON_SCALE_ANIM_DURATION)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => _killStreakIcon.transform.DOScale(1f, ICON_SCALE_ANIM_DURATION).SetEase(Ease.InQuad));

            _killCountText.transform.DOScale(ICON_SCALE_BOOST, ICON_SCALE_ANIM_DURATION)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => _killCountText.transform.DOScale(1f, ICON_SCALE_ANIM_DURATION).SetEase(Ease.InQuad));

            if (_canvasGroup.alpha < 1f)
            {
                _canvasGroup.DOKill(true);
                _canvasGroup.DOFade(1f, 0.2f);
            }
        }

        private void FadeOutKillStreak()
        {
            if (_canvasGroup != null && _canvasGroup.alpha > 0f)
            {
                _canvasGroup.DOKill(true);
                _canvasGroup.DOFade(0f, 0.5f)
                    .OnComplete(() =>
                    {
                        _currentKillStreak = 0;
                        _killCountText.text = "";
                        _killStreakIcon.sprite = null;
                    });
            }
        }
    }
}