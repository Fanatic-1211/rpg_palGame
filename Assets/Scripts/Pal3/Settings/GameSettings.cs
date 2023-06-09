﻿// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2023, Jiaqi Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Settings
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Command;
    using Command.InternalCommands;
    using Core.Utils;
    using IngameDebugConsole;
    using MetaData;
    using UnityEngine;

    public sealed class GameSettings : SettingsBase, IDisposable
    {
        private static Resolution _nativeResolution;

        public bool IsOpenSourceVersion { get; }

        public GameSettings(ITransactionalKeyValueStore settingsStore, bool isOpenSourceVersion) : base(settingsStore)
        {
            IsOpenSourceVersion = isOpenSourceVersion;

            #if UNITY_STANDALONE
            // Last resolution is the full native resolution on desktop platforms
            _nativeResolution = Screen.resolutions.Last();
            #else
            // Current resolution is the full native resolution on other platforms
            _nativeResolution = Screen.currentResolution;
            #endif

            InitDefaultSettings();

            PropertyChanged += OnPropertyChanged;

            DebugLogConsole.AddCommand<int>("Settings.VSyncCount",
                "设置垂直同步设定（0：关闭，1：开启，2: 开启（每2帧刷新）)", _ => VSyncCount = _);
            DebugLogConsole.AddCommand<int>("Settings.AntiAliasing",
                "设置抗锯齿设定（0：关闭，2：2倍抗锯齿，4：4倍抗锯齿，8：8倍抗锯齿)", _ => AntiAliasing = _);
            DebugLogConsole.AddCommand<int>("Settings.TargetFrameRate",
                "设置目标帧率设定（-1：不限制，30：30帧，60：60帧）", _ => TargetFrameRate = _);
            DebugLogConsole.AddCommand<float>("Settings.ResolutionScale",
                "设置分辨率缩放设定（0.1：10%分辨率，0.5：50%分辨率，1.0：100%分辨率）", _ => ResolutionScale = _);
            DebugLogConsole.AddCommand<float>("Settings.MusicVolume",
                "设置音乐音量设定（0.0：静音，1.0：最大音量）", _ => MusicVolume = _);
            DebugLogConsole.AddCommand<float>("Settings.SfxVolume",
                "设置音效音量设定（0.0：静音，1.0：最大音量）", _ => SfxVolume = _);
            DebugLogConsole.AddCommand<bool>("Settings.IsVoiceOverEnabled",
                "设置角色配音设定（true：开启，false：关闭）", _ => IsVoiceOverEnabled = _);
            DebugLogConsole.AddCommand<string>("Settings.GameDataFolderPath",
                "设置自定义游戏数据文件夹路径", _ => GameDataFolderPath = _);

            if (!IsOpenSourceVersion)
            {
                DebugLogConsole.AddCommand<bool>("Settings.IsRealtimeLightingAndShadowsEnabled",
                    "设置实时光照和阴影设定（true：开启，false：关闭）", _ => IsRealtimeLightingAndShadowsEnabled = _);
                DebugLogConsole.AddCommand<bool>("Settings.IsAmbientOcclusionEnabled",
                    "设置环境光遮蔽设定（true：开启，false：关闭）", _ => IsAmbientOcclusionEnabled = _);
            }

            DebugLogConsole.AddCommand("Settings.Save",
                "保存所有设置", SaveSettings);
            DebugLogConsole.AddCommand("Settings.Reset",
                "重置所有设置", ResetSettings);
            DebugLogConsole.AddCommand("Settings.Print",
                "打印所有设置", PrintSettings);
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            string settingName = args.PropertyName;

            if (settingName == nameof(VSyncCount))
            {
                QualitySettings.vSyncCount = VSyncCount;
            }
            else if (settingName == nameof(AntiAliasing))
            {
                QualitySettings.antiAliasing = AntiAliasing;
            }
            else if (settingName == nameof(TargetFrameRate))
            {
                Application.targetFrameRate = TargetFrameRate;
            }
            else if (settingName == nameof(ResolutionScale))
            {
                // Let OS to handle and persist the resolution change
                // on Windows, macOS and Linux.
                #if !UNITY_STANDALONE
                Screen.SetResolution(
                    (int) (_nativeResolution.width * ResolutionScale),
                    (int) (_nativeResolution.height * ResolutionScale),
                    Screen.fullScreenMode);
                #else
                if (Screen.fullScreenMode
                    is FullScreenMode.ExclusiveFullScreen
                    or FullScreenMode.FullScreenWindow)
                {
                    Screen.SetResolution(
                        (int) (_nativeResolution.width * ResolutionScale),
                        (int) (_nativeResolution.height * ResolutionScale),
                        Screen.fullScreenMode);
                }
                #endif
            }

            // Broadcast the setting change notification.
            CommandDispatcher<ICommand>.Instance.Dispatch(new SettingChangedNotification(settingName));
        }

        private void InitDefaultSettings()
        {
            // Game should never trigger device sleep
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Hide keyboard on handheld devices
            if (Utility.IsHandheldDevice())
            {
                TouchScreenKeyboard.hideInput = true;
            }
        }

        public void InitOrLoadSettings()
        {
            if (SettingsStore.TryGet(nameof(VSyncCount), out int vSyncCount))
            {
                VSyncCount = vSyncCount;
            }
            else
            {
                // Disable v-sync on desktop devices by default
                VSyncCount = Utility.IsDesktopDevice() ? 0 : 1;
            }

            if (SettingsStore.TryGet(nameof(AntiAliasing), out int antiAliasing))
            {
                AntiAliasing = antiAliasing;
            }
            else
            {
                // 2x MSAA by default on desktop devices
                AntiAliasing = Utility.IsDesktopDevice() ? 2 : 0;
            }

            if (SettingsStore.TryGet(nameof(TargetFrameRate), out int targetFrameRate))
            {
                TargetFrameRate = targetFrameRate;
            }
            else
            {
                #if UNITY_2022_1_OR_NEWER
                var screenRefreshRate = (int) Screen.currentResolution.refreshRateRatio.value;
                #else
                var screenRefreshRate = Screen.currentResolution.refreshRate;
                #endif

                // Set target frame rate to screen refresh rate by default
                TargetFrameRate = Mathf.Max(screenRefreshRate, 60); // 60Hz is the minimum
            }

            if (SettingsStore.TryGet(nameof(ResolutionScale), out float resolutionScale))
            {
                ResolutionScale = resolutionScale;
            }
            else
            {
                // Full resolution by default unless on Android with SDK version lower than 23 (old devices)
                // SDK version 23 is Android 6.0 Marshmallow
                ResolutionScale = Utility.IsAndroidDeviceAndSdkVersionLowerThanOrEqualTo(23) ? 0.75f : 1.0f;
            }

            #if UNITY_STANDALONE
            // Reset resolution scale if not in fullscreen mode for desktop devices
            if (Screen.fullScreenMode != FullScreenMode.ExclusiveFullScreen &&
                Screen.fullScreenMode != FullScreenMode.FullScreenWindow &&
                Math.Abs(ResolutionScale - 1.0f) > 0.01f)
            {
                ResolutionScale = 1.0f;
            }
            #endif

            if (SettingsStore.TryGet(nameof(MusicVolume), out float musicVolume))
            {
                MusicVolume = musicVolume;
            }
            else
            {
                // 50% music volume by default
                MusicVolume = 0.5f;
            }

            if (SettingsStore.TryGet(nameof(SfxVolume), out float sfxVolume))
            {
                SfxVolume = sfxVolume;
            }
            else
            {
                // 50% sfx volume by default
                SfxVolume = 0.5f;
            }

            if (IsOpenSourceVersion)
            {
                // Toon materials are available in closed source builds only
                // so there will be no lit shader variants in the build for lighting to work
                IsRealtimeLightingAndShadowsEnabled = false;
            }
            else
            {
                if (SettingsStore.TryGet(nameof(IsRealtimeLightingAndShadowsEnabled),
                        out bool isRealtimeLightingAndShadowsEnabled))
                {
                    IsRealtimeLightingAndShadowsEnabled = isRealtimeLightingAndShadowsEnabled;
                }
                else
                {
                    // Enable realtime lighting and shadows by default
                    IsRealtimeLightingAndShadowsEnabled = true;
                }
            }

            if (IsOpenSourceVersion)
            {
                // Toon materials are available in closed source builds only
                // so there will be no lit shader variants in the build for lighting to work
                // thus AO will not work anyway
                IsAmbientOcclusionEnabled = false;
            }
            else
            {
                if (SettingsStore.TryGet(nameof(IsAmbientOcclusionEnabled), out bool isAmbientOcclusionEnabled))
                {
                    IsAmbientOcclusionEnabled = isAmbientOcclusionEnabled;
                }
                else
                {
                    #if UNITY_ANDROID // AO not working well with OpenGL on Android
                    IsAmbientOcclusionEnabled = false;
                    #else
                    // Enable ambient occlusion by default on desktop devices
                    IsAmbientOcclusionEnabled = Utility.IsDesktopDevice();
                    #endif
                }
            }

            if (SettingsStore.TryGet(nameof(IsVoiceOverEnabled), out bool isVoiceOverEnabled))
            {
                IsVoiceOverEnabled = isVoiceOverEnabled;
            }
            else
            {
                // Enable voice over by default
                IsVoiceOverEnabled = true;
            }

            if (SettingsStore.TryGet(nameof(GameDataFolderPath), out string gameDataFolderPath))
            {
                GameDataFolderPath = gameDataFolderPath;
            }
        }

        public IEnumerable<string> GetGameDataFolderSearchLocations()
        {
            // Use game data folder path if it is not empty
            // GameDataFolderPath is either set by the user or the last
            // successfully loaded game data folder path
            if (!string.IsNullOrEmpty(GameDataFolderPath))
            {
                yield return GameDataFolderPath;
            }

            // Streaming assets path
            yield return Application.streamingAssetsPath +
                         Path.DirectorySeparatorChar +
                         GameConstants.AppName +
                         Path.DirectorySeparatorChar;

            // Persistent data path
            yield return Application.persistentDataPath +
                         Path.DirectorySeparatorChar +
                         GameConstants.AppName +
                         Path.DirectorySeparatorChar;
        }

        public void SaveSettings()
        {
            SettingsStore.Save();
        }

        public void ResetSettings()
        {
            // Delete all settings keys
            foreach (PropertyInfo property in typeof(SettingsBase).GetProperties())
            {
                SettingsStore.DeleteKey(property.Name);
            }

            // Re-initialize settings
            InitOrLoadSettings();

            // Save settings
            SaveSettings();
        }

        public void PrintSettings()
        {
            Debug.Log("Current game settings:");

            foreach (PropertyInfo property in typeof(SettingsBase).GetProperties())
            {
                Debug.Log($"{property.Name}: {property.GetValue(this)}");
            }
        }

        public void Dispose()
        {
            PropertyChanged -= OnPropertyChanged;
        }
    }
}