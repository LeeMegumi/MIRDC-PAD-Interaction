// 選單 MIRDC > 建立 EXE 嵌入測試場景
// 自動產生 Assets/Scenes/EmbedTest.unity：上方控制列 + 嵌入區域 + EmbeddedAppHost
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MIRDC.NativeEmbed.EditorTools
{
    public static class EmbedDemoSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/EmbedTest.unity";
        const string ExeRel = "Apps/ResponsiveTestApp/ResponsiveTestApp.exe";

        static readonly Color BarColor   = new Color(0.12f, 0.13f, 0.16f);
        static readonly Color AreaColor  = new Color(0.20f, 0.22f, 0.26f);
        static readonly Color BtnColor   = new Color(0.25f, 0.45f, 0.85f);
        static readonly Color BgColor    = new Color(0.07f, 0.08f, 0.10f);

        [MenuItem("MIRDC/建立 EXE 嵌入測試場景")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = BgColor; }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Canvas
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // EventSystem
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif

            // 上方控制列
            var bar = NewUI("TopBar", canvasGO.transform);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, 96); bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<Image>().color = BarColor;
            var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(24, 24, 16, 16); hlg.spacing = 16;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            var bLaunch = NewButton(bar, "啟動 / 顯示", font);
            var bToggle = NewButton(bar, "顯示 / 隱藏", font);
            var bLayout = NewButton(bar, "切換版面", font);
            var bClose  = NewButton(bar, "關閉程式", font);

            var status = NewText(bar, "狀態：-", font, 24, TextAnchor.MiddleLeft);
            var le = status.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1;

            // 嵌入區域（exe 視窗會貼齊這個範圍）
            var area = NewUI("EmbedArea", canvasGO.transform);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(24, 24); area.offsetMax = new Vector2(-24, -120);
            area.gameObject.AddComponent<Image>().color = AreaColor;
            var hint = NewText(area, "EXE 會顯示在這個區域\n（按「啟動 / 顯示」）", font, 32, TextAnchor.MiddleCenter);
            Stretch(hint.rectTransform);
            hint.color = new Color(1, 1, 1, 0.35f);

            // Host
            var hostGO = new GameObject("AppHost - ResponsiveTestApp");
            var host = hostGO.AddComponent<EmbeddedAppHost>();
            host.exePath = ExeRel;
            host.arguments = "--embedded --parent-pid={pid}";
            host.targetRect = area;
            host.mode = EmbedMode.Child;

            var demo = hostGO.AddComponent<EmbedDemoController>();
            demo.host = host; demo.embedArea = area; demo.statusText = status;

            UnityEventTools.AddPersistentListener(bLaunch.onClick, new UnityAction(demo.OnLaunch));
            UnityEventTools.AddPersistentListener(bToggle.onClick, new UnityAction(demo.OnToggleVisible));
            UnityEventTools.AddPersistentListener(bLayout.onClick, new UnityAction(demo.OnCycleLayout));
            UnityEventTools.AddPersistentListener(bClose.onClick,  new UnityAction(demo.OnClose));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 加到 Build Settings 第一個
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            // exe 拿焦點時 Unity 必須持續運作；方便測試先開可縮放視窗
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;

            string exeFull = Path.Combine(Application.streamingAssetsPath, ExeRel);
            if (!File.Exists(exeFull))
                Debug.LogWarning($"[EmbedDemo] 找不到 {exeFull}，請把 ResponsiveTestApp.exe 放到 StreamingAssets/{ExeRel}");

            Selection.activeGameObject = hostGO;
            Debug.Log($"[EmbedDemo] 已建立 {ScenePath}");
        }

        // ---- helpers（EmbedIntegrationSceneBuilder 也會用） ----
        internal static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        internal static Text NewText(RectTransform parent, string text, Font font, int size, TextAnchor align)
        {
            var rt = NewUI("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = Color.white; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        internal static Button NewButton(RectTransform parent, string label, Font font, float width = 200)
        {
            var rt = NewUI("Btn " + label, parent);
            rt.gameObject.AddComponent<Image>().color = BtnColor;
            var btn = rt.gameObject.AddComponent<Button>();
            var le = rt.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width;
            var t = NewText(rt, label, font, 26, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return btn;
        }
    }
}
