# 原型驗證與目前交付狀態

## 2026-10-13 Build 畫面比例修正

- **根因與修正**：第二顯示器的 null destination 使用主後台 `Screen` 尺寸計算比例。改為使用 `OnRenderImage` 當次相機影格；明確目的 RenderTexture 優先使用自身尺寸。
- **可重現回歸**：同一組測試以舊 Presenter 得到 21 過／13 失敗；例如直向 1024×1920 讓方塊變成 77×192。新版本 **34 過／0 失敗**，驗證橫向、直向、704 原生與較小視窗輸出，包含方形像素、方向、完整角落、留黑與水平偏移。這是隔離 Editor GPU 模擬不同相機影格，不等於實體雙螢幕測試。
- **相關回歸**：QR 繪圖／ZXing 解碼 **68 項**、真實工作台 EditorWindow GUI **33 項**通過，包含現有滑鼠掃碼入口；此次 GUI 未記錄 EndLayoutGroup。沒有重跑依賴舊範例的 Player 全流程自測。
- **交付**：`Builds/2026-10-13_704_ImportedLevels_DualScreen_AspectFix`，Release Build 成功、batch exit 0，144 檔與 Sandbox SHA-256 完全相同。直接輸出 1-1～1-5，1-3 為 ClearMask；無二次轉換。
- **保留資產**：89 個既有正式資產、scene 與 meta 雜湊不變。60 個來源檔比較中只有 Sandbox scene 儲存換行不同，正規化換行後內容一致；其他 59 檔雜湊相同。
- **Player／設備限制**：本機舊版啟動確認只偵測到一個顯示器。啟動新版時使用者按 Esc 停止電腦操作，因此新版實際畫面／縮放檢查未完成；TouchService、實體掃碼器、第二螢幕與 704 場域未驗收。
- 報告、前後 GPU 圖、log、來源與交付雜湊：[DisplayAspectFix_20261013](../TestResults/DisplayAspectFix_20261013/)。

## 2026-10-13 匯入關卡直接輸出與紫色 ClearMask 預設

- **來源／場景**：五份原匯入資產直接輸出，1-3 採 ClearMask 版；舊 1-3 只新增排除輸出旗標，場景 `levels` 引用改為這五份原 GUID。未重建正式關卡副本，未更改五份輸出資產內容。
- **專項 23 項通過**：既有匯入資料直接可用、關號排序、排除／重新勾選、清空場景後自動補回、參照原 GUID、新 ZIP 直接可用、紫格首次匯入即有來源遮罩、缺少遮罩拒絕匯入、刪除匯入關卡清引用、手工草稿不意外輸出、來源資產雜湊保持不變。
- **回歸**：既有 Aseprite ZIP 匯入 90 項、真實工作台 EditorWindow GUI 33 項通過。Unity 2022.3.62f3 隔離驗證 batch exit 0，專案完整快照位於 `TestResults/ImportedOutput_20261013/Sandbox`。
- **Build**：直接呼叫正常輸出入口 `PrototypeBuild.BuildWindows`，一般 Release 建置成功、exit 0；輸出及 `關卡清單.txt` 確認 1-1～1-5，沒有生成 Level_01..05。交付 `Builds/2026-10-13_704_ImportedLevels_DualScreen` 的 144 個檔案與隔離 Build 逐一 SHA-256 相同。
- **尚未驗證**：新 Player 尚未啟動／自測；Play Mode 進出、新勾選控制人工操作、實體掃碼機、TouchService 及 704 投影仍待驗收。既有 Player 自測依賴舊範例關卡假設，本次未拿它宣稱新匯入五關通過。
- 報告、來源比對、交付雜湊及完整 log：`TestResults/ImportedOutput_20261013/`。

## 2026-10-13 QR 藍底與場域輸出像素對齊

- **需求**：2×2 及 3×3 以上都要保留藍底；改善場域輸出中小型 QR 的清晰度。方形 QR 保留底色外框與白色淨空，小型 QR 來源倍率至少 7；先在原生 atlas 排列整數大小模組，再映回來源。2×2 方形為 128×128px、QR 卡片 116×116px、每模組 4px、外框各約 6px；工作台 atlas 預覽改為 Point 取樣。
- **修正前重現**：同一專項對保留的原 `GameCanvasRenderer.cs` 得到 **43 過／25 失敗**，包括 3／4／6 格外框過窄、QR 大小隨倍率改變及最終模組不等寬。報告：[Before](../TestResults/QrClarity_20261013/Before/qr-rendering-checks.txt)。
- **QR 專項**：Unity 2022.3.62f3 隔離 GPU **68 項通過，exit 0**。使用真正 Output Atlas 與 Fullscreen Fit shader；原生 2688×3840 的 2／3／4／6 格、圓形、長方形、淺藍及動畫 QR 可由 ZXing 獨立解碼。1344×1920 半尺寸驗收方形／長方形／動畫，最小圓形只驗原生解碼；同時檢查無混色邊界、完整底色外框、原生模組等寬、7／8／9 倍畫布及動畫第二幀。[報告](../TestResults/QrClarity_20261013/After/qr-rendering-checks.txt)、[前後對照](../TestResults/QrClarity_20261013/qr-before-after.png)。
- **完整回歸**：既有 `ProductionVerification` **165 項通過，exit 0**，其前置基礎檢查另有 60 項通過。第一次使用正式設定的副本，因過關藍飽和度 49% 不符合舊測試的原色假設而中止；之後僅將 Sandbox 的色彩 fixture 改為 100% 再跑。正式紅 71%／過關藍 49% 設定保留，QR 不使用這兩個飽和度設定。[報告](../TestResults/QrClarity_20261013/Production/production-checks.txt)；初次 log 保存在 `Production-current-colors-initial.log`。
- **真實工作台**：EditorWindow **33 項通過，exit 0**，包括場域輸出 RenderTexture 的 Point 取樣與三視圖切換、重繪及草稿刷新；未記錄 `EndLayoutGroup`。[報告](../TestResults/QrClarity_20261013/Workbench/workbench-checks.txt)。同期 A／B 模擬介面變更保留，沒有用舊工作台覆寫。
- **來源與資產核對**：本次五個程式／驗證檔與受測 Sandbox SHA-256 相同，快照存於 `TestedSource/`；正式關卡、草稿、場景及 meta 共 14 檔前後雜湊一致。核對見同資料夾 `source-sha256.json`、`assets-before.json`、`assets-after.json`。
- **範圍**：本次未建置 Player，未測實體掃碼機、TouchService、投影設備、掃描距離或任意非原生縮放比例。正式 Editor 人工操作及 `EndLayoutGroup` 原根因仍待確認。重跑入口：`TestResults/QrClarity_20261013/run-verification.ps1 -Stage Baseline|Quality|Production|Workbench`；只操作該資料夾的 Sandbox。

## 2026-10-13 A／B 雙掃碼器與模擬預設

- 工作台前綴預設 `A`，工作台及 Play Mode 操作台新增 A／B 快速帶入；實體接收仍依 USB 裝置分流，兩支同時啟用，不受模擬欄位影響。
- 隔離 Unity 2022.3.62f3 編譯與 batch 執行成功，exit 0；既有基礎 60 項及 ProductionVerification 165 項通過，涵蓋 A／B 交錯輸入完整組碼、可選結尾不重複發送、40 路並行同碼只完成一次及不同碼可立即接受。
- 本次是程式模擬，未測兩支實體掃碼器、TouchService 或現場輸出，未建置 Player。關卡、草稿與場景前後 SHA-256 相同。
- 報告、log、來源比對與資產雜湊：`TestResults/ScannerPrefixes_20261013/`。掃碼回歸之後另觀察到工作台 atlas 預覽紋理取樣設定被同步調整，已保留該變更；受測回歸快照另存 `TestedSource/ScanProWorkbench.regression.cs.txt`。
- 保留上述同步變更後，既有真實工作台 GUI 驗證 32 項通過，涵蓋平面／CAVE／atlas 切換與重畫，未記錄 EndLayoutGroup。受測工作台為 `TestedSource/ScanProWorkbench.gui.cs.txt`；未直接模擬新 A／B 按鈕點擊，人工操作仍待確認。

## 2026-10-13 CAVE 左牆取景、2×2 QR 清晰度與記分板關號

> QR 滿幅繪製已由本文件上方的藍底／像素對齊修正取代；以下保留為當時驗證紀錄。

- **改動**：CAVE 維持 10/12 展開圖貼圖和房間幾何，預設第三人稱取景調為 home offset `(9,20,-37)`、FOV 52，使左牆以較正面、較高比例完整呈現；牆面本身未旋轉。藍格數字／算式與底線在 CAVE 和 VenueOutput 內維持同角度。
- **QR 清晰度**：2×2 QR 使用完整方形範圍，矩陣含標準淨空，每個模組至少 3 個渲染像素。獨立 ZXing 解碼通過最小 QR 的畫布輸出與場域 atlas 映射輸出。
- **記分板**：依關卡 `displayName` 或匯入檔名顯示有效關號（例如 `1-2 (250ms)` 顯示 `1-2`），移除影格速度後綴；沒有有效檔名時使用內部關號。
- **GPU 驗證**：Unity 2022.3.62f3 隔離副本 `ProductionVerification` **165 項通過（exit 0）**，包括左牆完整投影範圍、QR 經 atlas 後獨立解碼與檔名關號規則。報告：[`production-checks.txt`](../TestResults/CavePreviewFollowup_20261013/Sandbox/TestResults/production-checks.txt)。
- **工作台 GUI 驗證**：隔離 Unity 真實 EditorWindow **32 項通過（exit 0）**，反覆平面／CAVE／atlas Repaint、切換預覽及草稿 AssetDatabase refresh 均未記錄 `EndLayoutGroup`。報告：[`workbench-checks.txt`](../TestResults/CavePreviewFollowup_20261013/Artifacts/workbench-checks.txt)，log：[`workbench-followup3.log`](../TestResults/CavePreviewFollowup_20261013/workbench-followup3.log)。CAVE 圖像：[`cave-preview-left-wall.png`](../TestResults/CavePreviewFollowup_20261013/Artifacts/cave-preview-left-wall.png)。
- **佈局錯誤限制**：正式 Unity Editor.log 的原錯誤只有 Unity GUI host 堆疊，沒有 ScanPro 呼叫框架；`ScrollViewScope` 防護保留，但隔離測試未重現不代表已確認原根因或正式 Editor 已清除。
- **未驗證**：未在正式 Editor 人工點選操作、未建置 Player；未測 TouchService、實體掃碼機或 704 投影。測試在隔離副本執行，未修改正式關卡或場景。逐項報告與 log 見 [`TestResults/CavePreviewFollowup_20261013`](../TestResults/CavePreviewFollowup_20261013)。
## 2026-10-13 先前 atlas 面映射方向稽核（CAVE 預覽已後續回復）

> 歷史狀態：本節的 163 項測試與 atlas-based CAVE 畫面屬於先前中途版本；目前 CAVE 已恢復展開圖預覽。現行 CAVE／VenueOutput 藍格角度與左牆可見性依本文第一節重新驗證。

- **需求確認**：使用者指出實際 CAVE 投影後數字方向仍錯，並指定依附圖判斷右牆方向為正向基準。四牆方向以觀眾站在 Floor 內側看向各牆時可正常閱讀為準。
- **方向修正**：以場域 Face 4 右牆作為正向參考，校正 Face 0、1、3 的牆面文字旋轉。CAVE 預覽現在使用場域輸出的最終 atlas 紋理，並由 Floor 內側渲染四面牆，保持預覽與輸出面的映射一致。
- **舊測試範圍更正**：2026-10-12 `NumberCaveVerification` 50 項及 `ProductionVerification` 151 項沒有驗證最終 atlas shader 各牆面映射後的實際觀眾方向；它們不能證明實際投影方向正確。請以本節 10/13 結果為準。
- **隔離 GPU 回歸**：Unity 2022.3.62f3 `ProductionVerification` **163 項通過（exit 0）**。新檢查逐一確認最終 atlas 的 Face 0／1／3 標記與 Face 4 右牆參考一致，並從 Floor 觀眾視角確認左、前、後、右牆的非對稱 `12` 標記正讀。逐項結果見 [`production-checks.txt`](../TestResults/CaveProjectionFix_20261013/Sandbox/TestResults/production-checks.txt)；圖像證據在 [`Artifacts`](../TestResults/CaveProjectionFix_20261013/Artifacts)。
- **正式專案編譯**：`ScanPro704` 正式專案使用 Unity 2022.3.62f3 batch mode 完成 C# scripts 編譯與 domain reload，exit 0，沒有 compiler error；log：[`official-project-compile.log`](../TestResults/CaveProjectionFix_20261013/official-project-compile.log)。
- **尚未驗收**：未建置 Player，未連接實際 TouchService 或掃碼機，未執行實體 CAVE／投影驗收。正式關卡與場景未改寫。隔離渲染和腳本編譯不代表實際安裝方向已通過。

## 2026-10-12 數字字形、場域方向與 CAVE 左牆

> 歷史驗證更正：此日的 50／151 項方向測試沒有覆蓋最終 atlas 面映射；實際方向依 2026-10-13 本文件開頭的 163 項 GPU 回歸重新驗證。

- **核心修改**：`GameCanvasRenderer.cs` 將舊 3×5 點陣改為手繪 5×7 字形，窄體 `1` 讓單格 `10`／`11`／`12` 保持可見；容量檢查與繪製共用 `MeasureTextGrid`，2／5／6／9 與底線間隔一列。CAVE 牆面閱讀方向作為基準，VenueOutput 文字補償 atlas 的順時針映射；平面、地板與入口方向保持原規則。`CaveRoom` 空白黑面加低對比表面色，讓左牆不再與深色背景混在一起。
- **Unity 驗證**：Unity 2022.3.62f3 隔離副本 `NumberCaveVerification` **50 項通過，Unity exit 0**。覆蓋 1–12、20／59／69／99、算式、單格雙位數、2／5／6／9 底線空列、圓形／長方形尺寸、手動 label 與自動 label 逐像素一致、四面 `12` marker 經 atlas 四分之一轉向後與 CAVE raster 逐像素相同、記分板 face 6 文字，以及 atlas 2688×3840 尺寸與底部全寬 512px 黑帶。
- **完整回歸**：同一隔離專案的完整 `ProductionVerification` **151 項通過（exit 0）**，涵蓋場域每面方向、記分板、QR 尺寸與獨立解碼、動畫／shutter／紫格執行期、TouchService 訊息解析等既有功能；逐項結果見 [`production-checks.txt`](../TestResults/NumberCaveFix_20261012/Sandbox/TestResults/production-checks.txt)。這是自動化隔離測試，不代表實體 TouchService、掃碼機或場域驗收。
- **圖像檢視**：隔離預設 CAVE 及面向左牆截圖都可見左牆；預設畫面暖色牆面像素 8,060。樣張、`cave-default.png`、`cave-left-wall-view.png`、`venue-scoreboard-canvas.png` 與 shader 實際輸出的 `venue-output-numbers.png` 位於 [`NumberCaveFix_20261012/Artifacts`](../TestResults/NumberCaveFix_20261012/Artifacts)。完整項目見同資料夾 `checks.txt`，log 為 `unity-number-cave-final.log`。
- **正式 Editor**：只讀 `Editor.log` 確認 2026-10-12 17:06:59 腳本建置成功並完成 assembly reload；未手動操作正式工作台。
- **限制**：這是隔離 renderer／GPU shader 與完整自動化回歸，不是正式工作台的人工操作或現場投影驗收。正式 Editor 未做手動輸入與右鍵環視；本輪未建置 Player。完整回歸含動畫／shutter、QR 獨立解碼及 TouchService 訊息解析，但未測實際 TouchService 服務連線、掃碼機、實體七面方向或 704 場域。未專項重跑「文字／QR 隨各自藍格影格移動」回歸。正式 `Levels` 及場景關卡清單仍空，不重建關卡。未 commit／push。

## 2026-10-12 紫格清除遮罩只影響重疊區域

- 紫格觸發只清除 `to(clearwrong).png` 遮罩和連結危險區／`frames/` 的實際重疊；未覆蓋部分保持顯示與碰觸。放開後恢復。舊草稿缺少來源遮罩時採紫格圖形局部裁切，不會隱藏整個連結群組。
- Unity 2022.3.62f3 隔離驗證：`RunPurpleHoldVerification` **41 項**、原始 ZIP 匯入 **99 項**、色彩／圖層回歸 **65 項**、shutter 影格回歸 **174 項**通過。測試包含局部遮罩外的紅區仍有效、紫色清除區露出下層藍格、原始 1-3 ZIP 匯入遮罩、1-3 的四個 frames 群組及 shutter 切換共存。
- log：`../TestResults/PurpleMaskFix_20261012/purple-mask-unity-rerun.log`、`import-verification-final.log`、`color-layer-unity.log`、`shutter-unity.log`。1-3 隔離預覽：`../TestResults/PurpleMaskFix_20261012/imported-1-3-clear-mask-preview.png`。預覽使用驗證用紫格觸發群組，不是正式 Editor 操作截圖。
- 正式 Unity Editor 的 `Editor.log` 於 2026-10-12 15:44:18 記錄 Editor 腳本建置成功與 assembly reload；工作台實際長按操作尚未人工驗收。現存 `Imported_1-3 (100ms).asset` SHA-256 為 `11113641FB7190468016B25DAB5396510FDCBD69C3D0FD3C32EBC71AC0493E1F`，本次文件更新未改動。未建置 Player，未測 TouchService、實體掃碼機或場域。
- 已由原始 `1-3 (100ms).zip` 在隔離副本重匯入後建立新草稿 `Imported_1-3 (100ms) - ClearMask.asset`，3 個紫色機關均有來源遮罩；Unity 匯入 log 記錄 36 個項目。新草稿 SHA-256 `9E56DEDC170C0BF3851490E115D74BBA3C5598C925DC9C900A2679E3F0396C38`。新資產及 `.meta` 已複製至正式專案 `LevelDrafts`；原草稿未覆寫。來源 ZIP SHA-256 `32FE09324792C78C89D39717A92C0B351746D0FE115AA1744C2994E3CDE34675`。
- 新增草稿的 batch-mode 匯入記錄：`../TestResults/PurpleMaskFix_20261012/reimport-1-3.log`（`purpleTriggers=3 maskedTriggers=3 items=36`）。正式 Editor 15:54 的 Asset Pipeline Refresh 記錄新匯入 1 個資產；仍需在工作台選取 `ClearMask` 草稿操作驗收。

## 2026-10-12 shutter 每次掃描只顯示一幀

- 最新規則取代本文件下方較早的「未選 shutter 仍為紅色」：每組起始顯示第一幀，每次有效掃描只前進一幀並循環；未選影格不繪製、不攔截碰觸。工作台預覽及試玩的模擬掃碼都更新實際顯示狀態。
- Unity 2022.3.62f3 隔離副本 **202 項通過**：`TestResults/ShutterFrames_20261012/Shutter/checks.txt` 174 項；同份執行期及工作台來源的 `CurrentWorkbench/workbench-checks.txt` 28 項。最後 Shutter 執行已恢復全部 Editor 驗證程式並編譯成功，exit 0；保留同期紫格局部清除遮罩變更。
- 174 項包含正式 1-3 草稿四組 shutter 各自掃描、全序列與循環、逐像素參考畫面；合成三幀紅／綠、重送／跨機台去重、未掃描時間不推進、重啟、隱藏區不擋下層藍格、選中紅區扣心，以及紫格按住期間切換與放開。GUI 28 項包含真實 EditorWindow 預覽／試玩 callback、連續重送、第二次掃描循環與 Repaint 後的像素核對，另保留既有 GUI 回歸。
- 修改前同一測試在 1-3 初始逐像素比對失敗，重現兩幀重疊；`Baseline/1-3-initial.png` 為錯誤畫面。修正後 `Shutter/1-3-initial.png` 及 `1-3-switch-1..4.png` 是隔離 renderer 產圖，並非正式 Editor 人工操作截圖。
- 草稿／場景／色彩設定及其 meta 共 10 檔 SHA-256 前後相同。現存 1-3 草稿 SHA-256 為 `11113641FB7190468016B25DAB5396510FDCBD69C3D0FD3C32EBC71AC0493E1F`（來源匯入時間 2026-10-12 15:06:50）；下方舊雜湊是之前草稿的歷史紀錄。未修改來源 ZIP、未建置 Player，實體掃碼機／TouchService／704 場域未驗收。
- 初次額外執行舊紫格全套因新 Sandbox 缺測試 ZIP 而中止，不計為通過；後續已補 fixture。紫格全套與同期局部遮罩工作的完整驗證另行記錄，本節只報本輪 shutter 及工作台結果。重跑指令見 `TestResults/ShutterFrames_20261012/run-verification.ps1`。

## 2026-10-12 紫格整組透明（歷史版本，已由局部遮罩取代）

- 此前版本隱藏整個 `targetGroups`，並以 39 項測試核對該行為；它不符合後續補充的「紫格不得覆蓋 frames 未被遮罩涵蓋區域」要求。最新驗證以本文件開頭的局部遮罩結果為準。

## 2026-10-12 共用色彩、固定圖層與 CAVE 即時更新

- 在新隔離副本 `TestResults/ColorCave_20261012/Sandbox` 使用 Unity 2022.3.62f3 完成編譯與 **117 項檢查**，不使用正式 Editor 做測試、不初始化正式關卡。
- [色彩及 CAVE 40 項](../TestResults/ColorCave_20261012/Color/checks.txt)：跨關卡共用與持久化；100% 原色／0% 灰階；紅色以外逐像素不變（含 QR、藍格、淺藍、入口）；成功過渡在三視圖使用新色、失敗紅色不變；反向舊排序無法改變五層模板、紅蓋藍只扣心。真實工作台右鍵連續拖曳在暫停時即時更新 RenderTexture，沒有切換視窗／視圖，且遊戲畫布 updateCount 不增加；驗證重設、播放中旋轉、放開／失焦。另直接對 GUI 輸入 40% 紅與 20% 藍並按儲存，檢查實際 YAML 值。
- [修正前 CAVE 重現](../TestResults/ColorCave_20261012/Baseline/checks.txt)：相同滑鼠事件能移動相機，但下一次 Repaint 的 RenderTexture 像素不變，與使用者描述一致。該基線測試預期失敗，不能算通過。
- [工作台 GUI 22 項](../TestResults/ColorCave_20261012/Workbench/workbench-checks.txt)：Console、快取／播放／Undo、QR 尺寸、位置限制與算式重新套用。首次因既有測試假設固定 1040×840、硬編碼 20px／格而失敗；3×3 QR fixture 需要更高倍率。將測試改用 RequiredQrScale 及動態座標，並修正 Color 與 Color32 比較後重新通過；不是降低產品 QR 解析度。
- [規則回歸 55 項](../TestResults/ColorCave_20261012/Production/production-checks.txt)：使用 `RunColorLayerRegressions`，涵蓋 QR 尺寸／ZXing 解碼、固定排序／命中、鎖格／無敵、工具規則、紫格靜態／動畫遮罩與放開。這是選定範圍回歸，不是完整 ProductionVerification、Player 或硬體驗收。
- `assets-before.json` 與 `assets-after.json` 確認正式專案三份草稿、場景及相關 meta 雜湊一致；正式關卡仍空。全域色彩初值均保留 100%，未替使用者決定新色值。未建置 EXE，未手動操作正式 Editor，未測 TouchService、掃碼機或場域。
- 重跑：`run-verification.ps1 -Stage Color|Baseline|Workbench|Production`。只有測試副本插入控制項座標記錄，正式來源無測試插樁；最後 Sandbox 工作台使用與正式來源相同的程式。

## 2026-10-12 shutter 未選狀態紅色顯示

- `GameCanvasRenderer` 對淺藍 shutter 選擇以外、暫停傷害的紅色區域仍繪製紅色；`GameSession` 的無傷安全狀態不變。紫格遮罩只讓來源 `to(clearwrong).png` 與目標區重疊處透明，未覆蓋的 frames 保持顯示及碰觸；詳見本文件開頭的 41 項紫格遮罩驗證。
- 原 shutter 專項 `RunShutterColorVerification` **6 項通過**：起始未選區紅色且接觸不扣心、掃碼切換後狀態與顏色正確，以及載入正式 1-3 草稿後確認四個未選 shutter 區並找到實際可見紅色區域。原始報告／截圖：`TestResults/WorkbenchFix_20261012/ShutterRed/`；透明修正後的重跑結果見本文件開頭。
- 1-3 正式草稿 SHA-256 前後均為 `2652EA8D5FF79811B9775C8E192E2634DCF25C899A19987FA685E3064BC38C98`，只有讀取。正式 Editor.log 於 2026-10-12 14:28:09 記錄成功編譯及 reload；未建置 Player，工作台人工操作仍未確認。
- 追修期間嘗試在舊隔離專案跑完整 `ProductionVerification.Run`，但該次在既有 `Level 1 visible object footprints fit supplied map` 檢查失敗並中止；不作為通過紀錄。此次採用獨立 shutter 專項 6 項結果。

## 2026-10-12 工作台掃碼輸入焦點修正

- `DrawToolButton` 改為只在選取不同工具時清除文字焦點，避免已選中工具每次 GUI 事件都讓欄位失焦。
- Unity 2022.3.62f3 隔離副本的真實 `ScanProWorkbench` 視窗，使用 `EditorWindow.SendEvent` 模擬滑鼠點欄位、Ctrl+A、逐字輸入、Backspace、Enter 和按送出，**47 項通過**。覆蓋六種工具與正在播放的試玩模式；試玩有效 QR 接受後目標完成、剩餘數量減一。
- 測試輔助程式只在隔離副本記錄欄位位置，輸入值由 GUI 事件產生；沒有用反射直接寫入掃碼欄位。最初測試的點擊位置漏計 EditorWindow 頁籤高度，校正為視窗座標後重新執行，不能將校正前的失敗當成產品故障證據。
- 校正座標後重跑修正前程式，點進前綴欄時已取得焦點，後續 GUI 事件仍將焦點清零；逐字輸入後欄位維持 `SIM-A`，成功重現回報。結果見 [修正前輸入失敗](../TestResults/ScanInput_20261012/before/scan-input-checks.txt)，修正版以相同測試通過。
- 三份正式專案草稿及 `PrototypeGame.unity` 在此輪修改前後 SHA-256 相同，正式關卡資料夾仍空；比對結果保存在 `TestResults/ScanInput_20261012/assets-after.json`。
- 報告：[掃碼輸入 47 項](../TestResults/ScanInput_20261012/after/scan-input-checks.txt)。重跑工具及輔助原始碼位於同層 `run-input-check.ps1` 與 `WorkbenchScanInputVerification.cs.txt`，只操作指定 Sandbox。
- 移除 Sandbox 的測試座標記錄、同步正式來源後，既有 [WorkbenchVerification 21 項](../TestResults/ScanInput_20261012/workbench/workbench-checks.txt) 全部通過，涵蓋 Console 清除、預覽快取／播放／Undo、QR 尺寸及算式重新套用。本輪合計 68 項檢查通過。
- 未手動操作使用者目前開啟的正式 Editor；`Editor.log` 於 2026-10-12 14:28:09 記錄成功編譯及 assembly reload，但工作台人工操作仍待確認。未建置 EXE，未測實體掃碼機、TouchService、感測器或場域。

## 2026-10-12 工作台輸出控制精簡

- 已從工作台移除場域 Profile 欄位／選取按鈕、編輯圖 PNG、PNG 圖層選單與 4 秒路徑 GIF 控制；保留場域 PNG 匯出及 `704_Output.asset` 設定資產。
- 本次未執行 Unity 編譯或 GUI 驗證；需由 Unity Editor 重新載入後確認畫面。

## 2026-10-12 Console、2×2 QR、預設圖層與 1-3 數字顯示修正

- 修正 `GameCanvasRenderer.cs` 的 CS0172：`Color`／`Color32` 雙向隱式轉換使巢狀條件式無法決定型別，造成 Editor 腳本編譯失敗。現在以明確分支指定渲染顏色，隔離 Unity 編譯成功；這也讓工作台的新 Console 按鈕程式得以載入。
- 工作台「清空 Console」呼叫 Unity 2022.3.62f3 的 `LogEntries.Clear()`。針對按鈕無反應回報，新增已開啟 Console 視窗重繪及剩餘訊息數提示；全數清除時顯示「已清空」，仍有訊息時顯示筆數，失敗時顯示原因。Unity Console 會保留編譯錯誤，因此錯誤仍存在時按鈕會回報剩餘筆數。`EditorConsoleUtilityVerification.Run` 驗證清除、新訊息可再新增、工作台 callback 清除及成功提示；真實 `EditorWindow` GUI 驗證更新後 **21 項全通過**，涵蓋重複清除與清除後新訊息。
- 驗證在隔離 Unity 副本執行，沒有手動滑鼠點按正式 Unity Editor 的按鈕；目前電腦操作介面未提供 Unity 視窗。正式專案 `Editor.log` 於 2026-10-12 14:28:09 記錄腳本編譯成功及 assembly reload；Console 按鈕實際點擊行為仍待人工確認。
- 藍色掃碼與淺藍掃碼機關的尺寸驗證已統一為寬、高至少 2 格；涵蓋工作台尺寸欄位與放置、Inspector guard、靜態／動畫影格、渲染倍率與匯出。2×2 QR 會保留完整模組及白邊。圓形外框可為 2×2；其實體 20 cm² 面積仍待校正。
- 新建、ZIP 匯入及缺漏圖層補齊共用預設順序：內部由下至上 Blue、SwitchCyan、HoldPurple、HazardRed、SafeGreen；工作台由上至下綠 > 紅 > 紫 > 淺藍 > 藍。既有已存自訂值不會在載入時改寫；工作台套用按鈕支援 Undo。
- `1-3 (100ms).zip` 含 10 個單格藍色目標；匯入後標號為 1–10，原本的兩位數 `10` 因文字內距不足而未繪製。修正文字與自動內容尺寸檢查後，新增的 10 格合成回歸和原始 ZIP 渲染核對均確認 1–10 全數可見。當次驗證沒有在正式專案建立 1-3 草稿；之後由使用者匯入的草稿現況見本節下方 SHA-256 記錄。
- 隔離 Unity 2022.3.62f3 驗證：`EditorConsoleUtilityVerification.Run` 確認清除後訊息數歸零、之後仍可新增訊息，並測試工作台 callback；`AsepriteZipImportVerification.Run` **88 項**、基礎檢查 **51 項**、`ProductionVerification.Run` **136 項**、更新後真實 `EditorWindow` `WorkbenchVerification.Run` **21 項**通過，Unity exit 0。原始 ZIP 另外確認 10 個標號皆已渲染。
- 測試 log 位於 `TestResults/WorkbenchFix_20261012/Sandbox/`：`console-button-workbench-final.log`、`aseprite-verification-rerun4.log`、`production-render-rerun.log`、`workbench-gui-consolebutton-final.log`、`verify-1-3-source-import.log`。新增 GUI 報告：`TestResults/WorkbenchFix_20261012/WorkbenchGUI_ConsoleButton/workbench-checks.txt`。原始 ZIP 一次性核對程式為 `TestResults/WorkbenchFix_20261012/Verify13SourceZipTemp.cs`。
- 初始隔離驗證時沒有在正式專案建立 1-3 草稿；之後使用者在正式 Editor 匯入後，`Assets/ScanPro/LevelDrafts/Imported_1-3 (100ms).asset` 已存在。本輪修正前後 SHA-256 均為 `2652EA8D5FF79811B9775C8E192E2634DCF25C899A19987FA685E3064BC38C98`，草稿未寫入；正式 `Levels` 資料夾和場景關卡清單仍空。
- 未修改正式關卡、`PrototypeGame.unity`、現有三份草稿或來源 ZIP；未建置 Player。未測實體掃碼機、TouchService 或場域。工作台 Console 按鈕的實際滑鼠點擊仍待 Unity Editor 人工確認。

## 2026-10-12 紫色長按顯示

- 隔離 Unity 2022.3.62f3 Batch Mode **exit 0，20 項全數通過**。涵蓋新關、ZIP 匯入及現存草稿的 0.1 秒預設、門檻前後、持續按住、放開後重新計時、多觸點／多個紫格個別計時、共用紅組、重試重設、標籤隱藏，以及靜態圓形與動畫影格只填白實際形狀。
- 報告與 Unity log：`TestResults/PurpleHold_20261012/purple-hold-checks.txt`、`purple-hold-unity.log`。紫格修正驗證當時 `LevelDrafts` 只有兩份草稿，兩份皆已設為 0.1 秒；當時 1-3 不在資料夾。之後使用者已匯入 1-3，現有第三份草稿保留。正式關卡資料夾仍為空。
- 未建置 Player，未測 TouchService、實體掃碼機或場域。
- 上述 20 項是透明需求以前的紫格回饋基線；目前遮罩／frames 行為與重跑結果以本文件開頭「紫格清除遮罩只影響重疊區域」為準。

日期：2026-10-12（Asia/Taipei）。以下區分各次本機驗證結果與尚未重跑的程式變更，不代表現場設備驗收。

## 2026-10-12 工作台 IMGUI 佈局錯誤追查

- 使用者回報 Unity Repaint 時 `GUILayoutGroup.GetNext`／`BeginLayoutArea` 例外。堆疊沒有 ScanPro 呼叫框架，不能單靠堆疊判定根因。
- 原始碼檢查發現 Enter 結束路徑選取的分支會提早離開 `OnGUI`，使該按鍵事件跳過工作台整棵 GUILayout 控制項樹；已移除提早返回，並保留在下一個 Layout 事件才套用選取狀態的做法。
- 2026-10-12 隔離工作台 GUI 驗證共 17 項通過，包含 Enter 結束路徑的條件判定與真實 EditorWindow 的反覆 Repaint；尚未在正式 Unity Editor 人工實際按 Enter 重現操作。

## 2026-10-08 動態內容跟隨、檔名命名與工作台算式輸入

- 隔離副本 `TestResults/AnimatedContent_20261008/SandboxIsolated02` 執行：`AsepriteZipImportVerification.Run` **89 項**、`ProjectSetup.ValidatePrototype` **48 項**、`ProductionVerification.Run` **110 項**通過，Unity exit code 0。真實 EditorWindow 工作台 GUI `WorkbenchVerification.Run` **12 項**通過。
- 動態內容檢查涵蓋：影格內容範圍排除空白像素、數字取用起始影格的實際位置與尺寸、內容隨影格移動且物件識別不變、空影格同時隱藏藍格與內容、不規則影格以實際占用格檢查而非聯集外框、移動 QR 在 4×4 像素下仍有效並逐影格更新。移動 QR 的四個影格各自以 ZXing 獨立解碼為 `0007`。使用者的 `1-2 (250ms)` 草稿經檢查：10 個數字目標在每個可見影格都把內容留在當前遮罩內，8 個數字目標保留原 ID 與全部 72 幀。
- 名稱與關號檢查涵蓋：檔名對應關號（`1-1`→1、`1-5`→5、`2-1`→6、`10-5`→50，拒絕 `1-6` 與 `11-1`）、匯入保留完整檔名為顯示名稱、亂序匯入後發布仍用檔名關號、關號衝突時停止發布且草稿不變、跨組保留最後有效關號、無關號檔名沿用自動配號並列警告、重複匯入建立獨立草稿。
- 工作台 GUI 檢查新增：Enter 只在非文字編輯狀態結束路徑選取、依目前範圍重新套用到全部藍格（含既有手寫內容）、答案範圍用盡時整批失敗並保留原內容。
- 已校正兩份既有草稿的名稱與關號（只改 `levelNumber` 與 `displayName`），逐行比對確認其他資料未變。校正後 SHA-256：`Imported_1-1 (100ms).asset` = `AC8725F1C90013E4D39A0A3EBC175F32D176258FE99102FD6708344411F48A32`、`Imported_1-2 (250ms).asset` = `70B01DD288DA9353E79F2B2858CC18AE554D1A14AB22B1CE790CF62D67D7FFFF`；校正前基線在 `TestResults/AnimatedContent_20261008/Before/`。
- QR 最小尺寸限制：動態 QR 以 4×4 像素模組驗證可解碼，小型 QR 在低解析度匯出仍可能無法掃描；未放大藍格或裁切 QR，實體掃碼距離與面積未測。
- 報告：[匯入 89 項](../TestResults/AnimatedContent_20261008/SandboxIsolated02/TestResults/AsepriteImport_20261008/importer-checks.txt)、[ProductionVerification 110 項](../TestResults/AnimatedContent_20261008/SandboxIsolated02/TestResults/production-checks.txt)、[基礎 48 項](../TestResults/AnimatedContent_20261008/SandboxIsolated02/TestResults/prototype-checks.txt)、[工作台 GUI 12 項](../TestResults/AnimatedContent_20261008/WorkbenchGUI_Final3/workbench-checks.txt)。
- **未執行**：Unity 工作台人工操作、新 Player Build 與自測、實體掃碼機、TouchService、感測器與場域驗收。本輪未建置 EXE、未 commit／push。
- `Assets/ScanPro/Levels` 目前沒有任何正式關卡，試玩場景關卡清單為空，資料夾最後寫入時間 2026-10-08 15:56:59，早於本輪修改；本輪未刪除或建立正式關卡。`Level_05.asset`（SHA-256 `D5B63D1F5B36B6DDC8B67B5A98F5D5492BE484FEE0A3E5CBDB641FF1FD64ECB3`）的隔離副本快照仍可用於還原。

## 2026-10-08 QR 消失、靜態藍格編號與最後一關刪除

- 使用者提供的 `1-1 (100ms)` 關卡匯入後，單張 `blue_count` 圖辨識出 12 個分離區域並由上到下、由左到右編為 `1`～`12`；單張 `blue_scan` 圖產生 10 個獨立 QR 目標，各自使用唯一四位碼。
- 在該匯入關卡中提交有效掃碼後，測試確認目標狀態完成、剩餘目標減一，且目標區域不再包含掃碼藍色像素。Player 收到有效掃碼後同步立即重繪，以免等候下一個定時畫格。
- 工作台刪除邏輯以隔離資產驗證：草稿可刪除；正式關卡刪除後會從 Player 場景引用清單移除；即使刪除最後一關也允許，場景清單可留空；另存新的正式關卡會恢復引用。唯讀關卡仍受保護。關卡編號格式測試涵蓋 `1-1`、`1-5`、`2-1`、`2-5`、`10-5`。
- Unity 2022.3.62f3 隔離 Sandbox 匯入驗證 **59 項**、基礎驗證 **42 項**及完整 ProductionVerification **110 項**通過；ProductionVerification 已重跑。完整 ProductionVerification 使用保留圖形裝置的 batchmode；`-nographics` 不支援其中 GPU 全螢幕像素檢查。
- 目前正式資料夾與試玩場景只有 `Level_05`（`1-5`）。刪除它後一鍵 Build 會拒絕建置，直到另存新正式關卡；未重建來源不明的 `Level_01..04`。目前開啟的正式 Unity Editor 記錄顯示腳本編譯成功、無 Assembly 錯誤，工作台按鈕尚未人工點選驗收。
- 新版五關 Player 建置成功：`Builds/2026-10-08_704_CurrentLevels_MultiWindow_DualScreen_ScanDisappearFix/ScanPro704.exe`。Sandbox 輸出 142 個檔案複製後逐一 SHA-256 一致；正式 `Level_01..05.asset` 也與匯入前基線一致。
- **未執行**：新版 Player 自測或人工遊玩、Unity 工作台刪除按鈕人工操作、實體掃碼機、TouchService、感測器與場域驗收。自動資料／渲染測試不代表上述驗收。
- 報告：[匯入 59 項](../TestResults/AsepriteImport_20261008/importer-checks.txt)、[基礎 42 項](../TestResults/AsepriteImport_20261008/prototype-checks.txt)、[完整 110 項](../TestResults/AsepriteImport_20261008/production-checks.txt)。最後一關刪除測試 log：`TestResults/AsepriteImport_20261008/Sandbox/Logs/unity-import-delete-allowed-20261008.log`；ProductionVerification 重跑 log：`TestResults/AsepriteImport_20261008/Sandbox/Logs/unity-delete-allowed-production-20261008.log`。舊版 Player Build 的 142 檔比對報告及尚未執行 Player 自測的限制仍適用。

## 2026-10-08 Aseprite ZIP 匯入、影格轉換與工作台試玩

- 新增 Unity Editor ZIP 匯入器及工作台按鈕。完整 370×270 合成關卡包可轉成獨立草稿；檢查涵蓋底色排除與任意標記色、非等比例最近像素轉換、斜角分區、自然數影格順序及共用 350ms、資料夾答案、QR、紫格長按／放開恢復、`shiftRed`／`shiftGreen` 狀態、15 顆心上限／失敗重啟，以及草稿保存重開。完成藍格動畫循環後仍保持完成。
- `blue_count` 資料夾名無法解析成非負整數或加減式時會略過並顯示警告；缺共用速度的 ZIP 會拒絕建立草稿。初版匯入 API 與資料保存／重開檢查為 40 項；後續把使用者 `1-1 (100ms)` 樣例與最後一關刪除檢查納入後，`AsepriteZipImportVerification.Run` 最新共 **59 項通過**。最新報告：[importer-checks.txt](../TestResults/AsepriteImport_20261008/importer-checks.txt)，最後一關刪除 Unity log：`../TestResults/AsepriteImport_20261008/Sandbox/Logs/unity-import-delete-allowed-20261008.log`。
- 使用提供的 `4-1 (350ms)` 雷射槍資料夾只驗證可讀格式與 350ms 標記；`yellow/` 及未標 `shiftRed`／`shiftGreen` 的 shutter 會保留為警告，不推測玩法；樣例缺少藍色目標，不能單獨完成試玩。另附可實際匯入及試玩的完整合成測試包：[ScanPro_Importer_Fixture (350ms).zip](../TestResults/AsepriteImport_20261008/ScanPro_Importer_Fixture%20(350ms).zip)。
- 同一隔離副本的 `ProjectSetup.ValidatePrototype` **42 項**、`ProductionVerification.Run` **110 項**及真實工作台 `EditorWindow` GUI **9 項**通過。工作台 GUI 報告：[workbench-checks.txt](../TestResults/AsepriteImport_20261008/workbench-checks.txt)；基礎／完整報告：[prototype-checks.txt](../TestResults/AsepriteImport_20261008/prototype-checks.txt)、[production-checks.txt](../TestResults/AsepriteImport_20261008/production-checks.txt)。log 分別為 `unity-production-verification.log`、`unity-workbench-verification.log`。
- `Level_01..05.asset` 未經本次匯入、測試或正式專案 Editor 儲存；完成後 SHA-256 列於 [formal-level-sha256.tsv](../TestResults/AsepriteImport_20261008/formal-level-sha256.tsv)。五關 Player Build 已建立，但尚未執行自測；也未手動完成 Unity GUI 檔案選擇流程，雙螢幕、TouchService、掃碼機、感測器及場域未驗收。
- 可延續的測試工程副本留在 `TestResults/AsepriteImport_20261008/Sandbox/`。自動檢查不等同工作台端到端人工操作或實體設備驗收。使用者已確認紫格放開恢復、`shiftGreen` 不受 Floor 限制、起始／重啟 15 心及加心上限 15；時間與結束流程留待後續。

## 2026-10-08 安全區地板放置限制

- 依使用者確認，安全區只能出現在 Floor／face 2。`VenueLayout` 現以 Floor 來源矩形 `(100,100,320,220)` 檢查完整佔位範圍及所有路徑點；`LevelValidation`、Unity Inspector 與 `ScanProWorkbench` 的預覽、放置、鏡射、拖曳及路徑新增都套用此限制。違規 Inspector 編輯會被拒絕且不寫入資產；建置前的 `LevelValidation` 也會報錯。
- 五關共 **105 個既有 SafeGreen 物件**全數符合地板範圍；未修改正式關卡資產。隔離副本 `TestResults/SafeGreenFloor_20261008/Sandbox` 執行 `ProductionVerification.Run`，**109 項完整檢查通過**；`ProjectSetup.ValidatePrototype` **42 項通過**。其中 9 項新增檢查覆蓋地板內放置、牆面／跨界拒絕、路徑端點、關卡驗證、五關現有安全區及 Inspector 原子拒絕。五份正式關卡雜湊對照：[formal-level-sha256.tsv](../TestResults/SafeGreenFloor_20261008/formal-level-sha256.tsv)。報告：`TestResults/SafeGreenFloor_20261008/Sandbox/TestResults/production-checks.txt`、`prototype-checks.txt`；log：`TestResults/SafeGreenFloor_20261008/unity-production-gpu.log`。
- 使用真實 `EditorWindow` 另跑工作台 GUI 驗證，**9 項通過**（原有 7 項加安全區牆面拒絕／地板接受 2 項），exit code 0；報告：`TestResults/SafeGreenFloor_20261008/Sandbox/TestResults/workbench-checks.txt`；log：`TestResults/SafeGreenFloor_20261008/unity-workbench-gui.log`。
- 一次使用 `-nographics` 的嘗試在 GPU 全螢幕檢查失敗，改以一般圖形模式重跑後 Unity exit code 0、完整驗證通過。正式五份 `Level_01..05.asset` 前後 SHA-256 一致。本次未建立新 EXE、未重跑 Player／QR／TouchService，也未測實體感測器、掃碼機、雙螢幕或場域；目前交付 EXE 仍是 VenueHud_04。

## 2026-10-07 DPI 座標修正與最新五關交付版

最新交付 EXE：`Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_VenueHud_04/ScanPro704.exe`。在隔離專案 `TestResults/RuntimeMultiWindow_20261007/Sandbox` 以正式五關資產執行 `BuildCurrentLevelsFromWorkbench`，Build 輸出為 `Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_14`，再複製到正式交付資料夾。142 個檔案逐一 SHA-256 相符。EXE SHA-256：`F5F73D8616A2500E0FB0223D83774E2D7F6A74C1BBDAD772F4FEFC9BF5812036`；`Assembly-CSharp.dll` SHA-256：`F7123BB869BC7C5637EC432ED06DD581CE977864CE93FFBE81FAF0D2A1BBE72B`。清單與雜湊見 `TestResults/DeliveryVerification_20261007_VenueHud04/`。

- 修正鎖格視窗專用執行緒的 DPI 座標模式：建立視窗前切換至 Per-Monitor DPI Aware V2，讓 `GetWindowRect`、`GetMonitorInfo` 與視窗建立使用一致的實體螢幕座標。先前只檢查兩個後台視窗是否落在同一個顯示器，未檢查鎖格視窗矩形是否完整留在主螢幕，因此漏掉縮放比例不同時跨界遮住部分第二螢幕輸出的情況。
- 隔離 Build exit code 0。從正式交付路徑執行 Player 自測，exit code 0、**106 項通過**；新增檢查確認自測能切換到 per-monitor DPI-aware 座標、鎖格視窗與控制視窗屬於同一主螢幕，且鎖格視窗矩形完整包含於主螢幕實體像素範圍。四張場域輸出截圖各抽樣 **5,475** 點，與獨立等比例參考 **0 mismatch**，底部 512px 黑帶保留。報告：`TestResults/PlayerSmoke_VenueHud04_Delivery/runtime-checks.txt`；log：`Logs/venue-hud04-player-smoke-delivery-20261007.log`。
- 新交付包 142 個檔案與隔離 Build `_14` 的 SHA-256 全數相符；正式 `Level_01..05.asset` 雜湊與隔離副本相符，未由本次 Build 或測試寫回／改動。雜湊對照：`TestResults/DeliveryVerification_20261007_VenueHud04/`。
- 本輪未重跑 18 項 QR 專項、完整 ProductionVerification、TouchService 或 5 秒桌面錄影；QR／規則與先前短錄影結果見下方既有紀錄。短片未重現頻繁閃爍不代表長時間穩定性，手動雙螢幕操作、焦點與場域仍待驗收。


## 2026-10-07 完整驗證修正（VenueHud_03，前一交付版）

VenueHud_03 交付 EXE：`Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_VenueHud_03/ScanPro704.exe`。該版在隔離專案 `TestResults/RuntimeMultiWindow_20261007/Sandbox` 使用正式五關資產建置，Build 輸出為 `Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_11`，再複製到交付資料夾。142 個檔案逐一 SHA-256 相符。EXE SHA-256：`F5F73D8616A2500E0FB0223D83774E2D7F6A74C1BBDAD772F4FEFC9BF5812036`；`Assembly-CSharp.dll` SHA-256：`8BC6901036178A5D78C049599A4781C5CA8FED7965F8CDA0C080D2228BE4C2A7`。清單與雜湊見 `TestResults/DeliveryVerification_20261007_VenueHud03/`。

- 修正 `VenueLayout` 格子遮罩基準：alpha 有效且 RGB 任一通道非黑即視為原圖可見格中心，再扣除固定入口與記分板區。基礎檢查先核對原 PNG 有 **1704** 個可見格中心，再核對固定區排除 **40** 格後保留 **1664** 個可編輯格；舊測試把原圖格數誤當成排除固定區後數量，已改成分開核對，沒有刪除檢查。
- `ProductionVerification.Run` 在隔離副本完整通過 **100 項**；基礎 `ProjectSetup.ValidatePrototype` 通過 **42 項**，Unity exit code 0。涵蓋固定記分板可映射顯示但拒絕觸碰、最上層顏色命中、關卡映射、入口／路徑、QR 與遊戲規則。報告：`TestResults/RuntimeMultiWindow_20261007/Sandbox/TestResults/production-checks.txt`；log：`Logs/production-verification-final-sandbox-20261007.log`。
- 正式專案回寫程式後，`VerifyCurrentChanges` **37 項**及真實工作台 GUI `WorkbenchVerification.Run` 當時 **7 項**均 exit 0。後者涵蓋預覽重繪、暫停快取、播放動畫頻率及 Undo；報告位於 `TestResults/Feature_Verification_20261007_Formal/`、`TestResults/WorkbenchVerification_20261007_Formal/`。2026-10-08 最新工作台 GUI 驗證新增安全區放置案例後為 **9 項通過**，見本文件首節。
- Player 從最新正式交付路徑啟動，exit code 0，**102 項**通過。四張輸出截圖各抽樣 **5,475** 點與獨立等比例參考 **0 mismatch**，512px 底部黑帶保留。報告與影像：`TestResults/PlayerSmoke_20261007_VenueHud03_Delivery/`；log：`Logs/venue-hud03-player-smoke-delivery-20261007.log`。
- 全螢幕／QR 專項 exit code 0，**18 項**通過：九個實際 Player 截圖中的 QR 色塊維持正方形；九個四位碼由本次 Player 自測輸出的 **2688×3840** 場域 atlas 擷取，依輸出面的四分之一轉向做獨立 ZXing 解碼並吻合目標碼。控制視窗截圖的水平裁切位置依 `fullscreenHorizontalOffset = -0.13` 計算。這是數位 atlas 解碼，不是實體掃碼機、QR 物理面積或掃碼距離驗收。報告：`TestResults/PlayerSmoke_20261007_VenueHud03_Delivery/fullscreen-qr-checks.txt`；log：`Logs/venue-hud03-fullscreen-qr-20261007.log`。
- 本次只回寫五個程式／驗證檔：`VenueLayout.cs`、`ProjectSetup.cs`、`ProductionVerification.cs`、`PrototypeGame.SmokeTest.cs`、`FullscreenVerification.cs`。五份正式關卡 `Level_01..05.asset` 與隔離副本雜湊一致，未由本次 Build／驗證儲存或覆寫；雜湊對照見 `TestResults/DeliveryVerification_20261007_VenueHud03/formal-level-sha256.tsv`。

安全區已依使用者 2026-10-07 澄清，限定出現在地板（Floor／face 2）；範圍與觸碰效果仍未定義，程式未自行將整面地板設為免傷或禁放。實體雙螢幕人工驗收、TouchService、感測器原始封包、掃碼機、投影／LED 路由、長時間穩定性及 Unity 工作台完整人工編輯流程仍未測。

### VenueHud_03 後台短時補測

- 從最新交付路徑啟動 Player，以 FFmpeg gdigrab、FFV1 無損、1536×960、30 FPS 擷取 5 秒，共 143 幀。鎖格區域（桌面座標 x=980、y=260，500×550）逐幀平均亮度差平均 **0.000751/255**、最大 **0.106582/255**，超過 1/255 的影格為 0；本次短片仍未重現頻繁閃爍。
- 同機螢幕列舉為主螢幕 1536×960、第二螢幕 1536×864。Win32 視窗列舉確認 Unity 控制視窗 (26,26,722,730)、鎖格視窗 (760,26,768,861) 均在主螢幕內；Unity Secondary Display 視窗存在且為 (1536,0,1536,864)。這是視窗存在與幾何檢查，非手動視窗操作驗收。
- 第二螢幕擷取當時顯示前景瀏覽器，未能直接目視確認其後方場域輸出。因此第二螢幕畫面、視窗前景／焦點、投影／LED 與雙螢幕現場流程仍待人工驗收。短時錄影與視窗列舉不代表長時間或場域驗收。
- 報告：TestResults/RuntimeMultiWindow_20261007/DesktopCapture_VenueHud03/stability-checks.txt；影片：TestResults/RuntimeMultiWindow_20261007/DesktopCapture_VenueHud03/primary-dashboard-stability-venuehud03-5s.mkv。

## 2026-10-07 記分板 HUD（前一交付版；已由 VenueHud_03 取代）

交付 EXE：`Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_VenueHud_01/ScanPro704.exe`。從隔離專案 `TestResults/RuntimeMultiWindow_20261007/Sandbox/Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_09` 複製整包至正式交付路徑；142 個檔案逐一比對 SHA-256，完全一致。EXE SHA-256：`F5F73D8616A2500E0FB0223D83774E2D7F6A74C1BBDAD772F4FEFC9BF5812036`。建置 log：`Logs/venue-scoreboard-build-20261007.log`。

- Player 自測從正式交付路徑執行，報告 **101 項通過**：`TestResults/VenueScoreboardHud_20261007/PlayerSmoke_Delivery/runtime-checks.txt`。涵蓋五關、第一螢幕兩個獨立視窗、鎖格及其快取重畫、記分板 HUD、遊戲規則與畫面輸出。四張輸出截圖各抽樣 5,475 點，與等比例參考均為 0 mismatch；512px 底部黑帶保留。
- `ProductionVerification.VerifyVenueScoreboardHudOnly` 獨立專項檢查 **8 項通過**，log：`Logs/venue-scoreboard-only-20261007.log`。固定記分板顯示組內進度（例如 `1-5`）與剩餘秒數（例如 `60s`）；倒數進入逾時時仍顯示，文字限於固定記分板區並沿用 face 6 方向校正。截圖：`TestResults/VenueScoreboardHud_20261007/PlayerSmoke_Delivery/player-704-output.png`。
- 五份正式關卡資產 SHA-256 均與隔離副本相符；Build／驗證沒有儲存或改寫正式關卡。
- 當時完整 `ProductionVerification.Run` 在底圖基準檢查中止；後續已確認舊檢查混淆原 PNG 可見格數與固定區排除後可編輯格數。此歷史失敗已由上方最新版本的 42 項基礎檢查及 100 項完整 production suite 取代；原始失敗 log 保留作為歷史紀錄。

安全區位置規格已依使用者 2026-10-07 澄清，限定在地板（Floor／face 2）。安全區範圍及觸碰效果尚未定義，因此沒有擴大解讀成整面地板免傷、禁放或其他玩法。實體雙螢幕、TouchService、掃碼機與場域投影仍未驗收。

## 2026-10-07 多視窗後台與閃爍修正版（前一交付版，HUD 加入前）

交付 EXE：`Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_FlickerFix_02/ScanPro704.exe`。由隔離專案副本 Build，複製至正式 `Builds` 路徑後再從交付路徑執行 Player 自測。Unity Build log：`Logs/flicker-fix-build-02-20261007.log`；交付版 Player log：`Logs/flicker-fix02-player-smoke-delivery-20261007.log`；完整測試報告：`TestResults/PlayerSmoke_20261007_FlickerFix02_Delivery/runtime-checks.txt`。

- Player 自測 **100 項通過，程序 exit code 0**。除兩個 HWND、鎖格點擊／解鎖外，測試會透過 Windows API 分別移動、縮放兩個視窗並還原原位置／尺寸；縮放後鎖格快取會重畫；連續一秒發布 60 個相同格圖快照時，不會排程重畫或重新繪製離屏緩衝。
- 鎖格視窗保留並重用 GDI 離屏緩衝；只有格圖快照或視窗尺寸變更才重畫，視窗曝光時直接複製快取影像。`WM_ERASEBKGND` 仍由視窗處理程序抑制，避免先清背景再繪圖造成閃爍。
- Windows 主螢幕以 30 FPS 擷取最終 5 秒短片，共 143 幀。排除起始 1.2 秒程式啟動畫面後，鎖格區 107 幀（約 3.57 秒）的逐影格平均亮度差平均為 **0.00086／255**、最大 **0.09150／255**，沒有任何影格超過 **1／255**；後台與整個主螢幕最大逐影格平均差分別為 **0.15112／255**。這次短時本機錄影未重現頻繁閃爍。影片：`TestResults/RuntimeMultiWindow_20261007/DesktopCapture/primary-dashboard-stability-final-5s.mkv`；分析：同資料夾 `stability-checks.txt`。另一段 12 秒錄影遭 Windows 開始選單及裝置連結面板覆蓋，導致桌面變暗及畫素改變；該段差異數據已排除，不作閃爍結論。短片擷取和像素分析不等同長時間現場操作驗收。
- 本機當時枚舉到主顯示器 1536×960 及另一顯示器 1536×864；Player 記錄場域相機指定至 `Display 1`，Unity 主控制視窗為 `Windowed`、902×912。這確認該 Windows 工作階段的顯示器列舉與程式路由紀錄，不能證明場域投影／LED 輸出方向或設備驗收。
- 四張 Player 場域截圖與獨立 aspect-fit 參考各抽樣 5,475 點，均為 0 mismatch；512px 底部黑帶檢查通過。這是場域畫面資料檢查，不用來判斷後台閃爍。
- 交付版與隔離 Build 的 `Assembly-CSharp.dll` SHA-256 均為 `034B412C89F3722A03F92724838A908F65FF11B17E1F848A2E1EB26DE20335A9`；五份正式關卡資產在本次 Build／複製前後 SHA-256 一致。Build 與 Player 自測均在隔離副本執行，正式專案關卡未透過 AssetDatabase 儲存。

仍待驗收：長時間場域操作中的畫面穩定性、手動拖移／縮放兩個視窗後的位置及焦點、第二顯示器缺席 fallback、TouchService、感測器原始輸入、實體掃碼機、704 投影及 LED 路由。未將這些項目記為通過。

## 2026-10-07 五關現有關卡雙螢幕版（多視窗修正前版本）

舊版 EXE：`Builds/2026-10-07_704_CurrentLevels_DualScreen_06/ScanPro704.exe`。這是加入第一螢幕多視窗及閃爍修正前的五關版本；建置 log 為 `Logs/BuildCurrentLevels_20261007_Delivery.log`，Player 自測 log 為 `Logs/PlayerSmoke_20261007_Delivery.log`。

- 工作台／規則檢查 **37 項通過**：`TestResults/Feature_Verification_20261007_Delivery/current-change-checks.txt`。涵蓋每關圖層順序及紅蓋藍命中、路徑小數秒停留與秒／幀換算、水平／垂直鏡像路徑、拖曳形狀、15 顆新關預設、數字藍格升冪、鎖格／無敵、Build 與 CAVE 視角工具等。
- Player 自測 **91 項通過**：`TestResults/PlayerSmoke_20261007_Delivery/runtime-checks.txt`。涵蓋五關、後台元件、遊戲輸出指定至第二顯示器（若存在）、第一顯示器全螢幕、遊戲規則與模擬觸控／掃碼。
- 四張 Player 畫面截圖各抽樣 **18,511 點**，和獨立等比例參考均為 **0 mismatch**；場域 atlas 的 512px 底部黑帶保留。Player log 確認第二顯示器偵測及場域相機路由到第二顯示器，第一顯示器進入 1920×1200 無邊框全螢幕。
- 五份關卡資產與 `PrototypeGame.unity` 在建置前後雜湊一致；`704_Output.asset` 增加 Player 必需的 shader 參照。工作台 Build 直接使用目前正式關卡，不執行初始化範例流程。
- 該舊版只有一個 Unity Player 後台視窗；最新版多視窗與閃爍修正版的 100 項自測記錄見上節。

本機自測不是雙實體螢幕人工目視驗收；也未測 TouchService、感測器原始輸入、實體掃碼機或 704 場域。找不到第二台螢幕時目前會把場域輸出退回主螢幕並記錄警告，該 fallback 尚未人工操作驗收。未宣稱設備／場域通過。

## 2026-10-07 第一關升序與左移補償建置（先前展示版，已由上方五關版取代）

目前第一關交付 EXE 為 `Builds/2026-10-07_704_Level01_Ascending_LeftCompensated_Fullscreen/ScanPro704.exe`，由 `TestResults/Build_20261007_Level01_Sandbox` 隔離副本建置，Unity batch build exit **0**。場景只包含 `Level_01`，失敗約 2.4 秒後自動重試；水平留黑補償設定為 -0.13，不裁切完整 atlas。正式專案五關資產 SHA-256 與 Sandbox 副本相同；本輪正式專案修改 `704_Output.asset` 的補償值，並調整 `PrototypeGame.SmokeTest.cs` 以檢查展示版只載入第一關。

Player 自測 **53 項通過**，包括只有 Level_01、全螢幕 2688×3840 atlas、橫向螢幕比例／黑邊、Raw Input 啟動及遊戲內觸碰／掃碼規則；1920×1200 截圖的 18,511 個取樣點與獨立等比例參考 **0 mismatch**。升序與掃碼來源分流另有 **8 項通過**，含先按最大值與中途錯序均不完成、由小到大按完通關。截圖：[Player 輸出](../TestResults/Build_20261007_Level01_Ascending_LeftCompensated/player-704-output.png)；報告：[Player 自測](../TestResults/Build_20261007_Level01_Ascending_LeftCompensated/runtime-checks.txt)、[升序檢查](../TestResults/Build_20261007_Level01_Ascending_LeftCompensated/level01-sequence-checks.txt)。

這個水平補償只使用非原生長寬比螢幕左右的黑邊空間；在 2688×3840 原比例下沒有左右留白，因此參數不會平移畫布。測試中的觸控／掃碼是本機模擬；本輪未重跑隔離 TouchService 串接。實體掃碼機、TouchService 現場服務、投影位置、QR 物理面積仍待驗收。

先前 `Builds/2026-10-06_704_Fullscreen_Centered_EditorTools` 是五關原型版本，Player 未做當時的人工畫面驗收；舊 AspectFit 的 179 項及本輪之前的 14 項方向／筆刷／QR 檢查均為各自版本的歷史結果。

這份較早的 AspectFit 記錄所指 production failure 是歷史狀態；截至 2026-10-07 最新交付已通過完整 ProductionVerification 100 項。工作台完整人工編輯、Undo 與試玩流程仍未由使用者人工驗收。

測試及 Player build 均在隔離專案副本執行；沒有呼叫正式專案的儲存或建置準備流程。五關結尾狀態 SHA-256 見 [正式關卡雜湊清單](../TestResults/EditorTools_20261006/formal-level-sha256.txt)。本次開始時讀到 `Level_02.asset` 為 `E2E8D7D347850C7ADF958EFB59C9E008EEA101D21A8FCE54F79479DC65258D8F`，含 4 個物件記錄；Unity Editor PID 18744 開啟期間，檔案於 2026-10-06 17:41 寫入，後續雜湊為 `4915944D86B01BC0BC0B1E7E310A2287FF0D89209C903CD0C4EB5C2DC3BB7820`，含 66 個物件記錄。Editor log 表示該載入物件當時為 dirty。寫入來源無法確認；本輪沒有呼叫儲存、覆寫或還原它。`Level_01.asset` 與更早基線 `A26A7ABCD07783AA74A872CAF8ABD0AD413B76AB3A0866D1FC5963D8724C0CC4` 不同的問題仍未釐清，未回復。

## 前一版 AspectFit 的已通過驗證

| 驗證層 | 結果 | 報告 |
| --- | --- | --- |
| Unity 基礎規則、資料／完整循環路徑 | 41 項 | `TestResults/AspectFit/prototype-checks.txt` |
| 固定入口／遊戲／QR／協議／GPU比例及七面方向 | 81 項 | `TestResults/AspectFit/production-checks.txt` |
| 場域 EXE、正確比例／原生解析度、無 GUI、五關及四種畫面 | 53 項 | `TestResults/WindowsPlayer_AspectFit/runtime-checks.txt` |
| 實際全螢幕截圖 QR 方形及獨立解碼 | 4 項 | `TestResults/WindowsPlayer_AspectFit/fullscreen-qr-checks.txt` |
| renderer 逐像素 RGBA 一致（先前入口階段） | 30 組 | `TestResults/Performance/pixel-parity-checks.txt` |
| 本輪方向／筆刷／鏡像刪除／QR | **14 項通過** | `TestResults/EditorTools_20261006/current-change-checks.txt` |
| 工作台重畫／外部變更／播放／復原（先前入口階段） | 7 項 | `TestResults/Workbench_Performance/workbench-checks.txt` |

前一版 **AspectFit** 等比例全螢幕修正有 **179** 項檢查（41+81+53+4）通過，build、Player及QR驗證均 exit **0**，產物為 `Builds/2026-10-06_704_Fullscreen_AspectFit`。三種GPU比例、七面方向、原生解析度與實際QR形狀皆納入。這些結果不驗證目前的 `Fullscreen_Centered`。前195項結果未驗長寬比，僅確認拉伸畫面和拉伸參考一致；144／177／140也屬歷史結果。核心canvas／工作台與TouchService未變，未重跑先前30／7／6項。

前AspectFit版本的檢查數、尺寸與遊戲組件SHA-256保存在[交付核對](../TestResults/AspectFit/delivery-verification.json)。該版之後又加入置中微調和入口動態移除；這些改動未驗收。

規則包含 40 機台同碼並行去重、USB 字元交錯分流、跨關舊碼拒絕、同群組紅格去重、紫格長按、淺藍切換、Pro 順序／雙點保持／倒數、alive 清除、七面反向映射。QR 由 QRCoder 產生，再以獨立 ZXing 解碼。

入口新增 13 項 production 檢查：底圖全白仍封鎖、六類物件皆拒絕、有效端點之間不能穿越入口、恰好貼邊路徑可用、位置／大小／路徑的 Inspector 修改會原子取消、合法路徑可存、原生 Inspector 使用防護、輸出設定不能移動／刪除入口、face 5 觸控總是拒絕。Player 再檢查所有關卡入口都沒有物件或移動路徑。

前 AspectFit Player 無需 `--output`，全螢幕使用顯示器原生解析度；實測1920×1200，完整atlas等比例顯示為840×1200，左右各540px黑色，內部2688×3840與底部512px黑帶保留。Win32無標題列／邊框、全monitor、隱藏游標、一般build及GUI排除通過。四張截圖各18511個取樣像素，與獨立CPU使用單一縮放比例的參考完全一致；實際兩個QR物件維持方形且獨立解碼吻合。GPU測試另外涵蓋1024×1920與2688×3840，但不是這兩種實體顯示器驗收。

顯示器索引在Unity OutputProfile設定，支援原生解析度與目標視窗移動；本機只有一台顯示器，第二台實際移動、現場LED路由／安裝方向／物理比例尚待驗收。七面CW90°由GPU四象限診斷圖驗證，沒有任意旋轉畫布。

GIF先前經Pillow讀取20幀、520×420、100ms／幀；renderer30組及工作台靜態／暫停不上傳紋理、外部／Undo更新保留先前報告，本次未重跑。效能耗時也是先前量測；範圍與限制見 [PERFORMANCE.md](PERFORMANCE.md)。

## 前一版 AspectFit 驗證重跑方式

下列舊命令只重建／驗證 `Fullscreen_AspectFit`，不能用來驗收目前 `Fullscreen_Centered`。目前版水平微調及入口動畫變更後尚未重跑自動檢查或 Player 擷取。

Unity：`C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe`。本輪使用隔離副本 `TestResults/Performance/Sandbox`，避免鎖定原 Editor 或批次保存原場景。直接在正式專案重跑會建立／保存原型場景，需先保存自己的工作並關閉該專案的其他 Editor。

```powershell
$scanProject = 'C:/Users/momou/Desktop/Task/Research/Claude/ScanPro704'
$scanSandbox = "$scanProject/TestResults/Performance/Sandbox"
$scanUnity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe'
& $scanUnity -batchmode -quit -projectPath $scanSandbox -executeMethod ScanPro704.Editor.ProductionVerification.Run -logFile "$scanProject/Logs/unity-aspectfit-verification.log"
& $scanUnity -batchmode -quit -projectPath $scanSandbox -executeMethod ScanPro704.Editor.PerformanceVerification.CompareReference --perf-results "$scanProject/TestResults/Performance" -logFile "$scanProject/Logs/pixel-parity-entrance.log"
& $scanUnity -projectPath $scanSandbox -executeMethod ScanPro704.Editor.WorkbenchVerification.Run --qa-dir "$scanProject/TestResults/Workbench_Performance" --qa-exit -logFile "$scanProject/Logs/workbench-entrance.log"
& $scanUnity -batchmode -quit -projectPath $scanSandbox -executeMethod ScanPro704.Editor.PrototypeBuild.BuildWindows --build-output "$scanProject/Builds/2026-10-06_704_Fullscreen_AspectFit" -logFile "$scanProject/Logs/windows-build-aspectfit.log"
```

畫面驗證必須讓 Player 視窗可見；隱藏視窗在這台機器無法可靠擷取 GUI，不能拿它的空白截圖當作通過。

```powershell
& "$scanProject/Builds/2026-10-06_704_Fullscreen_AspectFit/ScanPro704.exe" --self-test --qa-dir "$scanProject/TestResults/WindowsPlayer_AspectFit" --touch-uri ws://127.0.0.1:29111/ -logFile "$scanProject/Logs/windows-player-aspectfit.log"
& $scanUnity -batchmode -quit -projectPath $scanSandbox -executeMethod ScanPro704.Editor.FullscreenVerification.VerifyPlayerCapture --qa-dir "$scanProject/TestResults/WindowsPlayer_AspectFit" -logFile "$scanProject/Logs/fullscreen-qr-verification.log"
```

獨立 TouchService 測試：以 Python 執行 `Tools/verify_touchservice.py`。工具複製指定 checkout 的當前原始碼到測試快照，使用 UDP **29001**／WS **29111**，關閉 UI／托盤／autoLock／UDP publisher，送出合法模擬原始封包，由真正的 `TouchServiceClient` 收包完成藍格並清除離開觸點；結束後停止自己建立的程序。原本 config 的 SHA-256 前後一致，既有工作樹不變。

## 尚未驗證

手動拖移／縮放第一螢幕兩個後台視窗後的位置與焦點、真正雙螢幕人工目視驗收及缺少第二台螢幕 fallback、Unity 工作台完整人工編輯／儲存／匯出流程（含圖層、路徑時間與完成鍵、上下／左右鏡像、形狀拖曳／純筆刷、欄位寬度、一鍵 Build、CAVE 右鍵旋轉）、實體 USB 掃碼機和前綴韌體、TouchService／感測器原始封包、現場同時 2～10 人、投影／LED 路由、牆面可讀方向與物理比例、20 cm²、130cm 掃碼距離、完整 GUI FPS 及長時間壓力。每組安全區已限定在 Floor／face 2，但其範圍與觸碰效果仍待定義。完整 50 關和正式場次流程未完成。
