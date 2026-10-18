// 選單 MIRDC > 建立整合測試場景 (EXE + 網頁)
// 產生 Assets/Scenes/IntegrationTest.unity：
//   分頁 1  WinForm 應用      → ResponsiveTestApp.exe
//   分頁 2  本地網站          → WebHost.exe + http://localhost:3000/（自動在背景啟動 rwd-test-server.exe）
//   分頁 3  相機辨識網站      → WebHost.exe + https://uaitc.com:40056/（離開分頁即關閉，釋放鏡頭）
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using B = MIRDC.NativeEmbed.EditorTools.EmbedDemoSceneBuilder;

namespace MIRDC.NativeEmbed.EditorTools
{
    public static class EmbedIntegrationSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/IntegrationTest.unity";
        const string WinFormExe = "Apps/ResponsiveTestApp/ResponsiveTestApp.exe";
        const string WebHostExe = "Apps/WebHost/WebHost.exe";
        const string ServerExe  = "Apps/RwdServer/rwd-test-server.exe";
        const string LocalUrl   = "http://localhost:3000/";
        const string CameraUrl  = "https://uaitc.com:40056/";

        static readonly Color BarColor  = new Color(0.12f, 0.13f, 0.16f);
        static readonly Color AreaColor = new Color(0.20f, 0.22f, 0.26f);
        static readonly Color BgColor   = new Color(0.07f, 0.08f, 0.10f);
        static readonly Color ToolColor = new Color(0.30f, 0.32f, 0.38f);

        [MenuItem("MIRDC/建立整合測試場景 (EXE + 網頁)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = BgColor; }
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Canvas + EventSystem
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif

            // 上方列：分頁按鈕 + 網頁工具 + 狀態
            var bar = B.NewUI("TopBar", canvasGO.transform);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, 96); bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<Image>().color = BarColor;
            var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(24, 24, 16, 16); hlg.spacing = 12;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            var tabWin  = B.NewButton(bar, "WinForm 應用", font, 220);
            var tabWeb  = B.NewButton(bar, "本地網站", font, 220);
            var tabCam  = B.NewButton(bar, "相機辨識", font, 220);
            Spacer(bar, 24);
            var btnHome   = Tool(B.NewButton(bar, "首頁", font, 110));
            var btnBack   = Tool(B.NewButton(bar, "上一頁", font, 110));
            var btnReload = Tool(B.NewButton(bar, "重新整理", font, 130));
            var status = B.NewText(bar, "-", font, 22, TextAnchor.MiddleLeft);
            status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 三個顯示區域（同位置，切換時只開一個）
            var areaWin = NewArea(canvasGO.transform, "Area - WinForm", font);
            var areaWeb = NewArea(canvasGO.transform, "Area - LocalWeb", font);
            var areaCam = NewArea(canvasGO.transform, "Area - Camera", font);

            // 本地伺服器
            var serverGO = new GameObject("LocalServer - rwd-test-server");
            var server = serverGO.AddComponent<LocalServerProcess>();
            server.exePath = ServerExe;
            server.readyPort = 3000;

            // Hosts
            var hWin = NewHost("AppHost - WinForm", WinFormExe, "--embedded --parent-pid={pid}", areaWin, false);
            var hWeb = NewHost("AppHost - LocalWeb", WebHostExe,
                $"--embedded --parent-pid={{pid}} --kiosk --profile=local-rwd --url={LocalUrl}", areaWeb, true);
            var hCam = NewHost("AppHost - Camera", WebHostExe,
                $"--embedded --parent-pid={{pid}} --kiosk --profile=camera --url={CameraUrl}", areaCam, true);

            // 分頁切換
            var swGO = new GameObject("AppTabSwitcher");
            var sw = swGO.AddComponent<AppTabSwitcher>();
            sw.statusText = status;
            sw.server = server;
            sw.tabs.Add(new AppTabSwitcher.Tab { title = "WinForm 應用", host = hWin, area = areaWin, button = tabWin, preload = true });
            sw.tabs.Add(new AppTabSwitcher.Tab { title = "本地網站",     host = hWeb, area = areaWeb, button = tabWeb, preload = true });
            sw.tabs.Add(new AppTabSwitcher.Tab { title = "相機辨識",     host = hCam, area = areaCam, button = tabCam, preload = false, closeWhenHidden = true });

            UnityEventTools.AddPersistentListener(btnHome.onClick,   new UnityAction(sw.WebHome));
            UnityEventTools.AddPersistentListener(btnBack.onClick,   new UnityAction(sw.WebBack));
            UnityEventTools.AddPersistentListener(btnReload.onClick, new UnityAction(sw.WebReload));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;

            foreach (var rel in new[] { WinFormExe, WebHostExe, ServerExe })
                if (!File.Exists(Path.Combine(Application.streamingAssetsPath, rel)))
                    Debug.LogWarning($"[Integration] 缺少 StreamingAssets/{rel}");

            Selection.activeGameObject = swGO;
            Debug.Log($"[Integration] 已建立 {ScenePath}");
        }

        static EmbeddedAppHost NewHost(string name, string exe, string args, RectTransform area, bool web)
        {
            var go = new GameObject(name);
            var h = go.AddComponent<EmbeddedAppHost>();
            h.exePath = exe;
            h.arguments = args;
            h.targetRect = area;
            h.mode = EmbedMode.Child;
            h.redirectStdin = web;
            h.windowTimeout = web ? 30f : 15f; // WebView2 第一次建立 profile 較慢
            return h;
        }

        static RectTransform NewArea(Transform parent, string name, Font font)
        {
            var area = B.NewUI(name, parent);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(24, 24); area.offsetMax = new Vector2(-24, -120);
            area.gameObject.AddComponent<Image>().color = AreaColor;
            var hint = B.NewText(area, "載入中…", font, 32, TextAnchor.MiddleCenter);
            B.Stretch(hint.rectTransform);
            hint.color = new Color(1, 1, 1, 0.35f);
            return area;
        }

        static Button Tool(Button b) { b.targetGraphic.color = ToolColor; return b; }

        static void Spacer(RectTransform parent, float w)
        {
            var rt = B.NewUI("Spacer", parent);
            rt.gameObject.AddComponent<LayoutElement>().preferredWidth = w;
        }
    }
}
