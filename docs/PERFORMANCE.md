# 工作台預覽效能

日期：2026-10-06（Asia/Taipei）。使用者確認卡頓在 Unity 關卡工作台／預覽。

## 修正與測量

原本逐像素填色時反覆讀取 `Texture.width/height`，每次 Unity GUI 的 Layout、Repaint 和其他事件也重新產生整張預覽。新版快取紋理尺寸、場地遮罩及底圖；只有 Repaint 且畫面內容有變更時更新紋理。播放／試玩更新上限為 60 Hz，暫停後重用既有紋理；只有正在顯示的 CAVE 或場域 atlas 會合成。

掃碼代碼池改用整數，不再於每次預覽重設建立一萬個四位碼字串。隨機唯一代碼、跨機台去重和舊關卡代碼拒絕規則保留。

同一台電腦、Unity **2022.3.62f3**、**RTX 4050 Laptop GPU**，在隔離副本執行前後測量。每種 canvas 操作先暖身 5 次、量測 80 次，解析度 **1040×840**，包含格線、QR、動態目標及 `Texture.Apply`。遮罩量測 80 次；80 個掃碼目標重設量測 40 次。

| 操作 | 原版平均 ms | 新版平均 ms | 原版 P95 ms | 新版 P95 ms |
| --- | ---: | ---: | ---: | ---: |
| 第 1 關完整預覽繪圖 | 68.636 | 3.414 | 79.925 | 7.920 |
| 第 2 關完整預覽繪圖 | 61.440 | 2.927 | 65.759 | 5.359 |
| 第 5 關完整預覽繪圖 | 64.074 | 0.926 | 67.225 | 1.550 |
| 全場 2184 格遮罩查詢 | 0.783 | 0.103 | 1.255 | 0.107 |
| 重設 80 個掃碼目標 | 1.492 | 0.090 | 2.646 | 0.646 |

第 1／2／5 關單次繪圖平均耗時約降為原本的 1/20、1/21、1/69。原始報告為 [before.json](../TestResults/Performance/before.json)、[after.json](../TestResults/Performance/after.json)。

## 畫面與限制

使用相同關卡、QR 代碼與時間，逐像素 RGBA 比較原 renderer 與新版。第 1／2／5 關含格線、不同動畫時間、六個獨立圖層與逾時，共 **30 組全部一致**，報告：[pixel-parity-checks.txt](../TestResults/Performance/pixel-parity-checks.txt)。

編輯圖 **520×420**、預覽 **1040×840**、場域輸出 **2688×3840**、底部 **512px** 黑色留白均保持。沒有以降低解析度或縮小 QR 達成效能改善。

隔離副本中以真實 EditorWindow 執行 **7 項**工作台測試：完整解析度、靜態 Repaint 不上傳紋理、外部關卡修改更新目標像素、播放路徑、60 Hz 上限、暫停停止上傳及 Undo 還原像素，全部通過。報告：[workbench-checks.txt](../TestResults/Workbench_Performance/workbench-checks.txt)。測試使用臨時關卡，不保存原關卡或場景；完整人工蓋章、繪製、匯出流程仍待使用者驗收。

這是單次繪圖耗時，**不是完整工作台 FPS 或現場多人效能**。60 Hz 是更新上限，不能據此宣稱實測穩定 60 FPS。這台 Unity Mono 的 `GC.GetAllocatedBytesForCurrentThread` 無法正確計數，報告以 `allocationCounterSupported=false`、`allocatedBytesPerCall=-1` 標示；不能宣稱零配置。實體掃碼、感測器和 QR 20 cm² 校正仍待現場驗收。

## 重跑

選單 `704 → 效能測量` 可測目前來源，輸出 `TestResults/Performance/manual.json`。比較原版需使用含原 renderer 的隔離副本；其來源快照在 `TestResults/Performance/reference/GameCanvasRenderer.cs.txt`，參考類別只放於 sandbox 的 Editor 目錄，沒有進入正式遊戲。

本次隔離副本為 `TestResults/Performance/Sandbox`。所有驗證／打包均使用副本，正式來源已在原 Editor 成功重新編譯。批次命令與 Windows 驗證流程見 [VALIDATION.md](VALIDATION.md)。
