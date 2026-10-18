// 選單 MIRDC > 建立展示主程式場景 (首頁)
// 產生 Assets/Scenes/Kiosk.unity 與 Assets/UI/KioskPanelSettings.asset
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MIRDC.Kiosk.EditorTools
{
    public static class KioskSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Kiosk.unity";
        const string PanelPath = "Assets/UI/KioskPanelSettings.asset";
        const string ThemePath = "Assets/UI/KioskTheme.tss";
        const string UssPath   = "Assets/UI/Kiosk.uss";
        const string FontDir   = "Assets/UI/Fonts/";

        static readonly Color Ground = new Color32(228, 230, 229, 255);

        [MenuItem("MIRDC/建立展示主程式場景 (首頁)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            var regular = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "NotoSansTC-Regular.otf");
            var medium  = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "NotoSansTC-Medium.otf");
            var bold    = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "NotoSansTC-Bold.otf");
            if (uss == null) Debug.LogError("[Kiosk] 找不到 " + UssPath);
            if (theme == null) Debug.LogWarning("[Kiosk] 找不到 " + ThemePath);
            if (regular == null || bold == null) Debug.LogError("[Kiosk] 找不到 Noto Sans TC 字型（" + FontDir + "）");

            // PanelSettings：以 1920×1280（Surface 3:2）為基準等比縮放
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1280);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.clearColor = true;
            ps.colorClearValue = Ground;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();

            // 場景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera", typeof(Camera));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ground;
            cam.orthographic = true;

            var go = new GameObject("Kiosk");
            var doc = go.AddComponent<UIDocument>();
            // 用 SerializedObject 寫入，確保參照會存進場景檔
            var docSo = new SerializedObject(doc);
            docSo.FindProperty("m_PanelSettings").objectReferenceValue = ps;
            docSo.ApplyModifiedPropertiesWithoutUndo();

            var shell = go.AddComponent<KioskShell>();
            shell.panelSettings = ps;
            shell.themeStyleSheet = theme;
            shell.styleSheet = uss;
            shell.fontRegular = regular;
            shell.fontMedium = medium != null ? medium : regular;
            shell.fontBold = bold;

            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(shell);
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var saved = new SerializedObject(doc).FindProperty("m_PanelSettings").objectReferenceValue;
            if (saved == null) Debug.LogError("[Kiosk] UIDocument 的 PanelSettings 沒有寫入成功，請手動把 " + PanelPath + " 拖到 Kiosk 物件的 UIDocument > Panel Settings");

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            // 展示機設定
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.forceSingleInstance = true;

            Selection.activeGameObject = go;
            Debug.Log("[Kiosk] 已建立 " + ScenePath + "，服務清單請編輯 StreamingAssets/" + KioskConfig.FileName);
        }
    }
}
