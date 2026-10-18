# MIRDC Pad Interaction

金屬中心 Windows 平板展示 APP（Unity 6000.0.53f1）。以 Unity 整合客戶既有的網站與 Windows 應用程式。

## 在新電腦上開啟

1. `git clone https://github.com/LeeMegumi/MIRDC-PAD-Interaction.git`
2. 用 Unity Hub 加入專案，版本 **6000.0.53f1**（第一次開啟會重建 Library，需要幾分鐘）。
3. 開啟 `Assets/Scenes/Kiosk.unity`，按 Play。

## 結構

| 位置 | 內容 |
|---|---|
| `Assets/Scripts/Kiosk/` | 首頁與服務頁（UI Toolkit） |
| `Assets/Scripts/NativeEmbed/` | 外部 exe 視窗嵌入、背景伺服器 |
| `Assets/UI/` | 樣式（Kiosk.uss）、字型（Noto Sans TC，SIL OFL） |
| `Assets/StreamingAssets/kiosk-config.json` | 服務清單：名稱、顏色、網址、exe 路徑 |
| `Assets/StreamingAssets/Apps/WebHost/` | WebView2 網頁殼程式（嵌入網頁用） |

## 需求

- Windows 10/11（x64）、.NET Framework 4.8、Microsoft Edge WebView2 Runtime（Win11 內建）
- 相機網頁：Windows「設定 → 隱私權 → 相機 → 允許桌面應用程式存取相機」需開啟
