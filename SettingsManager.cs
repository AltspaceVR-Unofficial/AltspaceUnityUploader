﻿#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

namespace AltSpace_Unity_Uploader
{
    [Serializable]
    public class Settings
    {
        public string Login = "";
        public string Password = "";
        public bool BuildForPC = true;
        public bool BuildForAndroid = true;
        public bool BuildForMac = true;
        public int SelectShader = 0;
        public bool DefaultShaderOnly = true;
        public bool CheckBuildEnv = true;

        public string KitsRootDirectory = "Assets/Prefabs";
        public bool KitsSetLayer = false;
        public bool KitsSetLightLayer = true;
        public bool KitsNormalizePos = false;
        public bool KitsNormalizeRot = false;
        public bool KitsNormalizeScale = false;
        public bool KitUnsetStatic = true;
        public bool KitsRemoveWhenGenerated = true;
        public bool KitsGenerateScreenshot = true;

        public bool TmplSetLayer = true;
        public bool TmplSetLightLayer = true;
        public bool TmplDeleteCameras = true;
        public bool TmplFixEnviroLight = true;
        public bool TmplSetStatic = false;

    }

    [InitializeOnLoad]
    [ExecuteInEditMode]
    public class SettingsManager : EditorWindow
    {
        private static string _settingsPath = "Assets/AUU_Settings.json";

        private static Settings _settings = null;
        public static Settings settings {
            get
            {
                if(_settings == null && File.Exists(_settingsPath))
                {
                    // File.Decrypt(_settingsPath);
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonUtility.FromJson<Settings>(json);
                }

                if (_settings == null)
                    _settings = new Settings();

                return _settings;
            }

            set
            {
                _settings = value;
                File.Delete(_settingsPath);
                string text = JsonUtility.ToJson(_settings);
                File.WriteAllText(_settingsPath, text);
                // File.Encrypt(_settingsPath);
            }
        }

        public static List<BuildTarget> SelectedBuildTargets
        {
            get
            {
                List<BuildTarget> targets = new List<BuildTarget>();
                if (settings.BuildForPC)
                    targets.Add(BuildTarget.StandaloneWindows);

                if (settings.BuildForAndroid)
                    targets.Add(BuildTarget.Android);

                if (settings.BuildForMac)
                    targets.Add(BuildTarget.StandaloneOSX);
                return targets;
            }
        }

        [MenuItem("AUU/Settings", false, 10)]
        public static void ShowSettingsWindow()
        {
            SettingsManager window = GetWindow<SettingsManager>();
            window.Show();
        }

        private int m_selectedTab = 0;

        private string[] m_tabs =
        {
            "General",
            "Kits",
            "Templates"
        };

        private string[] m_shaders =
        {
            "No change",
            "MRE Diffuse Vertex",
            "MRE Unlit"
        };


        private static UnityEditor.PackageManager.Requests.AddRequest addResponse;
        private static UnityEditor.PackageManager.Requests.RemoveRequest delResponse;
        private static UnityEditor.PackageManager.Requests.ListRequest listResponse;

#pragma warning disable
        /// <summary>
        /// Check and fix the XR settings. Still using the deprecated XR API.
        /// </summary>
        private static void CheckXRSettings()
        {
            string[] xrSDKs = { "Oculus" };

            if (settings.BuildForPC || settings.BuildForMac)
            {
                PlayerSettings.SetVirtualRealitySupported(BuildTargetGroup.Standalone, true);
                PlayerSettings.SetVirtualRealitySDKs(BuildTargetGroup.Standalone, xrSDKs);
            }

            if (settings.BuildForAndroid)
            {
                PlayerSettings.SetVirtualRealitySupported(BuildTargetGroup.Android, true);
                PlayerSettings.SetVirtualRealitySDKs(BuildTargetGroup.Android, xrSDKs);
            }

            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;

        }
#pragma warning restore
        /// <summary>
        /// Check and fix the layer settings. 14 for Nav Mesh, 15 for Avatar Lighting.
        /// </summary>
        private static void CheckLayerSettings()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            SerializedProperty l14 = layersProp.GetArrayElementAtIndex(14);
            if (l14 != null) l14.stringValue = "Nav Mesh";

            SerializedProperty l15 = layersProp.GetArrayElementAtIndex(15);
            if (l15 != null) l15.stringValue = "Avatar 15";

            tagManager.ApplyModifiedProperties();
        }

        private static void PackageAddResponse()
        {
            if (addResponse.IsCompleted)
            {
                EditorApplication.update -= PackageAddResponse;
                Debug.LogWarning("Installed needed Oculus package " + addResponse.Result.name);
                EditorApplication.update += CheckPackageInstall;
            }
        }

        private static void PackageDelResponse()
        {
            if(delResponse.IsCompleted)
            {
                EditorApplication.update -= PackageDelResponse;
                Debug.LogWarning("Removed package " + delResponse.PackageIdOrName);
                EditorApplication.update += CheckPackageInstall;
            }
        }

        private static void PackageListResponse()
        {
            bool oculusStandaloneInstalled = false;
            bool oculusAndroidInstalled = false;
            bool unityxrmgmt = false;

            if (listResponse.IsCompleted)
            {
                EditorApplication.update -= PackageListResponse;

                foreach (var package in listResponse.Result)
                {
                    if (package.name == "com.unity.xr.oculus.standalone")
                        oculusStandaloneInstalled = true;
                    else if (package.name == "com.unity.xr.oculus.android")
                        oculusAndroidInstalled = true;
                    else if (package.name == "com.unity.xr.management")
                        unityxrmgmt = true;
                }

                if (unityxrmgmt)
                {
                    delResponse = UnityEditor.PackageManager.Client.Remove("com.unity.xr.management");
                    EditorApplication.update += PackageDelResponse;
                    return;
                }

                if (!oculusStandaloneInstalled && (settings.BuildForPC || settings.BuildForMac))
                {
                    addResponse = UnityEditor.PackageManager.Client.Add("com.unity.xr.oculus.standalone");
                    EditorApplication.update += PackageAddResponse;
                    return;
                }

                if (!oculusAndroidInstalled && settings.BuildForAndroid)
                {
                    addResponse = UnityEditor.PackageManager.Client.Add("com.unity.xr.oculus.android");
                    EditorApplication.update += PackageAddResponse;
                    return;
                }

                CheckXRSettings();
                CheckLayerSettings();
                Debug.Log("Player Settings adjusted for AltspaceVR build, we're good to go.");
            }
        }

        private static void CheckPackageInstall()
        {
            EditorApplication.update -= CheckPackageInstall;
            listResponse = UnityEditor.PackageManager.Client.List(true);
            EditorApplication.update += PackageListResponse;
        }


        static SettingsManager()
        {
            if(Common.usingUnityVersion != Common.currentUnityVersion)
            {
                Debug.LogWarning("Your Unity version is " + Application.unityVersion + ", which is different from a 2019.4 version.");
                Debug.LogWarning("It is STRONGLY recommended to install 2019.4.2f1 and update this project to use it.");
            }

            if (settings.CheckBuildEnv)
            {
                Debug.Log("Checking build settings...");
                EditorApplication.update += CheckPackageInstall;
            }
        }

        public void OnGUI()
        {
            _ = settings;

            if (!Common.IsBuildTargetSupported(BuildTarget.StandaloneWindows))
                _settings.BuildForPC = false;
            if (!Common.IsBuildTargetSupported(BuildTarget.Android))
                _settings.BuildForAndroid = false;
            if (!Common.IsBuildTargetSupported(BuildTarget.StandaloneOSX))
                _settings.BuildForMac = false;



            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });

            m_selectedTab = GUILayout.Toolbar(m_selectedTab, m_tabs);

            EditorGUILayout.Space(20);

            if (m_tabs[m_selectedTab] == "General")
            {
                _settings.Login = EditorGUILayout.TextField(new GUIContent("EMail", "The EMail you've registered yourself to Altspace with."), _settings.Login);
                _settings.Password = EditorGUILayout.PasswordField(new GUIContent("Password", "Your password"), _settings.Password);

                EditorGUILayout.Space(10);

                if (Common.IsBuildTargetSupported(BuildTarget.StandaloneWindows))
                    _settings.BuildForPC = EditorGUILayout.Toggle(new GUIContent("Build for PC"), _settings.BuildForPC);
                else
                    EditorGUILayout.LabelField(new GUIContent(
                        "Build for PC disabled",
                        "Building for PC is disabled, you need to install the correct module using Unity Hub."));

                if (Common.IsBuildTargetSupported(BuildTarget.Android))
                    _settings.BuildForAndroid = EditorGUILayout.Toggle(new GUIContent("Build for Android"), _settings.BuildForAndroid);
                else
                    EditorGUILayout.LabelField(new GUIContent(
                        "Build for Android disabled",
                        "Building for Android is disabled, you need to install the correct module using Unity Hub."));

                if (Common.IsBuildTargetSupported(BuildTarget.StandaloneOSX))
                    _settings.BuildForMac = EditorGUILayout.Toggle(new GUIContent("Build for macOS"), _settings.BuildForMac);
                else
                    EditorGUILayout.LabelField(new GUIContent(
                        "Build for macOS disabled",
                        "Building for macOS is disabled, you need to install the correct module using Unity Hub."));

                EditorGUILayout.Space(10);

                bool oldCheckBuildEnv = settings.CheckBuildEnv;

                _settings.CheckBuildEnv = EditorGUILayout.Toggle(new GUIContent(
                    "Check Build Environment",
                    "Do some consistency checks and fixes on the build environment, if needed"),
                    _settings.CheckBuildEnv);

                if(!oldCheckBuildEnv && settings.CheckBuildEnv)
                {
                    settings = _settings; // Save settings.
                    Debug.Log("Checking build settings now, hold on tight...");
                    EditorApplication.update += CheckPackageInstall;
                }

            }
            else if(m_tabs[m_selectedTab] == "Kits")
            {
                _settings.KitsRootDirectory = Common.FileSelectionField(new GUIContent(
                    "Kits Root Directory",
                    "Root path for all kit data files. Every kit gets its own directory below that one."),
                    true, true, _settings.KitsRootDirectory);

                EditorGUILayout.Space(10);

                _settings.KitsSetLayer = EditorGUILayout.Toggle(new GUIContent(
                    "Set layer to 14",
                    "Set layer of objects to 14, to allow for teleporting"),
                    _settings.KitsSetLayer);

                _settings.KitsSetLightLayer = EditorGUILayout.Toggle(new GUIContent(
                    "Include light layer 15",
                    "Add layer 15 to light culling mask, to allow avatars to be lit as well"),
                    _settings.KitsSetLightLayer);

                EditorGUILayout.Space(10);

                _settings.KitsNormalizePos = EditorGUILayout.Toggle(new GUIContent(
                    "Normalize Position",
                    "Set position to (0,0,0) before exporting"
                    ), _settings.KitsNormalizePos);

                _settings.KitsNormalizeRot = EditorGUILayout.Toggle(new GUIContent(
                    "Normalize Rotation",
                    "Set rotation to (0,0,0) before exporting"
                    ), _settings.KitsNormalizeRot);

                _settings.KitsNormalizeScale = EditorGUILayout.Toggle(new GUIContent(
                    "Normalize Scale",
                    "Set scale to (1,1,1) before exporting"
                    ), _settings.KitsNormalizeScale);

                EditorGUILayout.Space(10);

                _settings.SelectShader = EditorGUILayout.Popup(new GUIContent(
                    "Set shaders to...",
                    "Set the shaders of the kit object to the given one"
                    ), _settings.SelectShader, m_shaders);

                _settings.DefaultShaderOnly = EditorGUILayout.Toggle(new GUIContent(
                    "Default Shader only",
                    "Change only the 'Standard' shader to the given one, leave others unaffected"
                    ), _settings.DefaultShaderOnly);

                _settings.KitUnsetStatic = EditorGUILayout.Toggle(new GUIContent(
                    "Unset 'static' on objects",
                    "Removes the 'static' flag on objects."),
                    _settings.KitUnsetStatic);

                EditorGUILayout.Space(10);

                _settings.KitsRemoveWhenGenerated = EditorGUILayout.Toggle(new GUIContent(
                    "Remove item after generation",
                    "Remove the GameObject from the scene after converting to the kit object"
                    ), _settings.KitsRemoveWhenGenerated);

                _settings.KitsGenerateScreenshot = EditorGUILayout.Toggle(new GUIContent(
                    "Generate Screenshots",
                    "Add Screenshots to the generated items"
                    ), _settings.KitsGenerateScreenshot);
            }
            else if(m_tabs[m_selectedTab] == "Templates")
            {
                _settings.TmplSetLayer = EditorGUILayout.Toggle(new GUIContent(
                    "Set layer to 14",
                    "Set layer of objects to 14, to allow for teleporting"),
                    _settings.TmplSetLayer);

                _settings.TmplSetLightLayer = EditorGUILayout.Toggle(new GUIContent(
                    "Include light layer 15",
                    "Add layer 15 to light culling mask, to allow avatars to be lit as well"),
                    _settings.TmplSetLightLayer);

                _settings.TmplDeleteCameras = EditorGUILayout.Toggle(new GUIContent(
                    "Delete Cameras",
                    "Remove all cameras in the template"),
                    _settings.TmplDeleteCameras);

                _settings.TmplFixEnviroLight = EditorGUILayout.Toggle(new GUIContent(
                    "Fix Environment Lighting",
                    "Set Environment Lighting to 'Gradient' and adapt colors if needed"),
                    _settings.TmplFixEnviroLight);

                _settings.TmplSetStatic = EditorGUILayout.Toggle(new GUIContent(
                    "Set 'static' on objects",
                    "Set the 'static' flags on all objects, making them use baked lighting.\nUSE WITH CAUTION!"),
                    _settings.TmplSetStatic);
            }



            EditorGUILayout.Space(20);

            if(GUILayout.Button("Reload Settings"))
            {
                _settings = null;
                _ = settings;
                Repaint();
            }

            if(GUILayout.Button("Save Settings"))
            {
                settings = _settings;
                Close();
            }

            EditorGUILayout.EndVertical();
        }
    }

}

#endif // UNITY_EDITOR
