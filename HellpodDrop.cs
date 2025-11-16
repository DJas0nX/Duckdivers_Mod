using UnityEngine;
using ECM2;
using Duckov.Modding;
using FMODUnity;
using System.Collections.Generic;
using System.Collections;
using Cinemachine; // 确保包含Cinemachine命名空间

namespace DuckDivers
{
    public class HellpodDrop : MonoBehaviour
    {
        private CharacterMainControl m_Character;
        private CharacterMovement m_Movement;
        private GameObject m_HellpodInstance;
        private bool m_IsDropping = false;
        private bool m_IsLanded = false;

        [SerializeField]
        private float m_DropAnimationDuration = 3.3f; // 新增：固定下落动画时长
        [SerializeField]
        private float m_HellpodHeightOffset = -1.5f;

        // FMOD相关
        private FMOD.Sound m_DropSound;
        private FMOD.Channel m_DropChannel;

        private Transform m_PlayerPrevParent;
        private Camera m_MainCamera;
        private GameObject m_CameraParentGo;
        private MeshRenderer[] m_PlayerMeshRenderers;

        private Vector3 m_CalculatedLandingPoint; // 新增：存储计算出的落地点

        public void InitializeDrop(CharacterMainControl character, Vector3 startPosition, Dictionary<string, FMOD.Sound> modSounds)
        {
            m_Character = character;
            m_Movement = character.GetComponentInChildren<CharacterMovement>();

            if (m_Movement == null)
            {
                Debug.LogError("HellpodDrop: CharacterMovement component not found. Destroying HellpodDrop.");
                Destroy(gameObject);
                return;
            }

            m_HellpodInstance = AssetManager.Instance.CreateFromPath("Assets/Airborne/hellpod.prefab");
            if (m_HellpodInstance == null)
            {
                Debug.LogError("HellpodDrop: Hellpod prefab not found. Destroying HellpodDrop.");
                Destroy(gameObject);
                return;
            }

            // --- 新增逻辑：计算落地位置 ---
            // 从起始位置向下射线检测，找到地面
            RaycastHit hit;
            // 确保射线检测的层级是正确的，包括Default, Ground, Terrain
            int layerMask = LayerMask.GetMask("Default", "Ground", "Terrain");

            // 从Hellpod的起始高度向下进行大范围的射线检测
            if (Physics.Raycast(startPosition, Vector3.down, out hit, Mathf.Infinity, layerMask))
            {
                m_CalculatedLandingPoint = hit.point;
                // 将Hellpod的初始位置设置为比落地位置高，以实现下落动画
                // 确保Hellpod的底部在落地时与m_CalculatedLandingPoint对齐
                if(m_CalculatedLandingPoint.x == 765.51f && m_CalculatedLandingPoint.z == 575.80f)
                {
                    m_CalculatedLandingPoint.y = -2f;
                }
                m_HellpodInstance.transform.position = m_CalculatedLandingPoint + Vector3.up * ( /* 计算Hellpod的高度以确保底部对齐 */ 80f + Mathf.Abs(m_HellpodHeightOffset)); // 从目标点上方80m开始下落
                Debug.Log($"Hellpod will land at: {m_CalculatedLandingPoint}. Starting position: {m_HellpodInstance.transform.position}");
            }
            else
            {
                // 如果没有检测到地面，则默认一个落地位置或销毁
                Debug.LogWarning("HellpodDrop: Could not find ground below start position. Defaulting to start position Y - 500.");
                m_CalculatedLandingPoint = new Vector3(startPosition.x, startPosition.y - 500f, startPosition.z); // 默认一个非常低的点
                m_HellpodInstance.transform.position = startPosition; // 无法计算，从起始点开始下落
            }
            // --- 新增逻辑结束 ---


            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(m_HellpodInstance, m_Character.gameObject.scene);

            m_PlayerPrevParent = m_Character.transform.parent;
            m_Character.gameObject.SetActive(true);
            m_Character.transform.SetParent(null);
            m_Character.transform.position = m_HellpodInstance.transform.position + Vector3.up * m_HellpodHeightOffset;


            m_Movement.enabled = false;
            m_PlayerMeshRenderers = m_Character.GetComponentsInChildren<MeshRenderer>(true);
            SetPlayerMeshRenderersEnabled(false);

            m_MainCamera = Camera.main;
            if (m_MainCamera != null)
            {
                m_CameraParentGo = new GameObject("HellpodCameraFollow");
                m_CameraParentGo.transform.SetParent(m_HellpodInstance.transform);
                m_CameraParentGo.transform.localPosition = Vector3.zero;

                m_MainCamera.transform.SetParent(m_CameraParentGo.transform);

                m_MainCamera.transform.localPosition = new Vector3(0, 10f, 0);
                m_MainCamera.transform.localRotation = Quaternion.Euler(90f, 0, 0);

                var playerCameraBrain = m_Character.GetComponentInChildren<CinemachineBrain>();
                if (playerCameraBrain != null) playerCameraBrain.enabled = false;
            }
            else
            {
                Debug.LogWarning("HellpodDrop: Main camera not found.");
            }

            m_IsDropping = true;
            m_IsLanded = false;

            if (modSounds.TryGetValue("Hellpod_Drop", out m_DropSound))
            {
                FMOD.ChannelGroup masterSfxGroup;
                RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out masterSfxGroup);
                RuntimeManager.CoreSystem.playSound(m_DropSound, masterSfxGroup, false, out m_DropChannel);
            }
            else
            {
                Debug.LogWarning("HellpodDrop: Hellpod_Drop sound not found.");
            }

            // --- 修改：开始下落协程，替代Update中的持续下落和检测逻辑 ---
            StartCoroutine(DropHellpodFixedTime());
            // --- 修改结束 ---
        }

        // 移除Update中的下落逻辑，由协程控制
        void Update()
        {
            if (!m_IsDropping || m_IsLanded)
                return;

            // 保持玩家跟随Hellpod，但不再在这里计算Hellpod的下落
            m_Character.transform.position = m_HellpodInstance.transform.position + Vector3.up * m_HellpodHeightOffset;
        }

        // --- 新增：固定时间下落的协程 ---
        private IEnumerator DropHellpodFixedTime()
        {
            Vector3 startHellpodPosition = m_HellpodInstance.transform.position;
            // 落地点的Y轴需要根据Hellpod的偏移量进行调整，确保Hellpod底部对齐m_CalculatedLandingPoint
            Vector3 targetHellpodPosition = m_CalculatedLandingPoint;

            float timer = 0f;
            while (timer < m_DropAnimationDuration)
            {
                // 使用Lerp进行平滑插值下落
                m_HellpodInstance.transform.position = Vector3.Lerp(startHellpodPosition, targetHellpodPosition, timer / m_DropAnimationDuration);
                m_Character.transform.position = m_HellpodInstance.transform.position + Vector3.up * m_HellpodHeightOffset; // 玩家跟随Hellpod

                timer += Time.deltaTime;
                yield return null; // 等待下一帧
            }

            // 确保Hellpod最终精确停留在落地位置
            m_HellpodInstance.transform.position = targetHellpodPosition;
            m_Character.transform.position = m_HellpodInstance.transform.position + Vector3.up * m_HellpodHeightOffset;
            LandHellpod(targetHellpodPosition); // 动画结束后调用着陆逻辑
        }
        // --- 新增结束 ---

        [SerializeField]
        private float m_ExitDuration = 1.8f;
        private float m_DelayBeforeExit = 1.7f;
        private void LandHellpod(Vector3 landingPoint)
        {
            m_IsLanded = true;
            m_IsDropping = false;

            // 确保Hellpod在着陆点
            m_HellpodInstance.transform.position = landingPoint;


            SetPlayerMeshRenderersEnabled(true);
            StartCoroutine(ExitHellpodSequence(landingPoint));
        }

        void OnDestroy()
        {
            if (m_HellpodInstance != null)
            {
                Destroy(m_HellpodInstance);
            }
            if (m_CameraParentGo != null)
            {
                Destroy(m_CameraParentGo);
            }

            // 在销毁时恢复摄像机父级，避免摄像机丢失
            if (m_MainCamera != null && m_MainCamera.transform.parent == m_CameraParentGo.transform)
            {
                m_MainCamera.transform.SetParent(null);
            }
            // 恢复玩家控制和Mesh Renderer（以防万一）
            if (m_Character != null)
            {
                if (m_Movement != null) m_Movement.enabled = true;
                SetPlayerMeshRenderersEnabled(true);
                if (m_Character.transform.parent == null)
                {
                    m_Character.transform.SetParent(m_PlayerPrevParent);
                }
            }

            // 恢复Cinemachine Brain
            if (m_Character != null)
            {
                var playerCameraBrain = m_Character.GetComponentInChildren<CinemachineBrain>(true); // 查找所有子对象
                if (playerCameraBrain != null) playerCameraBrain.enabled = true;
            }
        }

        private IEnumerator ExitHellpodSequence(Vector3 landingPoint)
        {
            Vector3 startPlayerPos = m_Character.transform.position; // 玩家当前在Hellpod内
            Vector3 targetPlayerPos = landingPoint + Vector3.up * 0.1f; // 目标钻出位置

            float delayTimer = 0f;
            while (delayTimer < m_DelayBeforeExit)
            {
                // 确保摄像机和玩家在延迟期间保持位置
                if (m_CameraParentGo != null)
                {
                    m_CameraParentGo.transform.position = m_HellpodInstance.transform.position;
                }
                m_Character.transform.position = startPlayerPos; // 确保玩家停留在Hellpod内
                delayTimer += Time.deltaTime;
                yield return null;
            }

            float timer = 0f;
            while (timer < m_ExitDuration)
            {
                if (m_CameraParentGo != null)
                {
                    m_CameraParentGo.transform.position = m_HellpodInstance.transform.position;
                }

                m_Character.transform.position = Vector3.Lerp(startPlayerPos, targetPlayerPos, timer / m_ExitDuration);
                timer += Time.deltaTime;
                yield return null;
            }

            m_Character.transform.position = targetPlayerPos;

            // 恢复玩家的原始父级
            m_Character.transform.SetParent(m_PlayerPrevParent);
            m_Movement.enabled = true;

            // 恢复摄像机
            if (m_MainCamera != null)
            {
                m_MainCamera.transform.SetParent(null);
            }
            if (m_CameraParentGo != null)
            {
                Destroy(m_CameraParentGo);
            }

            // 恢复Cinemachine Brain
            var playerCameraBrain = m_Character.GetComponentInChildren<CinemachineBrain>(true);
            if (playerCameraBrain != null) playerCameraBrain.enabled = true;

        }

        void OnDrawGizmos()
        {
            if (m_HellpodInstance != null && m_IsDropping)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(m_HellpodInstance.transform.position, 0.5f);

                // 绘制射线检测起点和方向 (现在只用于调试显示计算出的落地点)
                Gizmos.color = Color.red;
                Gizmos.DrawLine(m_HellpodInstance.transform.position, m_CalculatedLandingPoint);

                // 绘制计算出的落地点
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(m_CalculatedLandingPoint, 1f);
            }
        }
        private void SetPlayerMeshRenderersEnabled(bool enabled)
        {
            if (m_PlayerMeshRenderers != null)
            {
                foreach (MeshRenderer renderer in m_PlayerMeshRenderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = enabled;
                    }
                }
            }
        }
    }
}