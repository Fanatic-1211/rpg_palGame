﻿// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2023, Jiaqi Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using Core.Animation;
    using Core.DataReader.Cpk;
    using Core.FileSystem;
    using Core.Services;
    using Data;
    using MetaData;
    using Renderer;
    using Settings;
    #if UNITY_EDITOR || UNITY_STANDALONE
    using SimpleFileBrowser;
    #endif
    using TMPro;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UI;
    using Debug = UnityEngine.Debug;

    /// <summary>
    /// The Game Resource initializer
    /// Initialize file system etc.
    /// </summary>
    public sealed class GameResourceInitializer : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera uiCamera;
        [SerializeField] private GameObject startingComponent;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private TextMeshProUGUI loadingText;

        private const int DEFAULT_CODE_PAGE = 936; // GBK Encoding's code page
                                                   // change it to 950 to supports Traditional Chinese (Big5)

        // Optional materials that are used in the game but not open sourced
        private Material _toonDefaultMaterial;
        private Material _toonTransparentMaterial;

        private IEnumerator Start()
        {
            loadingText.text = "Loading game assets...";
            yield return null; // Wait for next frame to make sure the text is updated

            _toonDefaultMaterial = Resources.Load<Material>("Materials/ToonDefault");
            _toonTransparentMaterial = Resources.Load<Material>("Materials/ToonTransparent");

            yield return InitResourceAsync();
        }

        private IEnumerator InitResourceAsync()
        {
            // TODO: let user to choose language? Or auto-detect encoding?
            int codepage = DEFAULT_CODE_PAGE;

            // Create and init CRC hash
            CrcHash crcHash = new ();
            crcHash.Init();
            ServiceLocator.Instance.Register<CrcHash>(crcHash);

            // If toon materials are not present, it's an open source build
            bool isOpenSourceVersion = _toonDefaultMaterial == null || _toonTransparentMaterial == null;

            // Init settings store
            ITransactionalKeyValueStore settingsStore = new PlayerPrefsStore();

            // Init settings
            GameSettings gameSettings = new (settingsStore, isOpenSourceVersion);
            gameSettings.InitOrLoadSettings();
            ServiceLocator.Instance.Register<GameSettings>(gameSettings);

            // Init file system
            string gameDataFolderPath = gameSettings.GameDataFolderPath;
            ICpkFileSystem cpkFileSystem = null;

            while (cpkFileSystem == null)
            {
                yield return InitFileSystemAsync(gameDataFolderPath,
                    crcHash, codepage, fileSystem =>
                {
                    cpkFileSystem = fileSystem;
                });

                if (cpkFileSystem == null)
                {
                    string userPickedGameDataFolderPath = null;

                    #if UNITY_EDITOR || UNITY_STANDALONE
                    yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Folders,
                         allowMultiSelection: false,
                         initialPath: null,
                         initialFilename: GameConstants.AppName,
                         title: $"请选择<<{GameConstants.AppNameCNFull}>>原始游戏文件夹根目录",
                         loadButtonText: "选择");
                    if (FileBrowser.Success && FileBrowser.Result.Length == 1)
		            {
                        userPickedGameDataFolderPath = FileBrowser.Result[0];
		            }
                    #endif

                    if (!string.IsNullOrEmpty(userPickedGameDataFolderPath))
                    {
                        gameDataFolderPath = userPickedGameDataFolderPath;
                        loadingText.text = "Loading game assets...";
                        yield return null; // Wait for next frame to make sure the text is updated
                        continue; // Retry when new root path is picked
                    }
                    else
                    {
                        #if UNITY_EDITOR
                        EditorApplication.ExitPlaymode();
                        #elif UNITY_STANDALONE
                        Application.Quit();
                        #endif
                    }

                    yield break; // Stop initialization if failed to init file system
                }

                ServiceLocator.Instance.Register<ICpkFileSystem>(cpkFileSystem);

                // Save game data folder path when file system initialized successfully,
                // since it's possible that user changed the game data folder path
                // during the file system initialization
                gameSettings.GameDataFolderPath = gameDataFolderPath;
            }

            // Init TextureLoaderFactory
            TextureLoaderFactory textureLoaderFactory = new ();
            ServiceLocator.Instance.Register<ITextureLoaderFactory>(textureLoaderFactory);

            // Init material factories
            IMaterialFactory unlitMaterialFactory = new UnlitMaterialFactory();
            IMaterialFactory litMaterialFactory = null;

            // Only create litMaterialFactory when toon materials are present
            if (_toonDefaultMaterial != null && _toonTransparentMaterial != null)
            {
                litMaterialFactory = new LitMaterialFactory(_toonDefaultMaterial, _toonTransparentMaterial);
            }

            // Init Game resource provider
            var resourceProvider = new GameResourceProvider(cpkFileSystem,
                new TextureLoaderFactory(),
                unlitMaterialFactory,
                litMaterialFactory,
                gameSettings,
                codepage);
            ServiceLocator.Instance.Register(resourceProvider);

            // Cache warm up
            resourceProvider.PreLoadMainActorMv3();

            // Instantiate starting component
            GameObject startingGameObject = Instantiate(startingComponent, null);
            startingGameObject.name = startingComponent.name;

            yield return FadeTextAndBackgroundImageAsync();

            FinalizeInit();
        }

        private void FinalizeInit()
        {
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            // Since everything except for ServiceLocator will be destroyed,
            // we can just name the current game object as ServiceLocator
            gameObject.name = nameof(ServiceLocator);

            Destroy(this);
        }

        private IEnumerator InitFileSystemAsync(string gameDataFolderPath,
            CrcHash crcHash,
            int codepage,
            Action<ICpkFileSystem> callback)
        {
            ICpkFileSystem cpkFileSystem = null;
            var path = gameDataFolderPath;
            Exception exception = null;

            var fileSystemInitThread = new Thread(() =>
            {
                try
                {
                    var timer = new Stopwatch();
                    timer.Start();
                    cpkFileSystem = InitializeCpkFileSystem(path, crcHash, codepage);
                    timer.Stop();
                    Debug.Log($"All cpk files mounted successfully under {path}. Total time: {timer.Elapsed.TotalSeconds:0.000}s");
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    GC.Collect();
                }
            })
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Highest
            };
            fileSystemInitThread.Start();

            while (fileSystemInitThread.IsAlive)
            {
                yield return null;
            }

            if (cpkFileSystem != null)
            {
                callback?.Invoke(cpkFileSystem);
            }
            else
            {
                loadingText.text = $"{exception.Message}";
                yield return null; // Wait for next frame to make sure the text is updated
            }
        }

        private IEnumerator FadeTextAndBackgroundImageAsync()
        {
            loadingText.text = string.Empty;
            loadingText.alpha = 0f;
            loadingText.enabled = false;

            yield return AnimationHelper.EnumerateValueAsync(1f, 0f, duration: 1f, AnimationCurveType.Linear,
                value =>
            {
                backgroundImage.color = new Color(0, 0, 0, value);
            });

            backgroundImage.color = new Color(0, 0, 0, 0);
            backgroundImage.enabled = false;
        }

        private ICpkFileSystem InitializeCpkFileSystem(string gameRootPath, CrcHash crcHash, int codepage)
        {
            ICpkFileSystem cpkFileSystem = new CpkFileSystem(gameRootPath, crcHash);

            var filesToMount = new List<string>();

            var baseDataCpk = FileConstants.BaseDataCpkPathInfo.relativePath +
                              Path.DirectorySeparatorChar + FileConstants.BaseDataCpkPathInfo.cpkName;

            filesToMount.Add(baseDataCpk);

            var musicCpk = FileConstants.MusicCpkPathInfo.relativePath +
                           Path.DirectorySeparatorChar + FileConstants.MusicCpkPathInfo.cpkName;

            filesToMount.Add(musicCpk);

            foreach ((string cpkName, string relativePath) sceneCpkPathInfo in FileConstants.SceneCpkPathInfos)
            {
                var sceneCpkPath = sceneCpkPathInfo.relativePath + Path.DirectorySeparatorChar +
                                   sceneCpkPathInfo.cpkName;
                filesToMount.Add(sceneCpkPath);
            }

            #if PAL3A
            var scnCpk = FileConstants.ScnCpkPathInfo.relativePath +
                          Path.DirectorySeparatorChar + FileConstants.ScnCpkPathInfo.cpkName;
            filesToMount.Add(scnCpk);
            var sceCpk = FileConstants.SceCpkPathInfo.relativePath +
                          Path.DirectorySeparatorChar + FileConstants.SceCpkPathInfo.cpkName;
            filesToMount.Add(sceCpk);
            #endif

            foreach (var cpkFilePath in filesToMount)
            {
                cpkFileSystem.Mount(cpkFilePath, codepage);
            }

            return cpkFileSystem;
        }
    }
}