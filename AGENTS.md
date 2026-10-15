# 704 掃碼 Pro

2026-10-13 最新交付為 `Builds/2026-10-13_704_ImportedLevels_DualScreen_AspectFix`。修正第二螢幕誤用主後台 Screen 尺寸造成變形；比例 34、QR 68、工作台 GUI 33 項通過，Release Build 與 144 檔雜湊核對成功。使用者按 Esc 停止 Computer Use，新版 Player 畫面檢查未完成；實體雙螢幕與 704 場域仍待驗收。以下舊版交付與測試僅為歷史記錄。

使用者於 2026-10-06 指定本專案存放於 Claude 工作區。本專案正式位置：
`C:/Users/momou/Desktop/Task/Research/Claude/ScanPro704`。

2026-10-13 最新使用者規則：已匯入與未來 ZIP 匯入關卡直接供試玩與 Build，不再另存正式或二次轉換；紫色首次匯入即使用 ClearMask。工作台可勾選「納入遊戲輸出」。目前場景直接引用 1-1～1-5 原匯入資產，1-3 採 ClearMask 版，原版保留且不輸出；Levels 資料夾仍空，不重建已刪除範例。最新交付 `Builds/2026-10-13_704_ImportedLevels_DualScreen` 建置成功，尚未啟動 Player 自測或硬體／場域驗收；詳細測試與限制見 HANDOVER／VALIDATION。以下較早的正式關卡限定與 10/08 交付路徑屬歷史狀態。

開始工作讀取 `../memory/AGENTS.md`、`../memory/MEMORY.md`、`../memory/preferences.md` 與 `../memory/專案日誌.md` 最新 3–5 筆，再讀本專案 `README.md`、`docs/Specification.md`、`docs/HANDOVER.md`。
以使用者同日對話澄清為準：Pro 只參考機制；掃碼為鍵盤輸入 `A/0000`、`B/0001`，各來源分流；20 cm²；成功藍色擴散、扣光心紅色擴散、逾時直接全紅；編輯 520×420 與輸出 2688×3840 分開，底部留 512px 黑色。
使用者先前要求「先記錄、不開始製作」適用於當時待辦；其後十一項工作台、遊戲及 Player 功能已實作。第一螢幕後台已拆成 Unity 控制視窗與獨立 Windows 鎖格視窗；記分板 HUD 已顯示五關組進度及剩餘秒數。2026-10-08 最新五關 Player Build 位於 `Builds/2026-10-08_704_CurrentLevels_MultiWindow_DualScreen_ScanDisappearFix`，建置成功但尚未執行 Player 自測。隔離來源 ProductionVerification 110 項、基礎檢查 42 項及工作台 GUI 9 項通過；VenueHud_04 的 106 項 Player 自測屬前一版，QR 專項 18 項也是前版紀錄。鎖格視窗執行緒使用 Per-Monitor DPI Aware V2，自測確認其完整位於主螢幕實體像素範圍。安全區只能放在 Floor／face 2 的限制已加入工作台、Inspector 與關卡／建置驗證；範圍與觸碰效果仍待定義。接手先讀 `docs/HANDOVER.md`、`docs/VALIDATION.md` 與 `docs/待製作清單_2026-10-06.md`，區分自動檢查、本機短錄影與長時間／現場驗收；手動視窗操作、長時間穩定性、TouchService、掃碼機與場域仍待驗收。只在使用者明確要求時 commit 或 push。

2026-10-08 已加入 Aseprite ZIP 匯入工作台入口與影格遮罩轉換；使用者 `1-1 (100ms)` 範例及最後一關刪除測試納入後，隔離副本匯入驗證共 59 項、基礎檢查 42 項、ProductionVerification 110 項、工作台 GUI 9 項通過。工作台允許刪除最後一個可編輯正式關卡，並安全清空場景關卡清單；新關卡發布後會恢復引用。正式資料夾與場景目前都只剩 Level_05（1-5），不要臆測還原 Level_01..04。刪除後沒有正式關卡時，一鍵 Build 會拒絕建置。當日五關 Player Build 曾建立，142 個檔案與 Sandbox 建置逐一 SHA-256 相同；新 Player 尚未自測。未手動操作 Unity 刪除按鈕或檔案選擇器，也未做 TouchService、掃碼機或場域驗收。完整測試 ZIP、報告與關卡雜湊見 `TestResults/AsepriteImport_20261008/`。已確認斜角相碰分開、顏色不代表類型、全部圖片非等比例最近像素縮至 520×420、紫格放開恢復、`shiftGreen` 依圖片位置且不受 Floor 專屬限制、起始／失敗重啟 15 心及上限 15；時間與結束流程留待後續、介面素材待提供。詳見 `docs/Specification.md`、`docs/Aseprite_ZIP匯入製作規劃_2026-10-08.md` 及 `docs/VALIDATION.md`。

Player EXE 第一台螢幕顯示兩個各自可移動、縮放的後台視窗：Unity 控制視窗與原生鎖格格圖；第二台顯示純場域遊戲。後台可鎖定 52×42 遊戲格停用該位置觸碰，並切換免扣心模式。關卡編輯、Build、模擬控制仍留在 Editor。VenueHud_03 主螢幕短片未重現頻繁閃爍；VenueHud_04 修正不同 DPI 縮放造成的鎖格視窗跨界，並通過實體像素矩形完整位於主螢幕的自測，但未重錄短片或做現場驗收。長時間場域使用、手動視窗操作／焦點、缺少第二台螢幕的 fallback、TouchService、掃碼機與投影仍待驗收。詳見 `docs/VALIDATION.md`；不可把模擬測試說成 TouchService、掃碼機或場域驗收。

2026-10-06 追加澄清：全關卡入口位置固定；入口不可互動，編輯器不能放任何物件，移動路徑也不得穿越。編輯圖入口為 (300,0,80,100)，輸出為 face 5／(2048,1920,640,512)／CW 90°；工作台、關卡 Inspector、輸出 Inspector 及建置檢查都必須保留此限制。

2026-10-06 使用者確認依全螢幕修正理解製作：完整 atlas 等比例置中，禁止為填滿不同長寬比螢幕而拉伸或裁切，剩餘區域留黑。場域輸出使用第二台螢幕及原生解析度；704 正式顯示端應設定 2688×3840。最新五關交付 build 與驗證見 `docs/VALIDATION.md`，不可把舊版結果當成目前 build 的驗收。

2026-10-06 最新追加：依使用者截圖將 active content 往左微調水平留黑空間的13%，完整畫布仍等比例、不裁切；704同長寬比時自動不偏移。保留入口背景，移除入口移動藍色小方塊。當前輸出與驗證以 `docs/VALIDATION.md` 及 README 記載的五關 Player 為準。
