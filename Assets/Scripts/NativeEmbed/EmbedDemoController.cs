// 測試場景用的控制器：按鈕事件 + 狀態顯示 + 切換嵌入區域尺寸（驗證 exe 的 RWD）
using UnityEngine;
using UnityEngine.UI;

namespace MIRDC.NativeEmbed
{
    public class EmbedDemoController : MonoBehaviour
    {
        public EmbeddedAppHost host;
        public RectTransform embedArea;
        public Text statusText;

        int _layout;
        static readonly string[] LayoutNames = { "全幅", "左側 2/3", "置中 1024×640", "窄版 720 寬" };

        void Update()
        {
            if (host == null || statusText == null) return;
            statusText.text = $"狀態：{host.State} ｜ {host.StatusText} ｜ 版面：{LayoutNames[_layout]}";
        }

        public void OnLaunch() => host.Launch();
        public void OnToggleVisible() => host.ToggleVisible();
        public void OnClose() => host.Close();

        public void OnCycleLayout()
        {
            _layout = (_layout + 1) % LayoutNames.Length;
            ApplyLayout(_layout);
        }

        void ApplyLayout(int i)
        {
            const float top = 120f, pad = 24f;
            switch (i)
            {
                case 0: // 全幅
                    Stretch(new Vector2(0, 0), new Vector2(1, 1), new Vector2(pad, pad), new Vector2(-pad, -top));
                    break;
                case 1: // 左側 2/3
                    Stretch(new Vector2(0, 0), new Vector2(0.666f, 1), new Vector2(pad, pad), new Vector2(-pad * 0.5f, -top));
                    break;
                case 2: // 置中固定
                    Fixed(new Vector2(1024, 640));
                    break;
                case 3: // 窄版：觸發 WinForm 的導覽收合 (<720px)
                    Fixed(new Vector2(700, 860));
                    break;
            }
        }

        void Stretch(Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            embedArea.anchorMin = aMin; embedArea.anchorMax = aMax;
            embedArea.pivot = new Vector2(0.5f, 0.5f);
            embedArea.offsetMin = offMin; embedArea.offsetMax = offMax;
        }

        void Fixed(Vector2 size)
        {
            embedArea.anchorMin = embedArea.anchorMax = new Vector2(0.5f, 0.5f);
            embedArea.pivot = new Vector2(0.5f, 0.5f);
            embedArea.sizeDelta = size;
            embedArea.anchoredPosition = new Vector2(0, -48);
        }
    }
}
