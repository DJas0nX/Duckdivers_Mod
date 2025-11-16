using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace DuckDivers
{
    /// <summary>
    /// AssetBundle资源管理器
    /// 基于资源路径映射的资源管理系统，支持UniTask异步操作
    /// 自动扫描当前DLL运行路径下的AssetBundle文件并建立资源映射
    /// </summary>
    public class AssetManager
    {
        private static AssetManager? _instance;
        public static AssetManager Instance
        {
            get
            {
                _instance ??= new AssetManager();
                return _instance;
            }
        }

        // AssetBundle缓存
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();

        // 资源缓存
        private Dictionary<string, UnityEngine.Object> _loadedAssets = new Dictionary<string, UnityEngine.Object>();

        // 资源路径到AssetBundle名称的映射
        private Dictionary<string, string> _assetPathToBundleMap = new Dictionary<string, string>();

        // AssetBundle名称到资源路径列表的映射
        private Dictionary<string, List<string>> _bundleToAssetPathsMap = new Dictionary<string, List<string>>();

        // 是否已初始化映射
        private bool _isInitialized = false;

        private AssetManager() { }

        /// <summary>
        /// 初始化AssetManager，扫描指定目录下的AssetBundle文件并建立资源路径映射
        /// </summary>
        /// <param name="bundleDirectory">AssetBundle文件目录，默认为当前DLL运行路径</param>
        public void Initialize(string bundleDirectory = "")
        {
            if (_isInitialized)
            {
                return;
            }

            if (string.IsNullOrEmpty(bundleDirectory))
            {
                // 获取当前DLL运行的路径
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                bundleDirectory = Path.GetDirectoryName(assemblyLocation);
            }

            ScanAndBuildAssetMapping(bundleDirectory);
            _isInitialized = true;
        }

        public void Uninitialize()
        {
            UnloadAllAssetBundles(true);
            _assetPathToBundleMap.Clear();
            _bundleToAssetPathsMap.Clear();
            _isInitialized = false;
        }

        /// <summary>
        /// 扫描目录并建立资源路径到AssetBundle的映射
        /// </summary>
        private void ScanAndBuildAssetMapping(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            // 查找所有.assetbundle文件
            string[] bundleFiles = Directory.GetFiles(directory, "*.assetbundle", SearchOption.AllDirectories);

            foreach (string bundleFile in bundleFiles)
            {
                string bundleName = Path.GetFileNameWithoutExtension(bundleFile);

                try
                {
                    // 临时加载AssetBundle以获取资源列表
                    AssetBundle tempBundle = AssetBundle.LoadFromFile(bundleFile);
                    if (tempBundle != null)
                    {
                        string[] assetNames = tempBundle.GetAllAssetNames();

                        if (!_bundleToAssetPathsMap.ContainsKey(bundleName))
                        {
                            _bundleToAssetPathsMap[bundleName] = new List<string>();
                        }

                        foreach (string assetPath in assetNames)
                        {
                            var path = assetPath.Replace("\\", "/").ToLower();
                            _assetPathToBundleMap[path] = bundleName;
                            _bundleToAssetPathsMap[bundleName].Add(path);
                        }

                        _loadedBundles[bundleName] = tempBundle;
                    }
                }
                catch (Exception e)
                {
                }
            }
        }

        /// <summary>
        /// 同步加载资源（通过资源路径）
        /// </summary>
        public T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            assetPath = assetPath.Replace("\\", "/").ToLower();

            // 检查缓存
            if (_loadedAssets.ContainsKey(assetPath))
            {
                return _loadedAssets[assetPath] as T;
            }

            // 查找资源所属的AssetBundle
            if (!_assetPathToBundleMap.ContainsKey(assetPath))
            {
                return null;
            }

            string bundleName = _assetPathToBundleMap[assetPath];

            // 确保AssetBundle已加载
            if (!EnsureBundleLoaded(bundleName))
            {
                return null;
            }

            // 从AssetBundle加载资源
            AssetBundle bundle = _loadedBundles[bundleName];
            T asset = bundle.LoadAsset<T>(assetPath);

            if (asset != null)
            {
                _loadedAssets[assetPath] = asset;
            }
            else
            {
            }

            return asset;
        }

        /// <summary>
        /// 异步加载资源（通过资源路径）
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string assetPath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // 检查缓存
            if (_loadedAssets.ContainsKey(assetPath))
            {
                return _loadedAssets[assetPath] as T;
            }

            // 查找资源所属的AssetBundle
            if (!_assetPathToBundleMap.ContainsKey(assetPath))
            {
                return null;
            }

            string bundleName = _assetPathToBundleMap[assetPath];

            // 确保AssetBundle已加载
            bool bundleLoaded = await EnsureBundleLoadedAsync(bundleName);
            if (!bundleLoaded)
            {
                return null;
            }

            // 从AssetBundle异步加载资源
            AssetBundle bundle = _loadedBundles[bundleName];
            AssetBundleRequest request = bundle.LoadAssetAsync<T>(assetPath);

            await request.ToUniTask();

            T asset = request.asset as T;
            if (asset != null)
            {
                _loadedAssets[assetPath] = asset;
            }
            else
            {
            }

            return asset;
        }

        /// <summary>
        /// 确保AssetBundle已加载（同步）
        /// </summary>
        private bool EnsureBundleLoaded(string bundleName)
        {
            if (_loadedBundles.ContainsKey(bundleName))
            {
                return true;
            }

            return LoadAssetBundleInternal(bundleName);
        }

        /// <summary>
        /// 确保AssetBundle已加载（异步）
        /// </summary>
        private async UniTask<bool> EnsureBundleLoadedAsync(string bundleName)
        {
            if (_loadedBundles.ContainsKey(bundleName))
            {
                return true;
            }

            return await LoadAssetBundleInternalAsync(bundleName);
        }

        /// <summary>
        /// 内部同步加载AssetBundle
        /// </summary>
        private bool LoadAssetBundleInternal(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName))
            {
                return false;
            }

            if (_loadedBundles.ContainsKey(bundleName))
            {
                return true;
            }

            try
            {
                // 获取当前DLL运行的路径
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string bundleDirectory = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(bundleDirectory, bundleName + ".assetbundle");

                if (!File.Exists(bundlePath))
                {
                    return false;
                }

                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle != null)
                {
                    _loadedBundles[bundleName] = bundle;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// 内部异步加载AssetBundle
        /// </summary>
        private async UniTask<bool> LoadAssetBundleInternalAsync(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName))
            {
                return false;
            }

            if (_loadedBundles.ContainsKey(bundleName))
            {
                return true;
            }

            try
            {
                // 获取当前DLL运行的路径
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string bundleDirectory = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(bundleDirectory, bundleName + ".assetbundle");

                if (!File.Exists(bundlePath))
                {
                    return false;
                }

                var bundle = await AssetBundle.LoadFromFileAsync(bundlePath);

                if (bundle != null)
                {
                    _loadedBundles[bundleName] = bundle;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// 获取所有资源路径
        /// </summary>
        public string[] GetAllAssetPaths()
        {
            return _assetPathToBundleMap.Keys.ToArray();
        }

        /// <summary>
        /// 获取指定AssetBundle中的所有资源路径
        /// </summary>
        public string[] GetAssetPathsInBundle(string bundleName)
        {
            if (_bundleToAssetPathsMap.ContainsKey(bundleName))
            {
                return _bundleToAssetPathsMap[bundleName].ToArray();
            }
            return new string[0];
        }

        /// <summary>
        /// 根据资源路径查找所属的AssetBundle名称
        /// </summary>
        public string FindBundleByAssetPath(string assetPath)
        {
            if (_assetPathToBundleMap.ContainsKey(assetPath))
            {
                return _assetPathToBundleMap[assetPath];
            }
            return string.Empty;
        }

        /// <summary>
        /// 卸载指定的AssetBundle
        /// </summary>
        public void UnloadAssetBundle(string bundleName, bool unloadAllLoadedObjects = false)
        {
            if (_loadedBundles.ContainsKey(bundleName))
            {
                _loadedBundles[bundleName].Unload(unloadAllLoadedObjects);
                _loadedBundles.Remove(bundleName);

                // 清理相关的资源缓存
                if (unloadAllLoadedObjects && _bundleToAssetPathsMap.ContainsKey(bundleName))
                {
                    foreach (string assetPath in _bundleToAssetPathsMap[bundleName])
                    {
                        _loadedAssets.Remove(assetPath);
                    }
                }

            }
        }

        /// <summary>
        /// 卸载所有AssetBundle
        /// </summary>
        public void UnloadAllAssetBundles(bool unloadAllLoadedObjects = false)
        {
            foreach (var bundle in _loadedBundles.Values)
            {
                bundle.Unload(unloadAllLoadedObjects);
            }

            _loadedBundles.Clear();

            if (unloadAllLoadedObjects)
            {
                _loadedAssets.Clear();
            }

        }

        /// <summary>
        /// 获取已加载的AssetBundle数量
        /// </summary>
        public int LoadedBundleCount => _loadedBundles.Count;

        /// <summary>
        /// 获取已加载的资源数量
        /// </summary>
        public int LoadedAssetCount => _loadedAssets.Count;

        /// <summary>
        /// 检查AssetBundle是否已加载
        /// </summary>
        public bool IsBundleLoaded(string bundleName)
        {
            return _loadedBundles.ContainsKey(bundleName);
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized => _isInitialized;


        public GameObject CreateFromPath(string assetPath)
        {
            var asset = LoadAsset<GameObject>(assetPath);
            if (asset == null)
                return default;
            return GameObject.Instantiate(asset);
        }
    }
}