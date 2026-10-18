// EmbeddedAppHost
// 啟動一支外部 Windows exe，把它的視窗嵌進 Unity 畫面中某個 RectTransform 的範圍，
// 並持續同步位置/大小、顯示/隱藏、當掉自動重啟。
//
// 兩種模式：
//   Child   : SetParent 成為 Unity 主視窗的子視窗（正式版建議，跟著 Unity 視窗走）
//   Overlay : 保持 top-level，只設 owner 並精準疊在 Unity 上方（備案；Editor 一律用這個）
//
// 抓 HWND 的方式：
//   1. 讀 stdout 的 "HWND=0x...."（金屬中心的 WinForm 在 --embedded 模式會輸出）
//   2. 沒有輸出的 exe → 依 PID 列舉 top-level 視窗，取面積最大的可見視窗
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

namespace MIRDC.NativeEmbed
{
    public enum EmbedMode { Child, Overlay }
    public enum HostState { Idle, Starting, Running, Restarting, Failed }

    [DisallowMultipleComponent]
    public class EmbeddedAppHost : MonoBehaviour
    {
        [Header("執行檔")]
        [Tooltip("相對於 StreamingAssets 的路徑，或絕對路徑")]
        public string exePath = "Apps/ResponsiveTestApp/ResponsiveTestApp.exe";
        [Tooltip("啟動參數。{pid} 會替換成 Unity 自己的 Process ID")]
        public string arguments = "--embedded --parent-pid={pid}";
        [Tooltip("讀取 stdout 的 HWND=0x... 。關閉則一律用 PID 列舉視窗")]
        public bool readHwndFromStdout = true;
        [Tooltip("沒收到 stdout HWND 時，幾秒後開始改用 PID 找視窗")]
        public float fallbackFindAfter = 2f;
        [Tooltip("開啟 stdin，可用 SendCommand() 傳指令給 exe（WebHost 用：home / reload / back / nav <url>）")]
        public bool redirectStdin = false;

        [Header("顯示區域")]
        [Tooltip("exe 視窗要貼齊的 UI 區域")]
        public RectTransform targetRect;
        [Tooltip("Canvas 為 Screen Space - Camera / World 時指定；Overlay Canvas 留空")]
        public Camera uiCamera;
        public EmbedMode mode = EmbedMode.Child;
        [Tooltip("targetRect 被 SetActive(false) 時自動隱藏 exe 視窗")]
        public bool followTargetActive = true;

        [Header("生命週期")]
        public bool launchOnStart = false;
        [Tooltip("超過秒數仍找不到視窗則視為失敗")]
        public float windowTimeout = 15f;
        public bool autoRestart = true;
        public float restartDelay = 2f;
        public int maxRestarts = 5;
        [Tooltip("把 exe 的 stdout/stderr 印到 Console")]
        public bool verboseLog = true;

        [Header("事件")]
        public UnityEvent onAttached;
        public UnityEvent onExited;

        /// <summary>
        /// 由程式提供顯示範圍（Unity 螢幕座標，左下原點，像素）。設定後取代 targetRect；
        /// 回傳寬高為 0 的 Rect 表示目前不該顯示。UI Toolkit 介面用這個。
        /// </summary>
        public Func<Rect> screenRectProvider;

        public HostState State { get; private set; } = HostState.Idle;
        public string StatusText { get; private set; } = "未啟動";
        public bool IsVisible => _wantVisible;
        public IntPtr ChildHwnd => _child;
        public event Action<HostState, string> StateChanged;

        // ---- runtime ----
        Process _proc;
        IntPtr _child = IntPtr.Zero;
        IntPtr _host = IntPtr.Zero;
        long _reportedHwnd;                 // 由 stdout 執行緒寫入
        readonly ConcurrentQueue<string> _log = new ConcurrentQueue<string>();
        float _launchTime;
        bool _wantVisible = true;
        bool _appliedVisible;
        bool _userClosed;
        bool _closing, _relaunchAfterClose;
        int _restartCount;
        float _runningSince;
        Win32.RECT _lastRect;
        bool _hasLastRect;
        EmbedMode _activeMode;
        Coroutine _restartCo;

        static readonly Regex HwndRegex = new Regex(@"HWND=0x([0-9A-Fa-f]+)", RegexOptions.Compiled);
        static readonly Vector3[] s_corners = new Vector3[4];

        // ================= Public API =================

        /// <summary>啟動（若已在執行則只顯示）。</summary>
        public void Launch()
        {
            _wantVisible = true;
            if (_closing) { _relaunchAfterClose = true; return; } // 關閉中又被要求啟動 → 關完再開
            if (IsProcessAlive()) return;
            _userClosed = false;
            _restartCount = 0;
            StartProcess();
        }

        public void Show() { _wantVisible = true; }
        public void Hide() { _wantVisible = false; }
        public void ToggleVisible() { _wantVisible = !_wantVisible; }

        /// <summary>正常關閉（送 WM_CLOSE，逾時強制結束）。</summary>
        public void Close()
        {
            _userClosed = true;
            if (_restartCo != null) { StopCoroutine(_restartCo); _restartCo = null; }
            if (_closing) { _relaunchAfterClose = false; return; }
            if (!IsProcessAlive()) { Cleanup(); SetState(HostState.Idle, "已關閉"); return; }
            StartCoroutine(CloseRoutine());
        }

        /// <summary>強制重新同步位置（例如手動改了 Canvas 之後）。</summary>
        public void ForceResync() { _hasLastRect = false; }

        public bool IsRunning => State == HostState.Running && IsProcessAlive();

        /// <summary>透過 stdin 送一行指令給 exe（需勾選 redirectStdin）。</summary>
        public bool SendCommand(string command)
        {
            if (!redirectStdin || !IsProcessAlive()) return false;
            try
            {
                _proc.StandardInput.WriteLine(command);
                _proc.StandardInput.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppHost:{name}] SendCommand 失敗：{ex.Message}");
                return false;
            }
        }

        // ================= Unity =================

        void Start()
        {
            Application.runInBackground = true; // exe 拿到焦點時 Unity 仍要持續更新
            if (launchOnStart) Launch();
        }

        void Update()
        {
            while (_log.TryDequeue(out var line))
                if (verboseLog) Debug.Log($"[AppHost:{name}] {line}");

            switch (State)
            {
                case HostState.Starting: TickStarting(); break;
                case HostState.Running:  TickRunning();  break;
            }
        }

        void LateUpdate()
        {
            if (State == HostState.Running) SyncRect(false);
        }

        void OnDisable()
        {
            // 離開 Play / 關閉場景時一定要把子程序收掉
            _userClosed = true;
            KillNow();
        }

        void OnApplicationQuit() { _userClosed = true; KillNow(); }

        // ================= Process =================

        string ResolveExePath()
        {
            if (string.IsNullOrWhiteSpace(exePath)) return null;
            return Path.IsPathRooted(exePath) ? exePath : Path.Combine(Application.streamingAssetsPath, exePath);
        }

        void StartProcess()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string path = ResolveExePath();
            if (path == null || !File.Exists(path)) { Fail($"找不到執行檔：{path}"); return; }

            string args = (arguments ?? "").Replace("{pid}", Process.GetCurrentProcess().Id.ToString());
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = readHwndFromStdout,
                RedirectStandardError = readHwndFromStdout,
                RedirectStandardInput = redirectStdin,
            };

            try
            {
                Interlocked.Exchange(ref _reportedHwnd, 0);
                _child = IntPtr.Zero;
                _hasLastRect = false;
                _appliedVisible = false;

                _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (readHwndFromStdout)
                {
                    _proc.OutputDataReceived += OnStdout;
                    _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _log.Enqueue("stderr: " + e.Data); };
                }
                _proc.Start();
                if (readHwndFromStdout) { _proc.BeginOutputReadLine(); _proc.BeginErrorReadLine(); }

                _launchTime = Time.realtimeSinceStartup;
                SetState(HostState.Starting, $"啟動中… PID {_proc.Id}");
                _log.Enqueue($"Start \"{path}\" {args}");
            }
            catch (Exception ex)
            {
                Fail("啟動失敗：" + ex.Message);
            }
#else
            Fail("僅支援 Windows");
#endif
        }

        void OnStdout(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            _log.Enqueue("stdout: " + e.Data);
            var m = HwndRegex.Match(e.Data);
            if (m.Success && long.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out long h))
                Interlocked.Exchange(ref _reportedHwnd, h);
        }

        bool IsProcessAlive()
        {
            try { return _proc != null && !_proc.HasExited; }
            catch { return false; }
        }

        IEnumerator CloseRoutine()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _closing = true;
            _relaunchAfterClose = false;
            SetState(State, "關閉中…");
            if (_child != IntPtr.Zero && Win32.IsWindow(_child))
                Win32.PostMessage(_child, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            float t = Time.realtimeSinceStartup;
            while (IsProcessAlive() && Time.realtimeSinceStartup - t < 2f) yield return null;
#endif
            KillNow();
            _closing = false;
            SetState(HostState.Idle, "已關閉");
            if (_relaunchAfterClose)
            {
                _relaunchAfterClose = false;
                _userClosed = false;
                _restartCount = 0;
                StartProcess();
            }
            yield break;
        }

        void KillNow()
        {
            try
            {
                if (IsProcessAlive())
                {
                    _proc.Kill();
                    _proc.WaitForExit(500);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[AppHost:{name}] Kill 失敗：{ex.Message}"); }
            Cleanup();
        }

        void Cleanup()
        {
            try { _proc?.Dispose(); } catch { }
            _proc = null;
            _child = IntPtr.Zero;
            _hasLastRect = false;
        }

        // ================= State ticks =================

        void TickStarting()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!IsProcessAlive()) { OnProcessExited(); return; }

            float elapsed = Time.realtimeSinceStartup - _launchTime;
            IntPtr hwnd = new IntPtr(Interlocked.Read(ref _reportedHwnd));

            if (hwnd == IntPtr.Zero && (!readHwndFromStdout || elapsed > fallbackFindAfter))
                hwnd = Win32.FindMainWindow(_proc.Id);

            if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd)) { Attach(hwnd); return; }

            if (elapsed > windowTimeout) Fail($"{windowTimeout:0} 秒內找不到 exe 視窗");
#endif
        }

        void TickRunning()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!IsProcessAlive() || !Win32.IsWindow(_child)) { OnProcessExited(); return; }

            // 穩定執行 30 秒後重置重啟次數
            if (_restartCount > 0 && Time.realtimeSinceStartup - _runningSince > 30f) _restartCount = 0;

            bool targetOk;
            if (screenRectProvider != null)
            {
                Rect pr = screenRectProvider();
                targetOk = pr.width > 1 && pr.height > 1;
            }
            else targetOk = targetRect != null && (!followTargetActive || targetRect.gameObject.activeInHierarchy);
            bool visible = _wantVisible && targetOk;
            if (visible != _appliedVisible)
            {
                Win32.ShowWindow(_child, visible ? Win32.SW_SHOWNA : Win32.SW_HIDE);
                _appliedVisible = visible;
                if (visible) _hasLastRect = false;
                StatusText = visible ? "執行中" : "已隱藏";
                StateChanged?.Invoke(State, StatusText);
            }
#endif
        }

        void OnProcessExited()
        {
            int code = 0;
            try { code = _proc?.ExitCode ?? 0; } catch { }
            Cleanup();
            onExited?.Invoke();

            if (!_userClosed && autoRestart && _restartCount < maxRestarts)
            {
                _restartCount++;
                SetState(HostState.Restarting, $"程式結束 (code {code})，{restartDelay:0} 秒後重啟 ({_restartCount}/{maxRestarts})");
                _restartCo = StartCoroutine(RestartAfter(restartDelay));
            }
            else
            {
                SetState(HostState.Idle, $"程式已結束 (code {code})");
            }
        }

        IEnumerator RestartAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _restartCo = null;
            if (!_userClosed) StartProcess();
        }

        void Fail(string msg)
        {
            Debug.LogError($"[AppHost:{name}] {msg}");
            KillNow();
            SetState(HostState.Failed, msg);
        }

        void SetState(HostState s, string text)
        {
            State = s;
            StatusText = text;
            StateChanged?.Invoke(s, text);
        }

        // ================= Window embedding =================

        void Attach(IntPtr hwnd)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _child = hwnd;
            _activeMode = Application.isEditor ? EmbedMode.Overlay : mode;
            _host = FindUnityWindow();
            if (_host == IntPtr.Zero) { Fail("找不到 Unity 主視窗 HWND"); return; }

            // 先藏起來再改樣式，避免閃爍與工作列殘留按鈕
            Win32.ShowWindow(_child, Win32.SW_HIDE);

            long style = Win32.GetWindowLong(_child, Win32.GWL_STYLE);
            style &= ~(Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
            long ex = Win32.GetWindowLong(_child, Win32.GWL_EXSTYLE);
            ex &= ~(Win32.WS_EX_APPWINDOW | Win32.WS_EX_DLGMODALFRAME | Win32.WS_EX_WINDOWEDGE | Win32.WS_EX_CLIENTEDGE | Win32.WS_EX_STATICEDGE);

            if (_activeMode == EmbedMode.Child)
            {
                // 父視窗加 WS_CLIPCHILDREN，避免 Unity 畫面蓋過子視窗
                long hostStyle = Win32.GetWindowLong(_host, Win32.GWL_STYLE);
                if ((hostStyle & Win32.WS_CLIPCHILDREN) == 0)
                    Win32.SetWindowLong(_host, Win32.GWL_STYLE, hostStyle | Win32.WS_CLIPCHILDREN);

                style = (style & ~Win32.WS_POPUP) | Win32.WS_CHILD | Win32.WS_CLIPSIBLINGS;
                Win32.SetWindowLong(_child, Win32.GWL_STYLE, style);
                Win32.SetWindowLong(_child, Win32.GWL_EXSTYLE, ex);
                if (Win32.SetParent(_child, _host) == IntPtr.Zero)
                    Debug.LogWarning($"[AppHost:{name}] SetParent 失敗 (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})，可改用 Overlay 模式");
            }
            else
            {
                style = (style & ~Win32.WS_CHILD) | Win32.WS_POPUP;
                ex |= Win32.WS_EX_TOOLWINDOW; // 不出現在工作列 / Alt-Tab
                Win32.SetWindowLong(_child, Win32.GWL_STYLE, style);
                Win32.SetWindowLong(_child, Win32.GWL_EXSTYLE, ex);
                Win32.SetWindowLong(_child, Win32.GWLP_HWNDPARENT, _host.ToInt64()); // owner：永遠在 Unity 之上
            }

            _hasLastRect = false;
            _appliedVisible = false;
            _runningSince = Time.realtimeSinceStartup;
            SetState(HostState.Running, $"執行中 ({_activeMode}) HWND 0x{_child.ToInt64():X}");
            SyncRect(true);
            onAttached?.Invoke();
#endif
        }

        IntPtr FindUnityWindow()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int pid = Process.GetCurrentProcess().Id;
            if (Application.isEditor)
            {
                IntPtr h = Win32.FindMainWindow(pid, "UnityContainerWndClass");
                return h != IntPtr.Zero ? h : Process.GetCurrentProcess().MainWindowHandle;
            }
            IntPtr p = Win32.FindMainWindow(pid, "UnityWndClass");
            return p != IntPtr.Zero ? p : Win32.FindMainWindow(pid);
#else
            return IntPtr.Zero;
#endif
        }

        /// <summary>把 targetRect 換算成 Win32 座標並套用到子視窗。</summary>
        void SyncRect(bool force)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_child == IntPtr.Zero || (targetRect == null && screenRectProvider == null)) return;
            if (!force && !_appliedVisible) return;
            if (!TryComputeWin32Rect(out Win32.RECT r)) return;

            if (!force && _hasLastRect &&
                r.Left == _lastRect.Left && r.Top == _lastRect.Top &&
                r.Right == _lastRect.Right && r.Bottom == _lastRect.Bottom) return;

            uint flags = Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER | Win32.SWP_FRAMECHANGED;
            if (_activeMode == EmbedMode.Child) flags |= Win32.SWP_NOZORDER;
            Win32.SetWindowPos(_child, Win32.HWND_TOP, r.Left, r.Top, Mathf.Max(1, r.Width), Mathf.Max(1, r.Height), flags);
            _lastRect = r;
            _hasLastRect = true;
#endif
        }

        bool TryComputeWin32Rect(out Win32.RECT r)
        {
            r = default;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // 1) 目標範圍 -> Unity 螢幕座標（左下原點，單位 = Screen.width/height）
            Vector2 min, max;
            if (screenRectProvider != null)
            {
                Rect pr = screenRectProvider();
                if (pr.width <= 1 || pr.height <= 1) return false;
                min = pr.min; max = pr.max;
            }
            else
            {
                var canvas = targetRect.GetComponentInParent<Canvas>();
                Camera cam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = uiCamera != null ? uiCamera : canvas.worldCamera;

                targetRect.GetWorldCorners(s_corners);
                min = RectTransformUtility.WorldToScreenPoint(cam, s_corners[0]);
                max = RectTransformUtility.WorldToScreenPoint(cam, s_corners[2]);
            }
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);

            // 2) Unity 螢幕 -> 實際像素區域（Player: Unity 視窗 client；Editor: Game View 畫面）
            Rect dst;
#if UNITY_EDITOR
            if (!EditorGameViewUtil.TryGetRenderRectOnScreen(out dst)) return false;
#else
            if (!Win32.GetClientRect(_host, out Win32.RECT client)) return false;
            dst = new Rect(0, 0, client.Width, client.Height);
            if (_activeMode == EmbedMode.Overlay)
            {
                var p = new Win32.POINT();
                Win32.ClientToScreen(_host, ref p);
                dst.x = p.X; dst.y = p.Y;
            }
#endif
            float sx = dst.width / sw, sy = dst.height / sh;
            r.Left   = Mathf.RoundToInt(dst.x + min.x * sx);
            r.Right  = Mathf.RoundToInt(dst.x + max.x * sx);
            r.Top    = Mathf.RoundToInt(dst.y + (sh - max.y) * sy);
            r.Bottom = Mathf.RoundToInt(dst.y + (sh - min.y) * sy);
            return r.Width > 0 && r.Height > 0;
#else
            return false;
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor 下取得 Game View 實際畫面在螢幕上的像素範圍。</summary>
    internal static class EditorGameViewUtil
    {
        static Type s_type;
        static System.Reflection.PropertyInfo s_viewInWindow, s_targetInView;

        public static bool TryGetRenderRectOnScreen(out Rect rect)
        {
            rect = default;
            const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            if (s_type == null)
            {
                s_type = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (s_type == null) return false;
                s_viewInWindow = s_type.GetProperty("viewInWindow", BF);
                s_targetInView = s_type.GetProperty("targetInView", BF);
            }

            var all = Resources.FindObjectsOfTypeAll(s_type);
            if (all == null || all.Length == 0) return false;
            var gv = (UnityEditor.EditorWindow)all[0];
            foreach (var o in all) { var w = (UnityEditor.EditorWindow)o; if (w.hasFocus) { gv = w; break; } }

            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
            Rect pos = gv.position; // 螢幕座標（points，左上原點）

            Rect view = new Rect(0, 21, pos.width, pos.height - 21);
            if (s_viewInWindow != null && s_viewInWindow.PropertyType == typeof(Rect))
                view = (Rect)s_viewInWindow.GetValue(gv);

            Rect target;
            if (s_targetInView != null && s_targetInView.PropertyType == typeof(Rect))
            {
                target = (Rect)s_targetInView.GetValue(gv);
            }
            else
            {
                // 退而求其次：假設 Game View 等比縮放置中
                float vw = view.width * ppp, vh = view.height * ppp;
                float scale = Mathf.Min(1f, Mathf.Min(vw / Screen.width, vh / Screen.height));
                float tw = Screen.width * scale / ppp, th = Screen.height * scale / ppp;
                target = new Rect((view.width - tw) * 0.5f, (view.height - th) * 0.5f, tw, th);
            }

            rect = new Rect(
                (pos.x + view.x + target.x) * ppp,
                (pos.y + view.y + target.y) * ppp,
                target.width * ppp,
                target.height * ppp);
            return rect.width > 1 && rect.height > 1;
        }
    }
#endif
}
