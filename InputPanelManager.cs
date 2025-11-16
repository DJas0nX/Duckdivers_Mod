using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;
using Duckov.UI;
using UnityEngine.InputSystem;
using FMODUnity;
using System.Collections;

namespace DuckDivers
{
    public class InputPanelManager : MonoBehaviour
    {
        // UI元素引用
        private GameObject _inputPanelCanvas;
        private RectTransform _mainPanelRect;
        private GameObject _skillListContainer;
        private Image _backgroundPanel;
        private CanvasGroup _mainPanelCanvasGroup; // 用于控制整个面板的可见性（在没有匹配技能时）

        // 动画相关
        private Vector2 _hiddenPanelPosition;
        private Vector2 _shownPanelPosition;
        private float _animationDuration = 0.2f; // 动画持续时间
        private Coroutine _panelAnimationCoroutine; // 存储面板动画的协程引用

        // 音效相关
        private Dictionary<string, FMOD.Sound> _modSounds_FMOD;

        // 输入序列管理
        private List<string> _currentInputSequence = new List<string>();
        private int _maxInputLength = 7;
        private bool _isPanelVisible = false;

        // 技能数据
        private Dictionary<string, string> _skillSequences = new Dictionary<string, string>
        {
            { "飞鹰空袭", "↑→↓→" },
            { "飞鹰500kg炸弹", "↑→↓↓↓" },
            { "飞鹰机枪扫射", "↑→→" },
            { "飞鹰凝固汽油弹空袭", "↑→↓↑" },
            { "轨道精准攻击", "→→↑" },
            { "轨道毒气攻击", "→→↓→" },
            { "轨道烟雾攻击", "→→↓↑" },
            { "轨道380MM高爆弹火力网", "→↓↑↑←↓↓" }
            //{ "撕裂者核弹", "→↑←→↓↑↓" },
        };

        // 按键到方向的映射
        private Dictionary<KeyCode, string> _keyToDirection = new Dictionary<KeyCode, string>
        {
            { KeyCode.W, "↑" },
            { KeyCode.A, "←" },
            { KeyCode.S, "↓" },
            { KeyCode.D, "→" },
            { KeyCode.UpArrow, "↑" },
            { KeyCode.LeftArrow, "←" },
            { KeyCode.DownArrow, "↓" },
            { KeyCode.RightArrow, "→" }
        };

        // 方向到图标路径的映射
        private Dictionary<string, Sprite> _directionSprites = new Dictionary<string, Sprite>();

        // 技能UI栏的实例列表
        private List<SkillUIItem> _skillUIItems = new List<SkillUIItem>();

        // 委托用于设置待生效技能
        public delegate void SetPendingSkillEventHandler(string skillName);
        private SetPendingSkillEventHandler _onSetPendingSkill;

        // 新增：用于显示“正在启动”的Text组件
        private TextMeshProUGUI _activatingSkillText;
        // 新增：用于存储被匹配到的技能UIItem
        private SkillUIItem _matchedSkillUIItem = null;

        public void Initialize(SetPendingSkillEventHandler handler, Dictionary<string, FMOD.Sound> modSounds)
        {
            _onSetPendingSkill = handler;
            _modSounds_FMOD = modSounds;

            LoadDirectionSprites();
            CreateInputPanelUI();

            _shownPanelPosition = new Vector2(20, -150);
            _hiddenPanelPosition = new Vector2(-_mainPanelRect.sizeDelta.x - 20, -150);

            HideInputPanelInstant();
        }

        private void LoadDirectionSprites()
        {
            string modDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string iconsPath = System.IO.Path.Combine(modDirectory, "icons");

            _directionSprites.Clear();

            _directionSprites.Add("↑", LoadSpriteFromPath(System.IO.Path.Combine(iconsPath, "Up.png")));
            _directionSprites.Add("↓", LoadSpriteFromPath(System.IO.Path.Combine(iconsPath, "Down.png")));
            _directionSprites.Add("←", LoadSpriteFromPath(System.IO.Path.Combine(iconsPath, "Left.png")));
            _directionSprites.Add("→", LoadSpriteFromPath(System.IO.Path.Combine(iconsPath, "Right.png")));

            foreach (var kvp in _directionSprites)
            {
                if (kvp.Value == null)
                {
                }
            }
        }

        private Sprite LoadSpriteFromPath(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            byte[] fileData;
            fileData = System.IO.File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return null;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
            {
                ToggleInputPanel();
            }

            // 只有当面板可见且没有成功匹配的技能时才处理输入
            if (_isPanelVisible && _matchedSkillUIItem == null)
            {
                HandleInput();
            }

            // 在有匹配技能的情况下，当面板可见时，我们也需要处理释放技能的逻辑
            // 假设技能释放由外部系统触发，此处仅处理UI状态的更新。
            // 技能释放的逻辑应该由 SkillExecutor 或类似组件处理，并在技能被调用后调用 OnSkillCalled。
        }

        private void CreateInputPanelUI()
        {
            _inputPanelCanvas = new GameObject("InputPanelCanvas");
            Canvas canvas = _inputPanelCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _inputPanelCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _inputPanelCanvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            _inputPanelCanvas.AddComponent<GraphicRaycaster>();
            _inputPanelCanvas.transform.SetParent(this.transform);

            _mainPanelCanvasGroup = _inputPanelCanvas.AddComponent<CanvasGroup>();
            _mainPanelCanvasGroup.alpha = 0f;
            _mainPanelCanvasGroup.blocksRaycasts = false;
            _mainPanelCanvasGroup.interactable = false;

            GameObject backgroundPanelGO = new GameObject("BackgroundPanel");
            backgroundPanelGO.transform.SetParent(_inputPanelCanvas.transform);
            _backgroundPanel = backgroundPanelGO.AddComponent<Image>();
            _backgroundPanel.color = new Color(0, 0, 0, 0f);
            _backgroundPanel.rectTransform.anchorMin = Vector2.zero;
            _backgroundPanel.rectTransform.anchorMax = Vector2.one;
            _backgroundPanel.rectTransform.offsetMin = Vector2.zero;
            _backgroundPanel.rectTransform.offsetMax = Vector2.zero;
            Button backgroundButton = backgroundPanelGO.AddComponent<Button>();
            backgroundButton.targetGraphic = _backgroundPanel;
            backgroundButton.onClick.AddListener(ToggleInputPanel);

            GameObject mainPanelGO = new GameObject("MainPanel");
            mainPanelGO.transform.SetParent(_inputPanelCanvas.transform);
            Image mainPanelImage = mainPanelGO.AddComponent<Image>();
            mainPanelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            _mainPanelRect = mainPanelGO.GetComponent<RectTransform>();
            _mainPanelRect.pivot = new Vector2(0f, 1f);
            _mainPanelRect.anchorMin = new Vector2(0f, 1f);
            _mainPanelRect.anchorMax = new Vector2(0f, 1f);
            _mainPanelRect.anchoredPosition = _hiddenPanelPosition;
            _mainPanelRect.sizeDelta = new Vector2(320, 540);

            _skillListContainer = new GameObject("SkillListContainer");
            _skillListContainer.transform.SetParent(mainPanelGO.transform);
            VerticalLayoutGroup layoutGroup = _skillListContainer.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.spacing = 5f;
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;

            RectTransform containerRect = _skillListContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

            foreach (var skill in _skillSequences)
            {
                CreateSkillUIItem(skill.Key, skill.Value);
            }

            GameObject activatingTextGO = new GameObject("ActivatingSkillText");
            activatingTextGO.transform.SetParent(_skillListContainer.transform); // 初始放在容器下，方便管理
            _activatingSkillText = activatingTextGO.AddComponent<TextMeshProUGUI>();
            _activatingSkillText.text = "正在启动";
            _activatingSkillText.color = Color.yellow;
            _activatingSkillText.fontSize = 22;
            _activatingSkillText.alignment = TextAlignmentOptions.Center;
            _activatingSkillText.rectTransform.sizeDelta = new Vector2(0, 30);
            _activatingSkillText.enabled = false;
        }

        private void CreateSkillUIItem(string skillName, string sequence)
        {
            GameObject skillPanelGO = new GameObject($"SkillPanel_{skillName}");
            skillPanelGO.transform.SetParent(_skillListContainer.transform);

            CanvasGroup skillCanvasGroup = skillPanelGO.AddComponent<CanvasGroup>();
            skillCanvasGroup.alpha = 1f;
            skillCanvasGroup.blocksRaycasts = false;
            skillCanvasGroup.interactable = false;

            RectTransform skillPanelRect = skillPanelGO.AddComponent<RectTransform>();
            skillPanelRect.sizeDelta = new Vector2(0, 60);

            LayoutElement skillLayoutElement = skillPanelGO.AddComponent<LayoutElement>(); // 新增
            skillLayoutElement.preferredHeight = 60; // 匹配 sizeDelta.y
            skillLayoutElement.flexibleHeight = 0;
            skillLayoutElement.flexibleWidth = 0; // 允许宽度自适应父级

            HorizontalLayoutGroup hLayoutGroup = skillPanelGO.AddComponent<HorizontalLayoutGroup>();
            hLayoutGroup.childAlignment = TextAnchor.MiddleLeft;
            hLayoutGroup.spacing = 10f;
            hLayoutGroup.padding = new RectOffset(10, 0, 0, 0);
            hLayoutGroup.childForceExpandHeight = false;
            hLayoutGroup.childForceExpandWidth = false;

            GameObject iconGO = new GameObject("SkillIcon");
            iconGO.transform.SetParent(skillPanelGO.transform);
            Image skillIcon = iconGO.AddComponent<Image>();

            string modDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string iconsPath = System.IO.Path.Combine(modDirectory, "icons");
            string skillIconFilePath = System.IO.Path.Combine(iconsPath, $"{skillName.Replace(" ", "_")}.png");

            Sprite skillSprite = LoadSpriteFromPath(skillIconFilePath);

            if (skillSprite == null)
            {
            }
            else
            {
                skillIcon.sprite = skillSprite;
            }

            skillIcon.rectTransform.sizeDelta = new Vector2(50, 50);
            skillIcon.preserveAspect = true;

            LayoutElement skillIconLayoutElement = iconGO.AddComponent<LayoutElement>();
            skillIconLayoutElement.preferredWidth = 50;
            skillIconLayoutElement.preferredHeight = 50;
            skillIconLayoutElement.flexibleWidth = 0;
            skillIconLayoutElement.flexibleHeight = 0;

            GameObject infoContainerGO = new GameObject("InfoContainer");
            infoContainerGO.transform.SetParent(skillPanelGO.transform);
            VerticalLayoutGroup vLayoutGroup = infoContainerGO.AddComponent<VerticalLayoutGroup>();
            vLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            vLayoutGroup.spacing = 2f;
            vLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            vLayoutGroup.childForceExpandHeight = false;
            vLayoutGroup.childForceExpandWidth = false;

            LayoutElement infoContainerLayoutElement = infoContainerGO.AddComponent<LayoutElement>();
            infoContainerLayoutElement.flexibleWidth = 0;
            infoContainerLayoutElement.flexibleHeight = 1;

            GameObject nameGO = new GameObject("SkillName");
            nameGO.transform.SetParent(infoContainerGO.transform);
            TextMeshProUGUI skillNameText = nameGO.AddComponent<TextMeshProUGUI>();
            skillNameText.text = skillName;
            skillNameText.color = Color.white;
            skillNameText.fontSize = 20;
            skillNameText.alignment = TextAlignmentOptions.TopLeft;
            skillNameText.rectTransform.sizeDelta = new Vector2(0, 25);

            GameObject sequenceContainerGO = new GameObject("SequenceContainer");
            sequenceContainerGO.transform.SetParent(infoContainerGO.transform);
            HorizontalLayoutGroup sequenceHLayout = sequenceContainerGO.AddComponent<HorizontalLayoutGroup>();
            sequenceHLayout.childAlignment = TextAnchor.MiddleLeft;
            sequenceHLayout.spacing = 5f;
            sequenceHLayout.padding = new RectOffset(0, 0, 0, 0);
            sequenceHLayout.childForceExpandHeight = false;
            sequenceHLayout.childForceExpandWidth = false;

            List<Image> sequenceIcons = new List<Image>();
            foreach (char dirChar in sequence)
            {
                string direction = dirChar.ToString();
                GameObject dirIconGO = new GameObject($"DirIcon_{direction}");
                dirIconGO.transform.SetParent(sequenceContainerGO.transform);
                Image dirIconImage = dirIconGO.AddComponent<Image>();

                if (_directionSprites.TryGetValue(direction, out Sprite sprite))
                {
                    dirIconImage.sprite = sprite;
                }
                else
                {
                }
                dirIconImage.rectTransform.sizeDelta = new Vector2(25, 25);
                dirIconImage.preserveAspect = true;

                LayoutElement dirIconLayoutElement = dirIconGO.AddComponent<LayoutElement>();
                dirIconLayoutElement.preferredWidth = 25;
                dirIconLayoutElement.preferredHeight = 25;
                dirIconLayoutElement.flexibleWidth = 0;
                dirIconLayoutElement.flexibleHeight = 0;

                sequenceIcons.Add(dirIconImage);
            }

            _skillUIItems.Add(new SkillUIItem
            {
                SkillName = skillName,
                SkillSequence = sequence,
                SkillPanelGameObject = skillPanelGO,
                SkillCanvasGroup = skillCanvasGroup,
                SkillIcon = skillIcon,
                SkillNameText = skillNameText,
                SequenceIcons = sequenceIcons,
                SequenceContainer = sequenceContainerGO,
                SkillPanelRectTransform = skillPanelRect,
                SkillLayoutElement = skillLayoutElement // 赋值
            });
        }

        private void ToggleInputPanel()
        {
            if (_matchedSkillUIItem != null) // 如果有匹配到的技能，这次Toggle是释放技能并隐藏面板
            {
                PlaySound("Strat_Deactivate");
                // 此时，OnSkillCalled 应该由外部的技能释放逻辑调用
                // 但为了在用户按Ctrl时也能关闭，这里先模拟一下行为
                OnSkillCalled(); // 模拟技能被调用并清理UI
                HideInputPanel(); // 隐藏整个面板
            }
            else if (_isPanelVisible) // 面板可见且没有匹配技能时，按Ctrl隐藏面板
            {
                PlaySound("Strat_Deactivate");
                HideInputPanel();
            }
            else // 面板不可见时，按Ctrl显示面板
            {
                PlaySound("Strat_Activate");
                ShowInputPanel();
            }
        }

        private void ShowInputPanel()
        {
            _inputPanelCanvas.SetActive(true);
            _isPanelVisible = true;
            _currentInputSequence.Clear();
            _matchedSkillUIItem = null;
            _activatingSkillText.enabled = false;

            // 确保所有技能栏可见并复位
            foreach (var item in _skillUIItems)
            {
                item.SkillPanelGameObject.SetActive(true);
                item.SkillCanvasGroup.alpha = 1f;
                item.SkillNameText.color = Color.white;
                item.SkillIcon.color = Color.white;
                foreach (var icon in item.SequenceIcons)
                {
                    icon.gameObject.SetActive(true);
                    icon.color = Color.white;
                }
                // 确保“正在启动”文本不在任何技能栏下
                if (_activatingSkillText.transform.parent != null && _activatingSkillText.transform.parent == item.SequenceContainer.transform)
                {
                    _activatingSkillText.transform.SetParent(_skillListContainer.transform);
                    _activatingSkillText.enabled = false;
                }
            }
            UpdateSkillUIStates(); // 首次显示时更新UI状态

            // 动画：从左侧滑入并渐显整个面板
            if (_panelAnimationCoroutine != null) StopCoroutine(_panelAnimationCoroutine);
            _panelAnimationCoroutine = StartCoroutine(AnimatePanel(_hiddenPanelPosition, _shownPanelPosition, 0f, 1f, _mainPanelRect, _mainPanelCanvasGroup, null));
        }

        private void HideInputPanel()
        {
            _isPanelVisible = false;
            _currentInputSequence.Clear();
            _matchedSkillUIItem = null; // 确保清除匹配状态

            // 动画：向左侧划出并渐隐整个面板
            if (_panelAnimationCoroutine != null) StopCoroutine(_panelAnimationCoroutine);
            _panelAnimationCoroutine = StartCoroutine(AnimatePanel(_shownPanelPosition, _hiddenPanelPosition, 1f, 0f, _mainPanelRect, _mainPanelCanvasGroup, () =>
            {
                _inputPanelCanvas.SetActive(false);
                // 重置所有技能栏状态，以便下次打开时正常显示
                foreach (var item in _skillUIItems)
                {
                    item.SkillPanelGameObject.SetActive(true);
                    item.SkillCanvasGroup.alpha = 1f;
                    item.SkillNameText.color = Color.white;
                    item.SkillIcon.color = Color.white;
                    foreach (var icon in item.SequenceIcons)
                    {
                        icon.gameObject.SetActive(true);
                        icon.color = Color.white;
                    }
                    // 确保“正在启动”文本不在任何技能栏下
                    if (_activatingSkillText.transform.parent != null && _activatingSkillText.transform.parent == item.SequenceContainer.transform)
                    {
                        _activatingSkillText.transform.SetParent(_skillListContainer.transform);
                        _activatingSkillText.enabled = false;
                    }
                }
                _activatingSkillText.enabled = false;
            }));
        }

        // 立即隐藏面板，用于初始化
        private void HideInputPanelInstant()
        {
            if (_panelAnimationCoroutine != null) StopCoroutine(_panelAnimationCoroutine);
            _inputPanelCanvas.SetActive(false);
            _isPanelVisible = false;
            _currentInputSequence.Clear();
            _mainPanelRect.anchoredPosition = _hiddenPanelPosition;
            _mainPanelCanvasGroup.alpha = 0f;
            _mainPanelCanvasGroup.blocksRaycasts = false;
            _mainPanelCanvasGroup.interactable = false;
            _matchedSkillUIItem = null;
            _activatingSkillText.enabled = false;

            foreach (var item in _skillUIItems)
            {
                item.SkillPanelGameObject.SetActive(true);
                item.SkillCanvasGroup.alpha = 1f;
                item.SkillNameText.color = Color.white;
                item.SkillIcon.color = Color.white;
                foreach (var icon in item.SequenceIcons)
                {
                    icon.gameObject.SetActive(true);
                    icon.color = Color.white;
                }
                if (_activatingSkillText.transform.parent != null && _activatingSkillText.transform.parent == item.SequenceContainer.transform)
                {
                    _activatingSkillText.transform.SetParent(_skillListContainer.transform);
                    _activatingSkillText.enabled = false;
                }
            }
        }

        private IEnumerator AnimatePanel(Vector2 startPos, Vector2 endPos, float startAlpha, float endAlpha, RectTransform targetRect, CanvasGroup targetCanvasGroup, Action onComplete = null)
        {
            float timer = 0f;
            targetCanvasGroup.blocksRaycasts = true;
            targetCanvasGroup.interactable = true;

            while (timer < _animationDuration)
            {
                timer += Time.deltaTime;
                float progress = timer / _animationDuration;

                targetRect.anchoredPosition = Vector2.Lerp(startPos, endPos, progress);
                targetCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, progress);

                yield return null;
            }

            targetRect.anchoredPosition = endPos;
            targetCanvasGroup.alpha = endAlpha;

            if (endAlpha == 0f)
            {
                targetCanvasGroup.blocksRaycasts = false;
                targetCanvasGroup.interactable = false;
            }

            onComplete?.Invoke();
        }

        // 新增：动画单个技能栏



        private void PlaySound(string soundName)
        {
            if (_modSounds_FMOD.TryGetValue(soundName, out FMOD.Sound soundToPlay))
            {
                FMOD.ChannelGroup masterSfxGroup;
                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                FMOD.Channel channel = default(FMOD.Channel);
                RuntimeManager.CoreSystem.playSound(soundToPlay, masterSfxGroup, false, out channel);
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) ||
                Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D) ||
                Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow) ||
                Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow) )
            {
                PlaySound("Strat_Input");
                KeyCode pressedKey = KeyCode.None;
                if (Input.GetKeyDown(KeyCode.W)) pressedKey = KeyCode.W;
                else if (Input.GetKeyDown(KeyCode.A)) pressedKey = KeyCode.A;
                else if (Input.GetKeyDown(KeyCode.S)) pressedKey = KeyCode.S;
                else if (Input.GetKeyDown(KeyCode.D)) pressedKey = KeyCode.D;
                else if (Input.GetKeyDown(KeyCode.UpArrow)) pressedKey = KeyCode.W;
                else if (Input.GetKeyDown(KeyCode.LeftArrow)) pressedKey = KeyCode.A;
                else if (Input.GetKeyDown(KeyCode.DownArrow)) pressedKey = KeyCode.S;
                else if (Input.GetKeyDown(KeyCode.RightArrow)) pressedKey = KeyCode.D;

                if (pressedKey != KeyCode.None && _keyToDirection.TryGetValue(pressedKey, out string direction))
                {
                    _currentInputSequence.Add(direction);
                    if (_currentInputSequence.Count > _maxInputLength)
                    {
                        _currentInputSequence.RemoveAt(0);
                    }
                    UpdateSkillUIStates();
                    CheckForSkillActivation();
                }
            }
        }

        private void UpdateSkillUIStates()
        {
            string currentSequenceString = string.Join("", _currentInputSequence);

            foreach (var skillItem in _skillUIItems)
            {
                // 如果已经有匹配到的技能，并且当前技能不是那个匹配到的技能，则隐藏它
                if (_matchedSkillUIItem != null && skillItem != _matchedSkillUIItem)
                {
                    skillItem.SkillPanelGameObject.SetActive(false);
                    continue;
                }
                // 如果是匹配到的技能，则保持其完全可见，并显示“正在启动”
                else if (_matchedSkillUIItem != null && skillItem == _matchedSkillUIItem)
                {
                    skillItem.SkillPanelGameObject.SetActive(true); // 确保可见
                    skillItem.SkillCanvasGroup.alpha = 1f;
                    skillItem.SkillNameText.color = Color.white;
                    skillItem.SkillIcon.color = Color.white;

                    // 隐藏原始的方向键序列图标
                    foreach (var icon in skillItem.SequenceIcons)
                    {
                        icon.gameObject.SetActive(false);
                    }

                    // 将“正在启动”文本移动到匹配技能的序列容器下并显示
                    if (_activatingSkillText.transform.parent != skillItem.SequenceContainer.transform)
                    {
                        _activatingSkillText.transform.SetParent(skillItem.SequenceContainer.transform);
                        _activatingSkillText.rectTransform.localPosition = Vector3.zero;
                        _activatingSkillText.rectTransform.anchoredPosition = new Vector2(0, 0); // 确保居中
                        _activatingSkillText.enabled = true;
                    }
                    continue;
                }

                // 没有匹配技能时的普通前缀匹配逻辑
                bool isPrefixMatch = skillItem.SkillSequence.StartsWith(currentSequenceString);
                skillItem.SkillPanelGameObject.SetActive(true); // 确保所有未匹配技能栏可见

                if (isPrefixMatch)
                {
                    skillItem.SkillCanvasGroup.alpha = 1f; // 完全不透明
                    skillItem.SkillNameText.color = Color.white;
                    skillItem.SkillIcon.color = Color.white;
                }
                else
                {
                    skillItem.SkillCanvasGroup.alpha = 0.3f; // 半透明
                    skillItem.SkillNameText.color = new Color(1f, 1f, 1f, 0.3f);
                    skillItem.SkillIcon.color = new Color(1f, 1f, 1f, 0.3f);
                }

                // 更新方向键图标的透明度
                for (int i = 0; i < skillItem.SequenceIcons.Count; i++)
                {
                    Image icon = skillItem.SequenceIcons[i];
                    icon.gameObject.SetActive(true); // 确保图标可见

                    if (i < _currentInputSequence.Count &&
                        i < skillItem.SkillSequence.Length &&
                        skillItem.SkillSequence[i].ToString() == _currentInputSequence[i])
                    {
                        icon.color = new Color(1f, 1f, 1f, 0.3f); // 已输入的匹配部分透明度减半
                    }
                    else
                    {
                        if (isPrefixMatch)
                        {
                            icon.color = Color.white; // 未输入的匹配部分恢复完全不透明
                        }
                        else
                        {
                            icon.color = new Color(1f, 1f, 1f, 0.15f); // 不匹配的技能栏中的图标更淡
                        }
                    }
                }
            }
        }

        private void CheckForSkillActivation()
        {
            string currentSequenceString = string.Join("", _currentInputSequence);
            bool skillMatched = false;
            foreach (var skillItem in _skillUIItems)
            {
                if (currentSequenceString.EndsWith(skillItem.SkillSequence))
                {
                    PlaySound("Strat_Success");

                    _matchedSkillUIItem = skillItem; // 标记匹配到的技能
                    _onSetPendingSkill?.Invoke(skillItem.SkillName); // 通知SkillExecutor设置待生效技能

                    HideOthersAndShowActivating(skillItem); // 触发其他技能栏的渐隐划出动画
                    skillMatched = true;
                    return; // 成功匹配技能后直接返回，不再检查其他技能
                }
            }


            if (!skillMatched && _currentInputSequence.Count > 0)
            {
                bool prefixMatchesAnySkill = false;
                foreach (var skillItem in _skillUIItems)
                {
                    // 检查当前输入序列是否是任何技能序列的前缀
                    if (skillItem.SkillSequence.StartsWith(currentSequenceString))
                    {
                        prefixMatchesAnySkill = true;
                        break;
                    }
                }

                if (!prefixMatchesAnySkill)
                {
                    PlaySound("Strat_Deactivate");
                    HideInputPanel();
                }
            }
        }
        // 新增：动画单个技能栏
        private IEnumerator AnimateSkillPanel(SkillUIItem skillItem, float startAlpha, float endAlpha, Action onComplete = null)
        {
            float timer = 0f;
            // 记录初始位置，以便在渐隐时向左移动
            Vector2 startAnchoredPosition = skillItem.SkillPanelRectTransform.anchoredPosition; // 使用 anchoredPosition
            Vector2 endAnchoredPosition = startAnchoredPosition + new Vector2(-skillItem.SkillPanelRectTransform.rect.width, 0); // 划出到左侧，使用 rect.width

            while (timer < _animationDuration)
            {
                timer += Time.deltaTime;
                float progress = timer / _animationDuration;

                skillItem.SkillCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, progress);
                // 仅当alpha从1到0时才执行划出动画
                if (startAlpha > endAlpha)
                {
                    skillItem.SkillPanelRectTransform.anchoredPosition = Vector2.Lerp(startAnchoredPosition, endAnchoredPosition, progress);
                }

                yield return null;
            }

            skillItem.SkillCanvasGroup.alpha = endAlpha;
            if (startAlpha > endAlpha)
            {
                // 动画结束后将其隐藏，避免占用布局空间
                skillItem.SkillPanelGameObject.SetActive(false);
            }
            onComplete?.Invoke();
        }

        // 新增：隐藏其他技能栏，并显示匹配到的技能栏的“正在启动”状态
        private void HideOthersAndShowActivating(SkillUIItem matchedSkill)
        {
            // 停止主面板动画，因为它不再控制所有技能栏
            if (_panelAnimationCoroutine != null)
            {
                StopCoroutine(_panelAnimationCoroutine);
                _panelAnimationCoroutine = null;
            }

            // 将主面板CanvasGroup设置为完全可见，确保匹配到的技能栏不会受到整体透明度的影响
            _mainPanelCanvasGroup.alpha = 1f;
            _mainPanelCanvasGroup.blocksRaycasts = true;
            _mainPanelCanvasGroup.interactable = true;

            // 调整 _mainPanelRect 的大小以只包裹匹配到的技能栏
            _mainPanelRect.sizeDelta = new Vector2(320, matchedSkill.SkillPanelRectTransform.sizeDelta.y + _skillListContainer.GetComponent<VerticalLayoutGroup>().padding.top + _skillListContainer.GetComponent<VerticalLayoutGroup>().padding.bottom);
            _mainPanelRect.anchoredPosition = _shownPanelPosition; // 保持面板在显示位置

            // 清除 _skillListContainer 的 VerticalLayoutGroup，使其不再控制布局
            VerticalLayoutGroup layoutGroup = _skillListContainer.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.enabled = false;
            }

            // 对其他技能栏进行渐隐和划出动画
            foreach (var item in _skillUIItems)
            {
                if (item == matchedSkill)
                {
                    // 匹配到的技能栏保持完全不透明和原位
                    item.SkillPanelGameObject.SetActive(true);
                    item.SkillCanvasGroup.alpha = 1f;
                    item.SkillNameText.color = Color.white;
                    item.SkillIcon.color = Color.white;

                    // 禁用其 LayoutElement，使其脱离布局控制
                    if (item.SkillLayoutElement != null) item.SkillLayoutElement.enabled = false;

                    // 调整匹配到的技能栏的位置，使其位于主面板的左上角（忽略padding）
                    // 假设 MainPanel 的 Pivot 是 (0,1)，anchoredPosition 是 (20, -90)
                    // SkillPanel 的 RectTransform 需要被设置为相对于其父级 (skillListContainer) 的位置
                    item.SkillPanelRectTransform.SetParent(_skillListContainer.transform, false); // 确保父级是 _skillListContainer
                    item.SkillPanelRectTransform.anchorMin = Vector2.up; // 左上角锚点
                    item.SkillPanelRectTransform.anchorMax = Vector2.up;
                    item.SkillPanelRectTransform.pivot = Vector2.up; // 左上角枢轴
                    item.SkillPanelRectTransform.anchoredPosition = new Vector2(layoutGroup.padding.left, -layoutGroup.padding.top); // 放置在布局组的内边距位置

                    // 隐藏原始的方向键序列图标
                    foreach (var icon in item.SequenceIcons)
                    {
                        icon.gameObject.SetActive(false);
                    }

                    // 将“正在启动”文本移动到匹配技能的序列容器下并显示
                    if (_activatingSkillText.transform.parent != item.SequenceContainer.transform)
                    {
                        _activatingSkillText.transform.SetParent(item.SequenceContainer.transform);
                        _activatingSkillText.rectTransform.localPosition = Vector3.zero;
                        _activatingSkillText.rectTransform.anchoredPosition = new Vector2(0, 0); // 确保居中
                        _activatingSkillText.enabled = true;
                    }
                }
                else
                {
                    // 禁用其他技能栏的 LayoutElement，使其脱离布局控制
                    if (item.SkillLayoutElement != null) item.SkillLayoutElement.enabled = false;

                    // 其他技能栏渐隐并划出
                    if (item.SkillPanelGameObject.activeSelf)
                    {
                        StartCoroutine(AnimateSkillPanel(item, item.SkillCanvasGroup.alpha, 0f, () => {
                            item.SkillPanelGameObject.SetActive(false);
                        }));
                    }
                }
            }
        }


        public void OnSkillCalled()
        {
            if (_matchedSkillUIItem != null)
            {

                // 恢复 _skillListContainer 的 VerticalLayoutGroup
                VerticalLayoutGroup layoutGroup = _skillListContainer.GetComponent<VerticalLayoutGroup>();
                if (layoutGroup != null)
                {
                    layoutGroup.enabled = true;
                }

                // 确保“正在启动”文本被隐藏并回到初始父级
                _activatingSkillText.enabled = false;
                if (_activatingSkillText.transform.parent != _skillListContainer.transform)
                {
                    _activatingSkillText.transform.SetParent(_skillListContainer.transform);
                }

                // 恢复匹配技能栏的所有图标和状态
                _matchedSkillUIItem.SkillPanelGameObject.SetActive(false); // 隐藏匹配到的技能栏
                _matchedSkillUIItem.SkillCanvasGroup.alpha = 1f;
                _matchedSkillUIItem.SkillNameText.color = Color.white;
                _matchedSkillUIItem.SkillIcon.color = Color.white;
                foreach (var icon in _matchedSkillUIItem.SequenceIcons)
                {
                    icon.gameObject.SetActive(true);
                    icon.color = Color.white;
                }
                // 恢复 LayoutElement
                if (_matchedSkillUIItem.SkillLayoutElement != null) _matchedSkillUIItem.SkillLayoutElement.enabled = true;


                // 恢复所有技能栏的可见性，并将它们的 LayoutElement 重新启用
                foreach (var item in _skillUIItems)
                {
                    item.SkillPanelGameObject.SetActive(true);
                    item.SkillCanvasGroup.alpha = 1f;
                    item.SkillNameText.color = Color.white;
                    item.SkillIcon.color = Color.white;
                    foreach (var icon in item.SequenceIcons)
                    {
                        icon.gameObject.SetActive(true);
                        icon.color = Color.white;
                    }
                    if (item.SkillLayoutElement != null) item.SkillLayoutElement.enabled = true;
                    // 确保所有技能栏的 RectTransform 位置复位，交给布局组管理
                    item.SkillPanelRectTransform.anchorMin = Vector2.zero; // 重置为默认拉伸锚点，让布局组控制
                    item.SkillPanelRectTransform.anchorMax = Vector2.one;
                    item.SkillPanelRectTransform.pivot = new Vector2(0.5f, 0.5f); // 恢复中心枢轴
                    item.SkillPanelRectTransform.offsetMin = Vector2.zero;
                    item.SkillPanelRectTransform.offsetMax = Vector2.zero;
                }

                _matchedSkillUIItem = null; // 清除匹配状态
                _isPanelVisible = false; // 面板现在应该是隐藏的
                _inputPanelCanvas.SetActive(false); // 确保整个Canvas被禁用

                // 重置主面板CanvasGroup状态和大小
                _mainPanelCanvasGroup.alpha = 0f;
                _mainPanelCanvasGroup.blocksRaycasts = false;
                _mainPanelCanvasGroup.interactable = false;
                _mainPanelRect.sizeDelta = new Vector2(320, 540); // 恢复到原始大小
                _mainPanelRect.anchoredPosition = _hiddenPanelPosition; // 恢复到隐藏位置
            }
        }

        public void Cleanup()
        {
            if (_inputPanelCanvas != null)
            {
                Destroy(_inputPanelCanvas);
            }
            _onSetPendingSkill = null;
        }

        private class SkillUIItem
        {
            public string SkillName;
            public string SkillSequence;
            public GameObject SkillPanelGameObject;
            public CanvasGroup SkillCanvasGroup;
            public Image SkillIcon;
            public TextMeshProUGUI SkillNameText;
            public List<Image> SequenceIcons;
            public GameObject SequenceContainer;
            public RectTransform SkillPanelRectTransform;
            public LayoutElement SkillLayoutElement; // 新增：技能栏的LayoutElement
        }
    }
}