// 展示主程式設定：StreamingAssets/kiosk-config.json
// 現場可以直接用記事本改，不用重新 Build。
using System;
using System.IO;
using UnityEngine;

namespace MIRDC.Kiosk
{
    [Serializable]
    public class KioskConfig
    {
        public string organization = "金屬工業研究發展中心";
        public string title = "金屬中心\n互動展示";
        public string lede = "點一下右邊的項目就能開始，畫面左上角的「首頁」可以隨時回到這裡。";
        public string colorNote = "右側各項目的顏色，取自鋼材回火時表面出現的氧化色，由淺到深對應溫度由低到高。";
        [Tooltip("服務頁閒置幾秒後自動回到首頁，0 = 不自動返回")]
        public int idleReturnSeconds = 180;
        public bool fullscreen = true;
        [Tooltip("按住左上角機構名稱幾秒後結束程式（管理用），0 = 停用")]
        public float exitHoldSeconds = 5f;
        [Tooltip("網頁殼程式路徑（相對於 StreamingAssets）")]
        public string webHostPath = "Apps/WebHost/WebHost.exe";
        public ServerEntry[] servers = new ServerEntry[0];
        public ServiceEntry[] services = new ServiceEntry[0];

        public const string FileName = "kiosk-config.json";

        public static KioskConfig Load(out string error)
        {
            error = null;
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            try
            {
                if (!File.Exists(path)) { error = "找不到設定檔 " + path; return new KioskConfig(); }
                var cfg = JsonUtility.FromJson<KioskConfig>(File.ReadAllText(path));
                return cfg ?? new KioskConfig();
            }
            catch (Exception ex)
            {
                error = "設定檔格式錯誤：" + ex.Message;
                return new KioskConfig();
            }
        }
    }

    [Serializable]
    public class ServerEntry
    {
        public string id;
        [Tooltip("相對於 StreamingAssets 的路徑")]
        public string path;
        public string args = "";
        public int port = 3000;
    }

    [Serializable]
    public class ServiceEntry
    {
        public string id;
        public string name;
        public string description;
        [Tooltip("首頁右下角的類型文字；留空則自動填「應用程式」或「網站」")]
        public string kind;
        [Tooltip("exe 或 web")]
        public string type = "web";
        [Tooltip("服務色（#RRGGBB）")]
        public string color = "#8E5A2E";

        // exe
        public string path;
        public string args = "--embedded --parent-pid={pid}";

        // web
        public string url;
        public string profile;
        [Tooltip("優先使用名稱含此關鍵字的鏡頭，例如 Front / Rear")]
        public string camera;
        public bool usesCamera;
        public string extraArgs;
        [Tooltip("需要先啟動的本地伺服器 id")]
        public string server;

        [Tooltip("程式開啟時就在背景啟動，點選時瞬間顯示")]
        public bool preload = true;
        [Tooltip("回首頁時關閉程式（使用鏡頭的服務建議開啟，才會釋放鏡頭）")]
        public bool closeWhenHidden;

        public bool IsWeb => string.Equals(type, "web", StringComparison.OrdinalIgnoreCase);
    }
}
