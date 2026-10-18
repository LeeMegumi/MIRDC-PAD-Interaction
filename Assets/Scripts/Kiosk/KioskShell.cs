// KioskShell — 展示主程式（首頁 + 各服務頁）
// 介面：UI Toolkit（樣式在 Assets/UI/Kiosk.uss）
// 服務清單：StreamingAssets/kiosk-config.json
// 每個服務是一個 EmbeddedAppHost（exe 直接嵌入；網頁透過 WebHost.exe 嵌入），
// 原生視窗貼齊服務頁的內容區，上方的「首頁」列永遠由 Unity 繪製，所以隨時點得到。
using System;
using System.Collections.Generic;
using MIRDC.NativeEmbed;
using UnityEngine;
using UnityEngine.UIElements;

namespace MIRDC.Kiosk
{
    [ExecuteAlways] // 編輯模式也會畫出首頁，方便排版檢查（不會啟動任何程式）
    [RequireComponent(typeof(UIDocument))]
    public class KioskShell : MonoBehaviour
    {
        [Header("UI Toolkit 面板")]
        [Tooltip("UIDocument 沒有指定 PanelSettings 時會自動套用這個")]
        public PanelSettings panelSettings;
        [Tooltip("連 panelSettings 都沒有時，執行期自動建立面板所用的主題")]
        public ThemeStyleSheet themeStyleSheet;

        [Header("字型（Noto Sans TC）")]
        public Font fontRegular;
        public Font fontMedium;
        public Font fontBold;

        [Header("樣式")]
        public StyleSheet styleSheet;

        [Header("效能")]
        public int targetFrameRate = 60;

        [Tooltip("進入服務頁後，等轉場跑完才顯示原生視窗（秒）")]
        public float revealDelay = 0.22f;

        sealed class Svc
        {
            public ServiceEntry cfg;
            public EmbeddedAppHost host;
            public Color color;
            public Color ink;
        }

        static readonly Color Ink = new Color32(22, 25, 27, 255);
        static readonly Color White = Color.white;
        static readonly string[] Weekdays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        KioskConfig _cfg;
        readonly List<Svc> _svcs = new List<Svc>();
        readonly Dictionary<string, LocalServerProcess> _servers = new Dictionary<string, LocalServerProcess>();

        UIDocument _doc;
        VisualElement _root, _home, _service, _content, _homeBtn, _homeIcon, _toolBack, _toolReload, _retry, _activeScreen;
        Label _clock, _date, _barTitle, _homeLabel, _message;
        Color _homeInk = White;
        Svc _current;
        float _showAt;
        float _orgHoldStart = -1f;
        int _lastMinute = -1;

        // ============================================================ 啟動

        void Awake()
        {
            if (!Application.isPlaying) return;

            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = targetFrameRate;
            Application.runInBackground = true;

            LoadConfig();

            if (_cfg.fullscreen && !Application.isEditor)
            {
                var d = Display.main;
                Screen.SetResolution(d.systemWidth, d.systemHeight, FullScreenMode.FullScreenWindow);
            }
        }

        void LoadConfig()
        {
            _cfg = KioskConfig.Load(out string err);
            if (err != null) Debug.LogWarning("[Kiosk] " + err);

            _svcs.Clear();
            foreach (var s in _cfg.services ?? new ServiceEntry[0])
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                var svc = new Svc { cfg = s };
                if (!ColorUtility.TryParseHtmlString(s.color, out svc.color)) svc.color = new Color32(142, 90, 46, 255);
                svc.ink = Luma(svc.color) > 0.45f ? Ink : White;
                _svcs.Add(svc);
            }
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            _doc = GetComponent<UIDocument>();
            EnsurePanel();
            BuildUI();
            CreateServers();
            CreateHosts();
        }

        // ---- 編輯模式預覽：只畫介面，不啟動任何程式 ----
        bool _previewDirty = true;

        void OnEnable()
        {
            if (Application.isPlaying) return;
            _previewDirty = true;
#if UNITY_EDITOR
            // 編輯模式的 Update 不會自動跑，排一次延遲呼叫確保開場景就看得到
            UnityEditor.EditorApplication.delayCall += () => { if (this != null && !Application.isPlaying) EditorPreview(); };
#endif
        }

        void OnValidate()
        {
            if (!Application.isPlaying) _previewDirty = true;
        }

        void EditorPreview()
        {
            _previewDirty = false;
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;
            EnsurePanel();
            if (_doc.rootVisualElement == null) { _previewDirty = true; return; }
            LoadConfig();
            BuildUI();
        }

        /// <summary>確保 UIDocument 有 PanelSettings，否則 UI Toolkit 什麼都不會畫。</summary>
        void EnsurePanel()
        {
            if (_doc.panelSettings != null) return;
            if (panelSettings != null) { _doc.panelSettings = panelSettings; return; }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "KioskPanelSettings (runtime)";
            ps.themeStyleSheet = themeStyleSheet;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1280);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            _doc.panelSettings = ps;
            Debug.LogWarning("[Kiosk] UIDocument 沒有 PanelSettings，已在執行期自動建立。建議重新執行選單 MIRDC > 建立展示主程式場景。");
        }

        // ============================================================ UI

        void BuildUI()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.style.flexGrow = 1;
            if (styleSheet != null) _root.styleSheets.Add(styleSheet);

            var shell = new VisualElement();
            shell.AddToClassList("k-root");
            if (fontRegular != null) shell.style.unityFontDefinition = FontDefinition.FromFont(fontRegular);
            _root.Add(shell);

            _home = BuildHome();
            _service = BuildService();
            shell.Add(_service);
            shell.Add(_home);

            _service.style.display = DisplayStyle.None;
            _service.AddToClassList("k-screen--hidden");
            _activeScreen = _home;
            UpdateClock(true);
        }

        VisualElement BuildHome()
        {
            var home = new VisualElement();
            home.AddToClassList("k-screen");
            home.AddToClassList("k-home");

            // 左欄
            var side = new VisualElement();
            side.AddToClassList("k-side");

            var org = L(_cfg.organization, "k-org", fontMedium);
            org.RegisterCallback<PointerDownEvent>(e => _orgHoldStart = Time.unscaledTime);
            org.RegisterCallback<PointerUpEvent>(e => _orgHoldStart = -1f);
            org.RegisterCallback<PointerLeaveEvent>(e => _orgHoldStart = -1f);

            var hero = new VisualElement();
            hero.Add(L(_cfg.title, "k-title", fontBold));
            if (!string.IsNullOrEmpty(_cfg.lede)) hero.Add(L(_cfg.lede, "k-lede", fontRegular));

            var foot = new VisualElement();
            _clock = L("", "k-clock", fontBold);
            _date = L("", "k-date", fontRegular);
            foot.Add(_clock);
            foot.Add(_date);
            if (!string.IsNullOrEmpty(_cfg.colorNote)) foot.Add(L(_cfg.colorNote, "k-note", fontRegular));

            side.Add(org);
            side.Add(hero);
            side.Add(foot);
            home.Add(side);

            // 右側服務色帶
            var bands = new VisualElement();
            bands.AddToClassList("k-bands");
            int n = _svcs.Count;
            bool grid = n > 3;
            if (grid) bands.AddToClassList("k-bands--grid");
            int rows = grid ? Mathf.CeilToInt(n / 2f) : Mathf.Max(1, n);

            for (int i = 0; i < n; i++)
            {
                var svc = _svcs[i];
                var band = new VisualElement();
                band.AddToClassList("k-band");
                band.style.backgroundColor = svc.color;
                band.style.color = svc.ink;
                if (grid)
                {
                    band.style.height = Length.Percent(100f / rows);
                    if (n % 2 == 1 && i == n - 1) band.style.width = Length.Percent(100f); // 奇數個時最後一個佔滿
                }

                var text = new VisualElement();
                text.AddToClassList("k-band-text");
                text.Add(L(svc.cfg.name, "k-band-name", fontBold));
                if (!string.IsNullOrEmpty(svc.cfg.description)) text.Add(L(svc.cfg.description, "k-band-desc", fontRegular));
                band.Add(text);
                band.Add(L(KindText(svc.cfg), "k-band-kind", fontMedium));

                band.AddManipulator(new Clickable(() => OpenService(svc)));
                bands.Add(band);
            }

            if (n == 0)
            {
                var empty = new VisualElement();
                empty.AddToClassList("k-band");
                empty.style.backgroundColor = (Color)new Color32(211, 215, 214, 255);
                empty.Add(L("設定檔裡還沒有任何服務。請編輯 StreamingAssets/" + KioskConfig.FileName + "。", "k-band-desc", fontRegular));
                bands.Add(empty);
            }

            home.Add(bands);
            return home;
        }

        VisualElement BuildService()
        {
            var screen = new VisualElement();
            screen.AddToClassList("k-screen");
            screen.AddToClassList("k-service");

            var bar = new VisualElement();
            bar.AddToClassList("k-bar");

            _homeBtn = new VisualElement();
            _homeBtn.AddToClassList("k-home-btn");
            _homeIcon = new VisualElement();
            _homeIcon.AddToClassList("k-home-icon");
            _homeIcon.generateVisualContent += DrawHouse;
            _homeLabel = L("首頁", "k-home-label", fontBold);
            _homeBtn.Add(_homeIcon);
            _homeBtn.Add(_homeLabel);
            _homeBtn.AddManipulator(new Clickable(GoHome));

            _barTitle = L("", "k-bar-title", fontBold);
            _toolBack = Tool("上一頁", () => _current?.host.SendCommand("back"));
            _toolReload = Tool("重新整理", () => _current?.host.SendCommand("reload"));

            bar.Add(_homeBtn);
            bar.Add(_barTitle);
            bar.Add(_toolBack);
            bar.Add(_toolReload);

            _content = new VisualElement();
            _content.AddToClassList("k-content");
            _message = L("", "k-message", fontRegular);
            _retry = new VisualElement();
            _retry.AddToClassList("k-retry");
            _retry.Add(L("再試一次", "k-retry-label", fontMedium));
            _retry.AddManipulator(new Clickable(() => _current?.host.Launch()));
            _retry.AddToClassList("k-hidden");
            _content.Add(_message);
            _content.Add(_retry);

            screen.Add(bar);
            screen.Add(_content);
            return screen;
        }

        VisualElement Tool(string text, Action onClick)
        {
            var t = new VisualElement();
            t.AddToClassList("k-tool");
            t.Add(L(text, "k-tool-label", fontMedium));
            t.AddManipulator(new Clickable(onClick));
            return t;
        }

        Label L(string text, string cls, Font font)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            return l;
        }

        void DrawHouse(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            float sx = r.width / 40f, sy = r.height / 40f;
            var p = ctx.painter2D;
            p.fillColor = _homeInk;
            p.BeginPath();
            p.MoveTo(new Vector2(4 * sx, 19 * sy));
            p.LineTo(new Vector2(20 * sx, 5 * sy));
            p.LineTo(new Vector2(36 * sx, 19 * sy));
            p.LineTo(new Vector2(36 * sx, 36 * sy));
            p.LineTo(new Vector2(24 * sx, 36 * sy));
            p.LineTo(new Vector2(24 * sx, 25 * sy));
            p.LineTo(new Vector2(16 * sx, 25 * sy));
            p.LineTo(new Vector2(16 * sx, 36 * sy));
            p.LineTo(new Vector2(4 * sx, 36 * sy));
            p.ClosePath();
            p.Fill();
        }

        static string KindText(ServiceEntry s)
        {
            if (!string.IsNullOrEmpty(s.kind)) return s.kind;
            string k = s.IsWeb ? "網站" : "應用程式";
            return s.usesCamera ? k + "・使用鏡頭" : k;
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // ============================================================ 畫面切換

        void ShowScreen(VisualElement show, VisualElement hide)
        {
            _activeScreen = show;
            show.style.display = DisplayStyle.Flex;
            show.BringToFront();
            show.AddToClassList("k-screen--hidden");
            show.schedule.Execute(() => { if (_activeScreen == show) show.RemoveFromClassList("k-screen--hidden"); }).StartingIn(16);

            hide.AddToClassList("k-screen--hidden");
            hide.schedule.Execute(() => { if (_activeScreen != hide) hide.style.display = DisplayStyle.None; }).StartingIn(200);
        }

        void OpenService(Svc svc)
        {
            if (_current == svc) return;
            _current = svc;
            _showAt = Time.unscaledTime + revealDelay;

            _barTitle.text = svc.cfg.name;
            _homeBtn.style.backgroundColor = svc.color;
            _homeInk = svc.ink;
            _homeLabel.style.color = svc.ink;
            _homeIcon.MarkDirtyRepaint();
            bool web = svc.cfg.IsWeb;
            _toolBack.EnableInClassList("k-hidden", !web);
            _toolReload.EnableInClassList("k-hidden", !web);
            UpdateMessage();

            ShowScreen(_service, _home);

            if (!string.IsNullOrEmpty(svc.cfg.server) && _servers.TryGetValue(svc.cfg.server, out var srv) && !srv.IsReady)
                srv.StartServer();
            svc.host.Launch();
        }

        public void GoHome()
        {
            if (_current == null) return;
            var prev = _current;
            _current = null;                         // 原生視窗由 screenRectProvider 自動隱藏
            if (prev.cfg.closeWhenHidden) prev.host.Close();
            if (prev.cfg.IsWeb && !prev.cfg.closeWhenHidden) prev.host.SendCommand("home"); // 下一位觀眾從網站首頁開始
            ShowScreen(_home, _service);
        }

        Rect ProvideRect(Svc s)
        {
            if (_current != s || Time.unscaledTime < _showAt || _content == null || _root == null) return Rect.zero;
            Rect rootWb = _root.worldBound;
            if (rootWb.width <= 0 || rootWb.height <= 0) return Rect.zero;
            Rect wb = _content.worldBound;
            float sx = Screen.width / rootWb.width, sy = Screen.height / rootWb.height;
            float w = wb.width * sx, h = wb.height * sy;
            return new Rect(wb.x * sx, Screen.height - wb.y * sy - h, w, h);
        }

        // ============================================================ 服務程式

        void CreateServers()
        {
            foreach (var s in _cfg.servers ?? new ServerEntry[0])
            {
                if (s == null || string.IsNullOrEmpty(s.id) || string.IsNullOrEmpty(s.path)) continue;
                var go = new GameObject("Server - " + s.id);
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                var p = go.AddComponent<LocalServerProcess>();
                p.exePath = s.path;
                p.arguments = s.args ?? "";
                p.readyPort = s.port;
                p.startOnAwake = true;
                go.SetActive(true);
                _servers[s.id] = p;
            }
        }

        void CreateHosts()
        {
            foreach (var svc in _svcs)
            {
                var c = svc.cfg;
                var go = new GameObject("Service - " + (string.IsNullOrEmpty(c.id) ? c.name : c.id));
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                var h = go.AddComponent<EmbeddedAppHost>();
                h.mode = EmbedMode.Child;
                h.launchOnStart = false;
                if (c.IsWeb)
                {
                    h.exePath = _cfg.webHostPath;
                    h.arguments = WebArgs(c);
                    h.redirectStdin = true;
                    h.windowTimeout = 30f;
                }
                else
                {
                    h.exePath = c.path;
                    h.arguments = string.IsNullOrEmpty(c.args) ? "--embedded --parent-pid={pid}" : c.args;
                }
                var captured = svc;
                h.screenRectProvider = () => ProvideRect(captured);
                go.SetActive(true);
                svc.host = h;

                if (c.preload && !c.closeWhenHidden) h.Launch();
            }
        }

        static string WebArgs(ServiceEntry c)
        {
            string profile = !string.IsNullOrEmpty(c.profile) ? c.profile : (!string.IsNullOrEmpty(c.id) ? c.id : "default");
            var parts = new List<string> { "--embedded", "--parent-pid={pid}", "--kiosk", Arg("profile", profile), Arg("url", c.url) };
            if (!string.IsNullOrEmpty(c.camera)) parts.Add(Arg("camera", c.camera));
            if (!string.IsNullOrEmpty(c.extraArgs)) parts.Add(c.extraArgs);
            return string.Join(" ", parts);
        }

        static string Arg(string name, string value)
        {
            value = value ?? "";
            string a = "--" + name + "=" + value;
            return value.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a;
        }

        // ============================================================ 每幀

        void Update()
        {
            if (!Application.isPlaying)
            {
                // UIDocument 在編輯模式下常會重建視覺樹（改 Inspector、重新編譯），被清空就重畫
                if (_previewDirty || _root == null || _root.panel == null || _root.childCount == 0) EditorPreview();
                return;
            }

            UpdateClock(false);

            if (_current != null)
            {
                UpdateMessage();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (_cfg.idleReturnSeconds > 0 && Win32.SystemIdleSeconds() > _cfg.idleReturnSeconds) GoHome();
#endif
            }

            if (_cfg.exitHoldSeconds > 0 && _orgHoldStart > 0 && Time.unscaledTime - _orgHoldStart > _cfg.exitHoldSeconds)
            {
                _orgHoldStart = -1f;
                Debug.Log("[Kiosk] 管理者結束程式");
                Application.Quit();
            }
        }

        void UpdateClock(bool force)
        {
            if (_clock == null) return;
            var now = DateTime.Now;
            if (!force && now.Minute == _lastMinute) return;
            _lastMinute = now.Minute;
            _clock.text = now.ToString("HH:mm");
            _date.text = $"{now.Month}月{now.Day}日 {Weekdays[(int)now.DayOfWeek]}";
        }

        void UpdateMessage()
        {
            if (_current == null) return;
            var h = _current.host;
            string name = _current.cfg.name;
            bool failed = false;
            string msg;

            if (h == null) msg = $"正在開啟「{name}」…";
            else switch (h.State)
            {
                case HostState.Failed:
                    msg = $"「{name}」沒有正常開啟。";
                    failed = true;
                    break;
                case HostState.Restarting:
                    msg = $"「{name}」意外關閉，正在重新開啟。";
                    break;
                default:
                    msg = $"正在開啟「{name}」…";
                    if (!string.IsNullOrEmpty(_current.cfg.server) && _servers.TryGetValue(_current.cfg.server, out var srv) && !srv.IsReady)
                        msg = "正在啟動網站伺服器…";
                    break;
            }

            if (_message.text != msg) _message.text = msg;
            _retry.EnableInClassList("k-hidden", !failed);
        }
    }
}
