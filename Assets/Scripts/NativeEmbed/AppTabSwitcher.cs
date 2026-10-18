// AppTabSwitcher
// 多個 EmbeddedAppHost 之間切換：同一時間只顯示一個，其餘隱藏（或關閉以釋放相機）。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MIRDC.NativeEmbed
{
    public class AppTabSwitcher : MonoBehaviour
    {
        [Serializable]
        public class Tab
        {
            public string title;
            public EmbeddedAppHost host;
            [Tooltip("此分頁的顯示區域（隱藏分頁時會 SetActive(false)）")]
            public RectTransform area;
            public Button button;
            [Tooltip("一開始就在背景啟動，切換時瞬間顯示")]
            public bool preload = true;
            [Tooltip("離開分頁時直接關閉程式（相機網頁建議勾選，才會釋放鏡頭）")]
            public bool closeWhenHidden = false;
        }

        public List<Tab> tabs = new List<Tab>();
        public int startTab = 0;
        public Text statusText;
        public LocalServerProcess server;

        public Color normalColor = new Color(0.25f, 0.27f, 0.32f);
        public Color selectedColor = new Color(0.25f, 0.45f, 0.85f);

        public int Current { get; private set; } = -1;
        public EmbeddedAppHost CurrentHost => Current >= 0 && Current < tabs.Count ? tabs[Current].host : null;

        void Start()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                int idx = i;
                if (tabs[i].button != null) tabs[i].button.onClick.AddListener(() => Select(idx));
                if (tabs[i].area != null) tabs[i].area.gameObject.SetActive(false);
            }
            foreach (var t in tabs)
                if (t.preload && !t.closeWhenHidden && t.host != null) t.host.Launch();

            Select(Mathf.Clamp(startTab, 0, tabs.Count - 1));
        }

        public void Select(int index)
        {
            if (index < 0 || index >= tabs.Count) return;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (i == index) continue;
                var t = tabs[i];
                if (t.area != null) t.area.gameObject.SetActive(false);
                if (t.closeWhenHidden && t.host != null && t.host.State != HostState.Idle) t.host.Close();
                Tint(t, false);
            }

            var cur = tabs[index];
            if (cur.area != null) cur.area.gameObject.SetActive(true);
            if (cur.host != null) cur.host.Launch(); // 未執行就啟動；已執行就顯示
            Tint(cur, true);
            Current = index;
        }

        // ---- 給工具列按鈕用（只對 WebHost 有效）----
        public void WebHome()   => CurrentHost?.SendCommand("home");
        public void WebReload() => CurrentHost?.SendCommand("reload");
        public void WebBack()   => CurrentHost?.SendCommand("back");

        void Tint(Tab t, bool on)
        {
            if (t.button != null && t.button.targetGraphic != null) t.button.targetGraphic.color = on ? selectedColor : normalColor;
        }

        void Update()
        {
            if (statusText == null) return;
            var h = CurrentHost;
            string s = h != null ? $"{tabs[Current].title}：{h.StatusText}" : "-";
            if (server != null) s += $"　｜　本地伺服器：{server.StatusText}";
            statusText.text = s;
        }
    }
}
