// LocalServerProcess
// 在背景（無主控台視窗）啟動本地網站伺服器 exe，偵測 port 開好後 IsReady = true。
// 若 port 已經有人在聽（例如手動先開了伺服器），預設直接沿用，不重複啟動。
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MIRDC.NativeEmbed
{
    [DefaultExecutionOrder(-100)] // 比 EmbeddedAppHost 早啟動
    public class LocalServerProcess : MonoBehaviour
    {
        [Tooltip("相對於 StreamingAssets 的路徑，或絕對路徑")]
        public string exePath = "Apps/RwdServer/rwd-test-server.exe";
        public string arguments = "";
        [Tooltip("伺服器就緒時會開啟的 TCP port")]
        public int readyPort = 3000;
        public bool startOnAwake = true;
        [Tooltip("port 已被占用時視為伺服器已在執行，不再啟動")]
        public bool reuseIfPortInUse = true;
        public bool autoRestart = true;
        public float restartDelay = 2f;
        public bool verboseLog = true;

        public bool IsReady { get; private set; }
        public bool IsExternal { get; private set; }
        public string StatusText { get; private set; } = "未啟動";

        Process _proc;
        readonly ConcurrentQueue<string> _log = new ConcurrentQueue<string>();
        bool _stopping;
        float _nextProbe, _restartAt = -1;

        void Awake()
        {
            if (startOnAwake) StartServer();
        }

        public void StartServer()
        {
            _stopping = false;
            if (IsAlive()) return;

            if (reuseIfPortInUse && PortOpen(readyPort))
            {
                IsReady = true; IsExternal = true;
                StatusText = $"沿用已在執行的伺服器 (port {readyPort})";
                Debug.Log($"[Server:{name}] {StatusText}");
                return;
            }

            string path = Path.IsPathRooted(exePath) ? exePath : Path.Combine(Application.streamingAssetsPath, exePath);
            if (!File.Exists(path)) { StatusText = "找不到 " + path; Debug.LogError($"[Server:{name}] {StatusText}"); return; }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments ?? "",
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _proc.OutputDataReceived += (s, e) => { if (e.Data != null) _log.Enqueue(e.Data); };
                _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _log.Enqueue("stderr: " + e.Data); };
                _proc.Start();
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                IsReady = false; IsExternal = false;
                StatusText = $"啟動中… PID {_proc.Id}";
            }
            catch (Exception ex)
            {
                StatusText = "啟動失敗：" + ex.Message;
                Debug.LogError($"[Server:{name}] {StatusText}");
            }
        }

        public void StopServer()
        {
            _stopping = true;
            IsReady = false;
            try { if (IsAlive()) { _proc.Kill(); _proc.WaitForExit(1000); } } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null;
            StatusText = "已停止";
        }

        void Update()
        {
            while (_log.TryDequeue(out var line))
                if (verboseLog && !string.IsNullOrWhiteSpace(line)) Debug.Log($"[Server:{name}] {line}");

            if (IsExternal || _stopping) return;

            if (_proc != null && !IsAlive())
            {
                IsReady = false;
                try { _proc.Dispose(); } catch { }
                _proc = null;
                StatusText = "伺服器已結束";
                if (autoRestart) _restartAt = Time.realtimeSinceStartup + restartDelay;
            }

            if (_proc == null && _restartAt > 0 && Time.realtimeSinceStartup >= _restartAt)
            {
                _restartAt = -1;
                StartServer();
            }

            if (_proc != null && !IsReady && Time.realtimeSinceStartup >= _nextProbe)
            {
                _nextProbe = Time.realtimeSinceStartup + 0.5f;
                if (PortOpen(readyPort)) { IsReady = true; StatusText = $"就緒 (port {readyPort})"; Debug.Log($"[Server:{name}] {StatusText}"); }
            }
        }

        void OnDisable() => StopServer();
        void OnApplicationQuit() => StopServer();

        bool IsAlive()
        {
            try { return _proc != null && !_proc.HasExited; } catch { return false; }
        }

        static bool PortOpen(int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(150);
                    if (!ok) return false;
                    c.EndConnect(ar);
                    return c.Connected;
                }
            }
            catch { return false; }
        }
    }
}
