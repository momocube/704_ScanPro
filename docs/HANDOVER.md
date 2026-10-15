# 704 掃碼 Pro 交接

## 2026-10-13 Build 畫面變形修正

- `VenueOutputPresenter.OnRenderImage` 原本在 destination 為 null 時使用主後台的 `Screen.width/height`，讓第二顯示器套用錯誤比例。現在使用該相機本次影格尺寸；明確 RenderTexture 輸出仍採目的紋理尺寸。新增 `SCANPRO_PRESENTATION` 尺寸變動 log，供現場核對 display／frame／operator／source。
- 新 `DisplayAspectVerification` 補上 null destination 的 GPU 回歸，涵蓋 1920×1080、1024×1920、2688×3840、720×960，以及角落方向、完整內容、黑帶與水平偏移。舊程式 21 過／13 失敗，修正版 34 項全過；QR 68 項與目前工作台 GUI 33 項亦通過。
- Release Build：`Builds/2026-10-13_704_ImportedLevels_DualScreen_AspectFix/ScanPro704.exe`；144 個交付檔案與隔離建置 SHA-256 相同。保留目前滑鼠掃碼／移除手動過場的程式、五份匯入關卡與 1-3 ClearMask，不重建 Levels。
- 既有正式資產／scene／meta 共 89 檔雜湊未改。受測與正式來源一致；Sandbox 場景經建置儲存後只有換行差異。證據：`TestResults/DisplayAspectFix_20261013/`。
- 本機只偵測到一個顯示器。舊版已啟動並關閉；啟動新版時使用者按 Esc 停止 Computer Use，故新版 Player 畫面及縮放檢查未完成，不能算雙螢幕實機驗收。TouchService、兩支實體掃碼器與 704 投影仍待測。專案非 Git repo，未 commit／push。

日期：2026-10-13（Asia/Taipei）。本文件記錄工作台與遊戲原型、2026-10-07 完成的十一項工作台／遊戲／Player 功能、雙螢幕 DPI 座標修正、2026-10-08 動態內容跟隨與檔名命名修正、2026-10-12 工作台修正，以及尚待人工和現場驗收的範圍。**2026-10-13 起，試玩場景直接引用 1-1～1-5 匯入關卡；`Assets/ScanPro/Levels` 保持空白，不需另存正式副本，也不要還原 `Level_01..05`。**

## 2026-10-13 試玩滑鼠掃碼與移除過場預覽操作

- 工作台「試玩」與 Unity Editor 遊戲場景 Play Mode 的平面視圖都可用滑鼠直接點擊可見藍色 QR 目標，依該回合目標代碼模擬掃碼；藍色觸摸格、紫色長按維持原操作。只有平面視圖支援此滑鼠輸入。
- 已移除工作台的「過場預覽」區、選取物件的成功過場預覽按鈕，以及工作台與 Play Mode 操作台的手動掃碼欄位。成功／失敗／逾時過場仍由實際遊戲狀態觸發；實體掃碼器 Raw Input 流程不變，Play Mode 操作台仍顯示最近掃碼與連線狀態。
- 共用 `GameSession.TryScanAt` 以執行期相同的可見圖層、完成狀態、動畫遮罩與命中規則找出掃碼目標，再提交該回合代碼；不會讓被上層物件遮住的 QR 被點擊完成。
- `WorkbenchVerification` 原有掃碼步驟已改用同一命中入口，未執行此驗證器；本次也未執行 Unity 編譯、GUI／Play Mode 測試或 Player Build。正式 Editor 操作、實體掃碼器與場域仍待驗收。專案不是 Git repository，未 commit／push。

## 2026-10-13 匯入關卡直接輸出與紫色 ClearMask 預設

- 使用者要求已匯入關卡直接視為可輸出資料，不再另存正式關卡或轉換一次；追加確認之後紫色機關首次匯入就要是 ClearMask 行為。此規則優先於下方歷史草稿發布限制。
- `PrototypeBuild.FindOutputLevels` 統一收集既有正式關卡及 `LevelDrafts` 中有 ZIP 來源資訊的匯入關卡，排除 `excludeFromOutput`，依現有關號排序。工作台新增「納入遊戲輸出」；匯入預設納入、手工未完成草稿不自動納入。匯入關卡的另存正式按鈕停用，原始資產及 GUID 保持不變。
- 工作台 Build 與 `704 → 輸出 704 雙螢幕遊戲` 共用直接建置流程，先驗證再同步場景引用，不再呼叫產生範例的 Prepare。開啟遊戲場景及進入該場景 Play Mode 時使用同一輸出清單；刪除匯入關卡也會清除其場景引用。
- 目前場景直接引用 1-1、1-2、1-3 ClearMask、1-4、1-5。原 1-3 只新增 `excludeFromOutput: 1` 保留作備份，未重新解析 ZIP 或修改各關玩法內容；`Assets/ScanPro/Levels` 保持空白。
- 新匯入紫色機關固定從 `to(clearwrong).png` 保存 ClearMask，摘要顯示已套用；缺少或全空遮罩則報錯並保留既有資料。手工紫格的既有回退邏輯保留。
- 隔離 Unity 2022.3.62f3：直接輸出專項 **23 項**、既有 ZIP 匯入 **90 項**、真實工作台 GUI **33 項**通過。一般 Release Player Build 成功並 exit 0，交付 **144 檔案 SHA-256 相同**。證據：`TestResults/ImportedOutput_20261013/`。
- 新交付：`Builds/2026-10-13_704_ImportedLevels_DualScreen/ScanPro704.exe`，附 `關卡清單.txt`，搬移需複製整包。尚未執行此 Player 自測、實際 Play Mode 進出、輸出勾選人工操作、TouchService、實體掃碼器或 704 場域驗收；原有 Player 自測不能算此版通過。專案非 Git repo，未 commit／push。

## 2026-10-13 QR 藍底與場域輸出追修

- 使用者確認 2×2 與 3×3 以上都要保留藍色背景，清晰度問題位於場域輸出。`GameCanvasRenderer` 為方形／動畫 QR 保留外框，圓形保留內接卡片外的底色；淺藍機關仍為淺藍底，白色淨空保留。
- 小型 QR 改按原生 atlas 像素對齊：2×2 方形為 128px，QR 卡片 116px、模組 4px、外側各約 6px 藍框；來源至少 7 倍。較大 QR 同樣以整數模組繪製，避免不同畫布倍率造成 QR 大小跳動。墨色改為近黑色；工作台 atlas RenderTexture 使用 Point 取樣。
- 隔離 Unity 2022.3.62f3：QR 專項 **68 項**、ProductionVerification **165 項**、真實工作台 GUI **33 項**通過。涵蓋原生／半尺寸數位輸出、前導零、2／3／4／6 格、圓形、淺藍、動畫第二幀、藍框與最終模組等寬；半尺寸解碼驗收不含最小圓形 QR。
- 同一 QR 專項對原碼得到 43 過／25 失敗，重現外框與倍率差異。既有完整回歸假設原色，故只將 Sandbox 色彩 fixture 設為 100%；正式專案紅 71%／過關藍 49% 設定保留。驗證、受測來源與樣張：`TestResults/QrClarity_20261013/`。
- 同期 A／B 模擬介面變更保留。未建置 Player，未測實體掃碼機、TouchService 或投影；數位解碼不代表場域驗收。專案不是 Git repository；未 commit／push。

## 2026-10-13 A／B 雙掃碼器與模擬預設

- 使用者確認兩支實體掃碼器已分別輸出 `A/`、`B/`，需要同時使用。現有 Raw Input 按 USB 裝置分開組碼，遊戲同時接收所有完整訊框，沒有用模擬欄位限制只啟用其中一支；此接收流程保持原樣。
- 工作台模擬前綴由 `SIM-A` 改為 `A`；工作台與 Play Mode 操作台新增「填入 A／填入 B」，保留手動輸入。按鈕只設定該次模擬事件來源，不切換實體掃碼器。
- Unity 2022.3.62f3 隔離來源驗證：基礎 60 項、ProductionVerification 165 項通過，batch exit 0。包含 A／B 字元交錯組碼、CR／LF 不重送、40 個並行來源同碼只完成一次、不同目標不互相阻塞及淺藍跨機台去重。證據在 `TestResults/ScannerPrefixes_20261013/`。
- 同步保留核對期間的 atlas 預覽取樣設定變更後，真實工作台 EditorWindow 32 項亦通過，未記錄 EndLayoutGroup；A／B 按鈕人工點按與實體輸入仍未驗收。
- 尚未建置 Player 或測試兩支實體掃碼器、TouchService、704 場域；現有 EXE 不包含這次 Editor 模擬介面變更。正式關卡、草稿與場景雜湊未改，未 commit／push。

## 2026-10-13 CAVE 取景、2×2 QR 與記分板關號

> 本節 QR 滿幅繪製為中途版本，已由上方「QR 藍底與場域輸出追修」取代；CAVE 與記分板規則保留。

- 依使用者要求，CAVE 預覽使用 520×420 展開圖貼牆、原房間幾何與外側第三人稱相機；保留環視與重設。左牆仍顯得過低後，再調預設取景 home offset 為 `(9,20,-37)`、FOV 52，使左牆以較正面、較高的比例出現在畫面。牆面幾何與文字方向未改。
- 牆面不再改整體角度。只有 TouchBlue 文字（數字、算式與底線）在藍格內獨立旋轉；CAVE／VenueOutput 藍格文字逐像素同角度，QR、記分板、牆面、格子幾何、輸出映射與觸碰位置保持原座標。
- 2×2 QR 使用完整方形範圍（QRCoder 矩陣內含標準淨空），每模組至少 3 個渲染像素；獨立 ZXing 解碼確認最小 QR 經場域 atlas 映射後仍可讀。記分板依 `displayName`／匯入檔名顯示關號並去除 `(250ms)` 等影格速度後綴，缺少有效檔名時退回內部關號格式。
- Unity 2022.3.62f3 隔離 GPU `ProductionVerification` **165 項通過**；真實工作台視窗 GUI **32 項通過**，涵蓋切換 CAVE／atlas、反覆 Repaint 與建立／刪除草稿。CAVE 截圖與 QR atlas 解碼證據在 `TestResults/CavePreviewFollowup_20261013/`。
- `EndLayoutGroup: BeginLayoutGroup must be called first.` 曾在正式 Editor.log 出現，堆疊只有 Unity GUI host；本次隔離工作台測試切換預覽及刷新草稿時未重現。ScrollViewScope 防護仍保留，但原錯誤根因及正式 Editor 是否已清除尚未確認。
- 未建置 Player、未測 TouchService、掃碼機或實體投影；本次驗證均在隔離副本，未改正式關卡或場景。ScanPro704 不是 Git repository，未 commit／push。

## 2026-10-13 ZIP 匯入自動偵測 touch 數量與答案上限

- 匯入器以建立完成的 `TouchBlue` 目標數作為 N。`blue_count` 圖片內四向分離的連通區各建立一個目標；同一序列影格由既有追蹤配成同一目標時只計一次。QR、危險區及其他圖層不計入。
- 偵測到 N 個 touch 目標時，草稿自動答案範圍設為 1～N，並在匯入摘要顯示；沒有數字 touch 目標時保留既有預設範圍。ZIP 原有藍格 label 不覆寫，此範圍供後續自動產生數字／算式使用。
- 答案最大值支援至 9999，且自動分配允許涵蓋 0～9999 的有效整數範圍；偵測數超過 9999 時上限設為 9999 並列警告。
- 本次未執行匯入測試或 Unity 編譯；人工匯入操作尚未驗收。專案不是 Git repository，未 commit／push。

## 2026-10-13 先前 atlas CAVE 方向稽核（預覽已後續回復）

> 歷史狀態：本節記錄的 atlas 觀眾視角是中途版本，已依使用者要求恢復 10/12 CAVE 預覽；目前狀態與驗證以本文開頭及 `docs/VALIDATION.md` 第一節為準。

- 使用者確認實際投影方向仍錯，並指定依附圖位置判斷右牆為正向參考；正向定義為觀眾站在 Floor 上看向四面牆時，牆上數字可正常閱讀。
- 以最終 atlas 的 Face 4 右牆方向校正 Face 0、1、3 牆面文字。CAVE 預覽改由最終場域 atlas 資料逐面貼到房間牆面，從 Floor 內側觀眾視角檢視，避免只比對 renderer 中間紋理而漏掉 atlas 面映射。
- 重新稽核後確認 2026-10-12 的 `NumberCaveVerification` 50 項及完整 `ProductionVerification` 151 項，沒有涵蓋最終 atlas shader 的全部面別映射；不可將舊方向檢查視為實際投影方向驗收。
- 最新 Unity 2022.3.62f3 隔離 GPU `ProductionVerification` **163 項通過（exit 0）**，包括最終 atlas Face 0／1／3 對 Face 4 的方向檢查，及 Floor 觀眾視角左、前、後、右四牆的非對稱 `12` 標記檢查。證據在 `TestResults/CaveProjectionFix_20261013/Artifacts/`，逐項報告在 `TestResults/CaveProjectionFix_20261013/Sandbox/TestResults/production-checks.txt`。
- 正式專案 Unity 2022.3.62f3 batch-mode 編譯與 domain reload 成功，exit 0；log 在 `TestResults/CaveProjectionFix_20261013/official-project-compile.log`。未建置 Player，未測實際 TouchService、掃碼機或投影設備；**實體場域方向仍待驗收**。正式關卡及場景未更改；專案不是 Git repository，未 commit／push。

## 2026-10-12 數字字形、場域輸出方向與 CAVE 左牆（核心已實作）

> 方向測試更新：本節的 50／151 項是 10/12 歷史結果，後續稽核確認沒有覆蓋最終 atlas 逐面映射；以 10/13 的 163 項 GPU 回歸為準。

- 使用者回報自動填入的數字字形奇怪、場域輸出數字仍非 CAVE 正向，以及 3D 左牆消失；之後要求依規劃製作。範圍依澄清限定為現有輸出畫面的數字方向，沒有新增操作面板或 HUD。
- `GameCanvasRenderer` 改為手繪 5×7 數字點陣，窄體 `1` 保留單格 `10`／`11`／`12`；容量檢查與繪製共用 `MeasureTextGrid`。2／5／6／9 底線與字身間保留一列空白。CAVE 牆面方向作為基準，場域文字依 atlas 順時針映射反向補償；平面與地板不變，記分板 face 6 走相同方向入口。
- 左牆幾何原本存在；純黑空白紋理與深色預覽背景混在一起。CAVE shader 現對空白黑面加低對比底色，預設及面向左牆的隔離預覽皆能辨識牆體。
- Unity 2022.3.62f3 隔離 Unity `NumberCaveVerification` **50 項通過**，完整 `ProductionVerification` **151 項通過（exit 0）**：涵蓋 1–12／算式容量、底線空列、手動與自動 label 逐像素一致、四個牆面文字方向逐像素對照、記分板、QR 解碼與相關執行期回歸。專項樣張／CAVE 截圖／場域輸出 PNG 在 `TestResults/NumberCaveFix_20261012/Artifacts/`；完整回歸逐項記錄在 `TestResults/NumberCaveFix_20261012/Sandbox/TestResults/production-checks.txt`。詳見[修正规劃](數字字形_場域輸出_CAVE修正规劃_2026-10-12.md)及[驗證記錄](VALIDATION.md)。
- 正式 Unity Editor `Editor.log` 已於 2026-10-12 17:06:59 記錄本次腳本建置成功及 assembly reload；尚未人工操作工作台、建置或自測 Player，也未連接實際 TouchService 服務、測掃碼機或實體場域。完整回歸只核對 TouchService 訊息解析。正式關卡目錄與場景清單仍空，不可自行重建關卡；本輪沒有改寫關卡／場景或輸出設定，也未 commit／push。

## 2026-10-12 紫格只清除 clearwrong 遮罩與 frames 重疊區（最新規則）

- 紫格長按期間只讓來源 `to(clearwrong).png` 遮罩覆蓋到的連結危險區／`frames/` 區域透明；遮罩沒有覆蓋的 frames 圖案仍顯示、仍保留碰觸效果。透明格露出下層並依可見圖層規則命中；放開後清除區恢復。紫格本體的長按變白效果保留。
- ZIP 匯入會把清除遮罩保存到 `LevelItem.purpleClearMask`。舊手工草稿若沒有此欄位，程式只用紫格當前圖形作為局部清除範圍，不會整組隱藏 `targetGroups`。正式專案已新增 `Assets/ScanPro/LevelDrafts/Imported_1-3 (100ms) - ClearMask.asset`，由來源 ZIP 重匯入並含 3/3 紫色機關清除遮罩；原有 `Imported_1-3 (100ms).asset` 保留未覆寫，工作台選名稱含 `ClearMask` 的草稿可試精確遮罩。
- Unity 2022.3.62f3 隔離副本驗證：紫格遮罩 **41 項**、原始 Aseprite ZIP 匯入 **99 項**（含來源 1-3 遮罩）、色彩／圖層回歸 **65 項**、shutter 影格 **174 項**通過。隔離圖在 `TestResults/PurpleMaskFix_20261012/imported-1-3-clear-mask-preview.png`；各 Unity log 同資料夾，細節見 `VALIDATION.md`。
- 正式 Unity Editor（PID 35004，專案路徑為本 checkout）於 2026-10-12 15:44:18 的 `Editor.log` 記錄腳本建置成功及 assembly reload；15:54 的 Asset Pipeline Refresh 記錄新增 1 個資產，表示遮罩草稿已匯入 Editor。尚未人工操作工作台長按驗收。原有 1-3 草稿 SHA-256 為 `11113641FB7190468016B25DAB5396510FDCBD69C3D0FD3C32EBC71AC0493E1F`，新增遮罩草稿 SHA-256 為 `9E56DEDC170C0BF3851490E115D74BBA3C5598C925DC9C900A2679E3F0396C38`；來源 ZIP SHA-256 `32FE09324792C78C89D39717A92C0B351746D0FE115AA1744C2994E3CDE34675`，未修改。
- 未建置 Player，未測 TouchService、實體掃碼機或場域。專案不是 Git repo，未 commit／push。

## 2026-10-12 淺藍機關每次掃描只切換一幀

- 使用者回報 1-3 牆面被 shutter 全部影格覆蓋。最新規則取代下方較早的「未選區仍畫紅色」：起始只顯示第一幀，每次有效掃描前進一幀，末幀後循環。未選紅／綠影格不繪製、不造成傷害，也不攔截露出的下層目標；選中的紅區保留扣心。
- `GameCanvasRenderer` 與 `GameSession.TopmostHit` 同步略過未選 shutter。工作台預覽模式的模擬掃碼原本只回報接受，現會同步推進預覽 session；預覽與試玩的去重皆使用當前關卡靜默間隔，重送不多跳。
- 隔離驗證與前後圖在 `TestResults/ShutterFrames_20261012/`。新 `ShutterFrameVerification` 核對正式 1-3 草稿的四組機關、每組全部狀態及循環，並以移除未選影格的參考畫面逐像素比對；另驗證三幀紅／綠、重送、時間不自動切換、下層藍格與紫格清除遮罩共存。工作台 GUI 同時驗證預覽與試玩 callback、重送及重繪。最新結果見 `VALIDATION.md`。
- 本次修改位置為 `Research/Claude/ScanPro704`，不是專案管理工具；未改寫舊來源 ZIP、草稿、正式關卡、場景或色彩設定，也未重建 EXE。同期紫格改為局部清除遮罩的程式修改保留，沒有回復成整群組隱藏。專案非 Git repo，未 commit／push。

## 2026-10-12 紫格整組透明（歷史實作，已被上方局部遮罩規則取代）

- 此前版本把整個 `targetGroups` 連結群組隱藏；當時的 39 項結果屬該歷史行為，不能代表目前要求。使用者補充 frames 有些區域不應被紫格覆蓋後，已由上方的精確／局部遮罩規則取代。

## 2026-10-12 共用飽和度、固定圖層與 CAVE 即時環視（已完成）

- 使用者確認所有關卡共用一組色彩設定。工作台右側新增「全關卡共用色彩」，紅色飽和度只影響關卡紅區，藍色只影響成功過渡；藍格、QR、淺藍、入口與失敗紅色過渡不變。100% 保留原色，0% 為灰階；支援 Undo，按「儲存全域色彩」保存到 `Assets/ScanPro/Resources/704_GameColors.asset`。一鍵 Build 也會儲存這份共用設定；既有 EXE 未更新。
- 最新要求取代自由圖層排序：模板固定上至下「綠 > 紅 > 紫 > 淺藍 > 藍」。刪除 `WorkbenchLayerOrderEditor`，工作台及 Inspector 均無排序操作；執行期繪圖、編輯選取與觸碰共用固定模板。舊資產 `layerOrder` 僅保留相容欄位，不再改變行為，未改寫任何關卡。
- 使用者補充「一直切換視窗才看得到有動」後確認根因：相機變動只呼叫 `Repaint`，`previewDirty` 為 false 時不會重畫 CAVE RenderTexture。現在相機有獨立 dirty 標記，拖曳／重設視角的下一次 Repaint 立即重繪，暫停亦可操作且不重新上傳遊戲紋理。右鍵放開、失焦與切視圖會釋放拖曳。
- 新增「過關：藍色擴散」預覽按鈕；手動過場改用共用 renderer，平面／CAVE／場域輸出均可看到飽和度與過場變化。
- 隔離 Unity 2022.3.62f3 共 **117 項通過**：色彩／CAVE 專項 40、既有工作台 GUI 22、QR／圖層／紫格規則回歸 55。專項透過真實 EditorWindow 滑鼠／鍵盤事件驗證欄位輸入、儲存按鈕及連續環視；相同 CAVE 測試在修正前版本重現相機移動但畫面不更新。工作台既有測試的固定 2× 像素座標及 Color/Color32 比較不適用新增的 3×3 QR fixture，已改按實際 QR 倍率取樣後通過。
- 測試及重跑工具在 `TestResults/ColorCave_20261012/`；草稿、正式關卡目錄與場景檔案 SHA-256 前後一致。未建置 Player，未手動操作正式 Editor，未測 TouchService、掃碼機或場域。專案無 Git repo；未 commit、未 push。

## 2026-10-12 掃碼模擬文字欄位無法輸入（已修正）

- 使用者回報「機台前綴／四位數代碼」不能填寫。`DrawToolButton` 原本只判斷 `GUILayout.Toggle` 的回傳狀態，已選中的工具每次 Layout、Repaint、鍵盤事件都會呼叫 `GUI.FocusControl(null)`，使文字欄位持續失焦；修正為只有工具值改變時才執行切換及清除焦點。
- 隔離 Unity 2022.3.62f3 的真實工作台視窗，以滑鼠／鍵盤事件驗證六種工具及試玩模式，47 項通過：逐字輸入前綴／代碼、前導零、Backspace／補字、路徑工具 Enter 不攔截文字編輯，以及實際按送出後接受掃碼、目標完成並減少剩餘數量。
- 同一測試對修正前程式重現「點入後失焦、前綴仍為 SIM-A」；三份草稿及場景修改前後 SHA-256 相同，未改正式關卡清單。工作期間已有其他工作台輸出控制精簡修改，本修正保留該現況。
- 測試只在 `TestResults/WorkbenchFix_20261012/Sandbox` 插入控制項座標記錄，不直接設定輸入值；測試工具及結果保存於 `TestResults/ScanInput_20261012/`。正式 Editor reload 後的人工點按仍待使用者確認，未建置 EXE、未測實體掃碼機或 TouchService。
- 移除 Sandbox 座標記錄、同步正式來源後，既有 WorkbenchVerification 21 項亦通過；本輪共 68 項檢查。

## 2026-10-12 工作台輸出控制精簡

- 工作台移除場域 Profile 欄位與選取按鈕、編輯圖 PNG 匯出、PNG 圖層選單及 4 秒路徑 GIF 匯出；僅保留場域 PNG 匯出入口。`704_Output.asset` 設定資產及場域輸出流程保留。
- 顯示器索引及水平微調仍可從 Project 視窗選取 `Assets/ScanPro/Data/704_Output.asset`，在 Inspector 編輯。
- 本次未執行 Unity 編譯或 GUI 驗證；關卡資料、場景及 Player Build 未修改。

## 2026-10-12 Console／QR／預設圖層與 1-3 匯入修正（已實作）

- 工作台工具列提供「清空 Console」；針對按鈕無反應回報，清除後會重繪已開啟的 Console 視窗並顯示剩餘訊息數；全數清除時顯示「已清空」，仍有錯誤時顯示剩餘筆數。另修正 `GameCanvasRenderer.cs` 的 CS0172。隔離 Unity 真實 EditorWindow 驗證 21 項通過，涵蓋工作台 callback、回饋狀態、清除後新增訊息與重複清除。正式 `Editor.log` 於 2026-10-12 14:28:09 記錄本輪腳本編譯成功及 assembly reload；工作台按鈕實際滑鼠點按仍待確認。
- 藍色及淺藍色掃碼格的尺寸下限為 2×2 格，含動畫影格、圓形與矩形；工作台、原生 Inspector、ZIP 匯入和關卡驗證一致。渲染器會提高 QR 整數模組倍率。物理面積 20 cm² 尚未校正，仍須另行實測。
- 此階段先統一新建與 ZIP 匯入預設排序；同日後續要求已將全部關卡固定為綠 > 紅 > 紫 > 淺藍 > 藍，排序介面移除，以上方最新紀錄為準。
- `1-3 (100ms).zip` 的十個藍色目標皆為 1×1 格；`10` 因雙位數文字邊距太大被渲染器略過。調整文字內距與自動文字尺寸檢查後，合成回歸及原始 ZIP 都確認 1～10 可見。
- 隔離 Unity 2022.3.62f3 驗證：Console 3 項、Aseprite 匯入 88 項、基礎 51 項、ProductionVerification 136 項、真實 EditorWindow GUI 更新後 21 項通過。結果及限制詳見 [工作台修正規劃](工作台修正規劃_2026-10-12.md) 與 [VALIDATION](VALIDATION.md)。
- 追加 shutter 紅色顯示修正：切換中未選取的紅色危險區維持紅色像素，但 `Disabled` 安全判定不變。當時紫格停用區仍顯示綠色；此顯示已由上方最新紫格透明修正取代。隔離 `RunShutterColorVerification` 6 項通過，並載入正式草稿 `Imported_1-3 (100ms).asset` 確認 4 個未選 shutter 區、至少一區實際可見紅色。草稿 SHA-256 為 `2652EA8D5FF79811B9775C8E192E2634DCF25C899A19987FA685E3064BC38C98`，本次只讀，未寫入。報告及初始狀態預覽：`TestResults/WorkbenchFix_20261012/ShutterRed/`。完整 ProductionVerification 在該舊 Sandbox 另因 `Level 1 visible object footprints fit supplied map` 檢查失敗而中止，未宣稱全套通過。
- 正式關卡資料夾與場景關卡清單仍空；本次未建置 Player。實體掃碼機、TouchService 與場域未測。

## 2026-10-12 工作台 IMGUI 佈局錯誤（程式已修，隔離 GUI 驗證通過）

- 使用者回報 `GUILayoutGroup.GetNext`／`BeginLayoutArea` 的 Repaint 例外。堆疊沒有 ScanPro 呼叫框架，單靠堆疊無法確認唯一來源；檢查工作台後找到高風險分支：按 Enter 結束路徑時，程式把選取狀態變更延到下一個 Layout，卻在按鍵事件先提早返回，跳過工作台其餘 GUILayout 控制項的繪製。
- 已移除該提早返回。按鍵仍會被處理，結束路徑的選取狀態仍在下一個 Layout 事件更新，而按鍵事件會繼續走完整個工作台 GUI 繪製流程，避免只繪製部分控制項。
- 2026-10-12 隔離工作台 GUI 驗證共 17 項通過，包含 Enter 結束路徑的條件判定與真實 EditorWindow 的反覆 Repaint；尚未在正式 Unity Editor 人工實際按 Enter 重現操作。未儲存場景或關卡，未建置 Player。
- 下一步：在隔離 Unity 副本以路徑工具按 Enter 結束選取，並確認重繪不再出錯；再人工操作算式範圍欄位、重新套用和物件選取。

## 2026-10-12 紫色長按顯示與門檻

- 紫格連續長按預設 **0.1 秒**後整個可見形狀立即變白，按住期間維持，放開後恢復紫色及清除區。按住時只有來源 `to(clearwrong).png` 遮罩與連結紅區／`frames/` 重疊的部分透明，未覆蓋部分仍顯示並保留碰觸；紫格不繪製 H、文字或圖示。詳細規則見本文件上方「紫格只清除 clearwrong 遮罩與 frames 重疊區」。
- 新關與 ZIP 匯入使用 `purpleHoldSeconds = 0.1`；逐關可調。2026-10-12 紫格修正時將當時存在的兩份匯入草稿改為 0.1 秒，當時 1-3 不在 `LevelDrafts`；之後使用者已在正式 Editor 匯入 1-3，現存第三份草稿見本文件上方 SHA-256 記錄。
- 執行期紫格顯示狀態獨立追蹤，避免沿用持續停用旗標造成放開後白色殘留；每個紫格與每條觸點分別計時，共用紅組任一紫格仍生效時會維持停用。
- 隔離 Unity 驗證 **20 項通過（exit 0）**；報告見 `docs/VALIDATION.md`。Player build、TouchService、掃碼機與場域未測。

## 2026-10-08 動態關卡數字／QR 跟隨藍格（已修正）

- 原因：`AsepriteZipImporter.AddSequence` 把所有影格的聯集範圍存成 `LevelItem.position/size`，各影格實際範圍另存 `LevelMaskFrame.offset/width/height`。藍格本體繪圖已使用當前影格 offset，但數字／QR 仍以聯集 position/size 定位與排版。`Imported_1-2 (250ms)` 的數字 1～8 共用聯集起點 `(13,13)`、尺寸 `26×16` 格（實際每格 3×3、共 72 幀），八個文字因此放大重疊在畫布中央，形成截圖裡的白色矩形；8 個 QR 同樣留在固定位置。
- 修正：新增 `LevelMaskFrame.TryGetContentRect` 與 `LevelItem.TryGetContentBounds`，提供「目前影格的實際位置、尺寸與是否可見」。繪圖與內容排版共用同一影格時間，內容位置改為物件位置加目前影格偏移，尺寸取目前影格範圍。數字與算式在當前影格內置中並自動縮放，保留 2、5、6、9 底線；QR 維持正方形、完整模組與白邊。空影格同時隱藏格子與內容；不規則遮罩取最大完整內接矩形，面積相同時優先接近影格中心，再比上、左位置。牆面文字方向依當前位置計算，平面、CAVE、場域輸出與匯出共用同一修正。
- 物件 ID、答案、QR 內容與完成狀態保留，完成後不因動畫循環重新出現；一般手動路徑物件維持原有移動方式。資料格式不變，`position/size` 仍是整段動畫範圍，既有匯入草稿直接套用修正。
- 連帶修正地圖驗證：入口、記分板、地板與場域範圍原本以動畫聯集外框判定，現改為依影格實際有內容的格子檢查；手動路徑物件沿用原檢查方式。
- 驗證：隔離副本 `AsepriteZipImportVerification.Run` 89 項通過，涵蓋逐影格內容範圍、空影格隱藏、不規則遮罩、移動 QR 四個影格各自以 ZXing 獨立解碼為 `0007`，以及 `Imported_1-2 (250ms)` 全部 10 個數字目標在每個可見影格都把內容留在當前遮罩內、8 個數字目標保留原 ID 與 72 幀。報告：`TestResults/AnimatedContent_20261008/SandboxIsolated02/TestResults/AsepriteImport_20261008/importer-checks.txt`。

## 2026-10-08 匯入名稱與關號依檔名（已實作）

- 匯入只移除 `.zip`，完整檔名寫入 `displayName`，例如 `1-2 (250ms)`；速度仍獨立解析（250ms）。從檔名開頭解析 `組別-關次`，支援 `1-1`～`10-5`，內部編號為（組別－1）×5＋關次，`1-6`、`11-1` 等會被拒絕。
- 工作台清單、試玩標題、執行期後台與刪除確認使用完整名稱；`LevelIdentifier` 保留為簡短關號，排序用關號、顯示用名稱。記分板維持原有進度格式。
- 發布匯入草稿保留檔名指定的關號，不再改配下一個空號；關號已被占用時指出衝突並停止發布，草稿與既有關卡都不變。重複匯入仍建立獨立草稿。無法解析關號的檔名可以匯入，保留完整名稱、沿用自動配號並顯示警告。
- 已校正兩份既有草稿，只改名稱與關號：`Imported_1-1 (100ms).asset` 為 `levelNumber: 1`、`displayName: 1-1 (100ms)`（原 6）；`Imported_1-2 (250ms).asset` 為 `levelNumber: 2`、`displayName: 1-2 (250ms)`（原 1 與 `1-1`）。逐行比對確認只有這兩個欄位變動，ID、物件、遮罩與資產引用不變。校正後 SHA-256：`AC8725F1C90013E4D39A0A3EBC175F32D176258FE99102FD6708344411F48A32`、`70B01DD288DA9353E79F2B2858CC18AE554D1A14AB22B1CE790CF62D67D7FFFF`；校正前基線保存在 `TestResults/AnimatedContent_20261008/Before/` 與 `drafts-before.csv`。

## 2026-10-08 工作台算式範圍輸入與重新套用（已實作）

- 工作台按 Enter 完成路徑的快捷鍵原本會攔截文字欄位輸入，導致算式與答案範圍無法輸入。現在文字欄位正在編輯時不觸發（`ShouldFinishPathSelection` 檢查 `EditorGUIUtility.editingTextField`），範圍欄位改用延遲輸入，輸入完成才套用。
- 新增「重新套用至全部藍格（覆蓋手寫內容）」，依目前答案／運算元範圍重新配置數字或算式，包含已有手寫內容的藍格，並可用 Undo 復原。範圍不足時整批失敗、保留原內容並顯示原因；舊版 Pro 機制未設為「無」時會先提示。
- 驗證：真實 EditorWindow 工作台 GUI 12 項通過，含 Enter 不攔截文字欄位、重新套用覆蓋既有內容、答案範圍用盡時原子失敗。報告：`TestResults/AnimatedContent_20261008/WorkbenchGUI_Final3/workbench-checks.txt`。

## 2026-10-08 正式關卡資料夾現況

- `Assets/ScanPro/Levels` 目前沒有任何關卡資產，`Scenes/PrototypeGame.unity` 的關卡清單為 `levels: []`。資料夾最後寫入時間為 2026-10-08 15:56:59，早於本輪草稿校正；本輪沒有刪除、建立或儲存任何正式關卡。
- 沒有正式關卡時一鍵 Build 會拒絕建置。`Level_05.asset` 的既有快照（SHA-256 `D5B63D1F5B36B6DDC8B67B5A98F5D5492BE484FEE0A3E5CBDB641FF1FD64ECB3`）仍保存在多個 `TestResults/*/Sandbox/Assets/ScanPro/Levels/` 隔離副本中，需要時可依使用者指示還原；未經確認不自行重建。

## 2026-10-08 QR 顯示、工作台關卡管理與編號

- 有效 `ScanBlue` 掃碼後會立即呼叫遊戲渲染，已完成 QR 目標從畫布移除；來源 `1-1 (100ms)` 匯入驗證確認掃碼後 `Remaining` 減一且該 QR 區域沒有藍色像素。
- 關卡流水號每五關一組，使用 `1-1`～`1-5`、`2-1`～`2-5` 等格式。工作台清單、遊戲標題與操作台按此顯示；ZIP 匯入配置下一個可用流水號並保留原檔名作匯入資訊。
- 工作台新增「刪除關卡」，可刪目前可編輯的正式關卡（包含最後一關）及 `Assets/ScanPro/LevelDrafts` 草稿／匯入資產；刪除正式關卡時同步從 `PrototypeGame` 場景移除引用。刪除最後一關會讓場景清單留空；沒有正式關卡時一鍵 Build 會拒絕建置，另存新正式關卡後會重新建立場景引用。唯讀關卡不允許刪除。
- 隔離 Sandbox：匯入驗證 59 項、基礎驗證 42 項、ProductionVerification 110 項通過；ProductionVerification 已於 2026-10-08 重新執行。匯入測試明確驗證可刪除最後一個正式關卡、清空場景引用、另存正式關卡後恢復引用；正式關卡素材未變。工作台刪除按鈕尚未在 Unity GUI 人工點選。2026-10-08 五關 Player Build 已建立於 `Builds/2026-10-08_704_CurrentLevels_MultiWindow_DualScreen_ScanDisappearFix/`，142 檔與 Sandbox 建置逐一 SHA-256 相同；Player 自測、實體掃碼機、TouchService 與場域尚未執行。

## 2026-10-08 安全區只能放在地板

- 使用者確認每組安全區只會出現在 Floor／face 2。已在 `VenueLayout` 定義 Floor 來源區域 `(100,100,320,220)`，安全區完整佔位範圍及所有路徑點都必須位於該區域；因地板區域為凸矩形，端點足跡皆在區內即可保證點間直線移動留在地板。
- `LevelValidation`、Unity Inspector、工作台的預覽／放置／鏡射／拖曳移動／路徑新增及建置前檢查會拒絕違規安全區，並回復 Inspector 的非法編輯。這只限制安全區放置面，不決定範圍或觸碰效果。
- 五關共 105 個現有安全區都符合新限制；正式 `Level_01..05.asset` 未修改。隔離副本 `ProductionVerification.Run` 109 項、基礎檢查 42 項、真實 EditorWindow 工作台 GUI 9 項均通過，報告見 `TestResults/SafeGreenFloor_20261008/`。本次未建置新 Player；現有 VenueHud_04 EXE 未更新，實體設備及場域也未測。

## 2026-10-08 Aseprite ZIP 匯入（階段一至三已實作）

- 在 `704 → 關卡工作台` 加入【匯入關卡包zip檔】。成功時建立唯一的 `Assets/ScanPro/LevelDrafts/Imported_...` 草稿、選取草稿並顯示轉換結果；來源 ZIP 檔名、Asia/Taipei 匯入時間、摘要及警告會保存。取消不改變關卡；讀取或資產建立失敗會顯示錯誤並回復原選取關卡；不覆寫正式關卡或既有草稿。
- 匯入器位於 `Assets/ScanPro/Editor/AsepriteZipImporter.cs`。支援 `basic.png`、`frames/`、`blue_count/`、`blue_scan/`、`trigger/`、有明確 `shiftRed`／`shiftGreen` 標記的 `shutter/`。未知資料夾只列警告；不把雷射槍的 `yellow/` 或未標型態的 shutter 猜成掃碼 Pro 機關。
- 來源 PNG 不等比例鋪滿至 520×420，以最近像素取樣。標記排除黑、白、RGB `(50,50,50)`、`(100,100,100)`、`(162,161,160)`，忽略透明度。圖片內以 4 向連通區分區，斜角相碰者分開；影格自然數排序、每關共用毫秒速度。影格遮罩同時控制繪圖與觸碰。
- `blue_count` 資料夾名稱須為非負整數或有效加減式；無效內容會略過並列警告。完成藍格在動畫循環後不重生。紫色機關達目前長按門檻後，按住期間連結紅區／`frames/` 群組透明並可觸碰下層，放開恢復；`shiftGreen` 依圖片位置，不受 Floor 專屬限制，但仍受場域禁放遮罩限制。匯入關卡預設 15 顆心；加心不超過 15，失敗重啟恢復 15。
- 初版 ZIP 匯入階段的報告為匯入 40 項、基礎 42 項、ProductionVerification 109 項、工作台 GUI 9 項；後續加入使用者 `1-1 (100ms)` 範例及最後一關刪除檢查，最新匯入驗證為 59 項、ProductionVerification 為 110 項，新五關 Player Build 也已建立，完整現況見本文件上節及 [VALIDATION](VALIDATION.md)。Unity GUI 檔案選擇器尚未人工操作；新 Player、TouchService、實體掃碼機及 704 場域未驗收。
- 第四階段待後續：時間與結束流程、營運後台／場次、雙記分板、Windows 成品輸出及場域交付；使用者指定介面素材之後再提供。兒童模式與省電模式暫不納入。每關既有 60 秒原型時間保留，不代表 45 分鐘場次流程已定義。
- 製作規格、階段驗收及剩餘格式注意事項見 [Aseprite ZIP 匯入製作規劃](Aseprite_ZIP匯入製作規劃_2026-10-08.md) 與 [VALIDATION](VALIDATION.md)。

## 2026-10-08 交付與追加功能狀態

- 原待製作清單第 1–10 項及第 11 項的遊戲端功能均已實作；待製作清單改作功能清點及人工／現場驗收表。第一螢幕後台現有兩個可各自移動、縮放的 Windows 頂層視窗：Unity 控制視窗負責狀態、關卡操作及無敵開關；原生鎖格視窗負責 52×42 格圖。
- 已實作：關卡顏色層排序及最上層命中；路徑點小數秒停留與 FPS 秒／幀換算；完成路徑後直接選另一物件；上下／左右鏡像按鈕；緊湊欄位；拖曳形狀與獨立純筆刷；新關預設 15 顆心；全關數字藍格升冪；工作台底部一鍵 Build 現有關卡；CAVE 右鍵旋轉／重設；Player 鎖格／無敵與雙螢幕輸出路由。
- 工作台／規則檢查 37 項、記分板 HUD 專項 8 項、基礎檢查 42 項、最新完整 ProductionVerification 110 項、最新真實工作台 GUI 9 項均有通過紀錄；最新 VenueHud_04 Player 自測 106 項通過，18 項 QR 專項為 VenueHud_03 的既有結果，本次沒有重跑。109 項來源驗證加入安全區只能留在地板的檢查，未重建 Player。Player 自測包含兩個 HWND 分離、鎖格點擊／解鎖、視窗程式移動／縮放／還原、DPI-aware 實體像素範圍、縮放後鎖格重畫、60 次相同快照不重畫、記分板顯示及四張輸出畫面取樣。產物、測試檔與未驗收項目見 [VALIDATION](VALIDATION.md)。
- 鎖格視窗原本每 0.1 秒重新發布並全幅逐格繪製；現改成只有格子狀態或視窗尺寸改變才重畫，並重用離屏緩衝畫面，曝光時直接貼回快取。VenueHud_03 的主螢幕 5 秒短片未重現頻繁閃爍。後續發現舊視窗測試只核對「屬於同一顯示器」，在不同 DPI 縮放比例下會漏掉鎖格視窗跨界遮住第二螢幕輸出的情況；VenueHud_04 已讓鎖格視窗執行緒使用 Per-Monitor DPI Aware V2，並在 Player 自測中要求整個視窗矩形完全位於主螢幕實體像素範圍。此輪未重錄桌面影片，亦不代表長時間場域驗收。
- 程式分工已有多個責任類別，例如 Runtime `GameRuntimeControls`、`RuntimeDashboard`、`VenueOutputPresenter`，以及 Editor `PathTimingEditor`、`WorkbenchLayerOrderEditor`、`WorkbenchMirrorUtility`、`WorkbenchShapeTool`、`WorkbenchExportService`。`ScanProWorkbench` 仍負責主要視窗組裝與部分事件分派；本輪沒有完成全面工作台重構。

使用者原要求曾指定先記錄待製作項目；之後實作已在此工作階段完成並有驗證結果。任何未人工／現場驗收的部分仍須明確標示，不能沿用程式自測宣稱設備或場域通過。

## 現況與入口

- 專案：`C:/Users/momou/Desktop/Task/Research/Claude/ScanPro704`，Unity **2022.3.62f3**。依使用者要求從 Task/704 移至 Claude 資料夾，舊位置已不存在。
- 首批 **5 關可試玩原型**已完成；最新交付版為 `Builds/2026-10-07_704_CurrentLevels_MultiWindow_DualScreen_VenueHud_04/ScanPro704.exe`。由隔離副本 `_14` Build 後整包複製，142 個檔案逐一 SHA-256 相符；交付路徑 Player 自測 106 項通過。QR 專項 18 項通過紀錄來自 VenueHud_03，本次未重跑。正式關卡 `Level_01..05` 與隔離副本 SHA-256 一致，未改寫關卡。實體 TouchService、掃碼機與 704 投影未測。
- Player 在第一顯示器呈現兩個可各自移動、縮放的後台視窗，在第二顯示器輸出場域 atlas；缺少第二顯示器時目前退回主螢幕並記錄警告。一般 Release build 不顯示 Development Build 字樣。VenueHud_03 的 5 秒主螢幕短片未重現頻繁閃爍；VenueHud_04 自測另確認原生鎖格視窗矩形完整落在主螢幕實體像素範圍，修正不同 DPI 導致跨界的問題。Unity Secondary Display 視窗幾何曾經核對，但當時被前景瀏覽器遮住；VenueHud_04 未新錄桌面短片，故頻繁閃爍是否在實際操作中完全排除、第二螢幕畫面／焦點仍待人工確認。長時間場域運作、手動視窗擺放／焦點、實體投影及 fallback 仍未驗收。
- 2026-10-06 使用者補充：工作台畫面不需要 Console／頂部訊息框；目前五關 `Level_01..05` 要可直接編輯與儲存。頂部兩個說明框已移除；工作台與 Inspector 將允許五份既有關卡及草稿編輯，其他正式關卡維持唯讀。只調整權限，沒有主動變更或儲存五關內容。
- 2026-10-06 使用者要求牆面數字及場域牆上的介面文字，以「玩家站在場域內看牆時能正常閱讀」為正向；CAVE 左右側牆依各自 UV→房間高度方向旋轉，EXE 輸出與 Editor 輸出預覽共用方向選擇入口，依 atlas 順時針映射逐面反向補償，平面編輯維持原向。算式及 2／5／6／9 底線沿用相同文字渲染路徑；固定記分板所在 face 6 也納入場域輸出方向區。方向仍待實體場域驗收。Player 不包含 Unity Editor 操作 GUI；2026-10-07 已加入獨立記分板 HUD，文字方向沿用 `TextOrientationForView`／`ResolveTextRotation`。
- 使用者追加要求：入口在所有關卡固定，不允許任何物件或互動。已鎖定來源／輸出幾何，工作台及 Unity Inspector 拒絕位置、尺寸與路徑跨入；建置再次檢查，face 5 觸控直接忽略。最新 EXE 已包含此修正。
- 2026-10-06 使用者要求新增左右對稱、可調中心，以及觸摸藍格的手動／自動數字／加減算式模式，並確認顯示數字 2、5、6、9 皆加底線。已加入草稿及目前五關可用的工作台工具、答案排序、自動內容尺寸限制與數字底線顯示。使用者再追加鏡像模式右鍵刪除同步對稱物件，以及筆刷新增方形／圓形、可套用所有顏色；已實作並於隔離副本執行 14 項目前功能檢查。工作台完整人工操作仍待使用者驗收。
- 關卡保護：目前 `Level_01..05` 可直接編輯／儲存；新草稿位於 `Assets/ScanPro/LevelDrafts`；其餘正式關卡仍唯讀。另存正式關卡會挑選空號並建立新資產。實作開始前留下的 `Level_01.asset` SHA-256 基線 `A26A7ABCD07783AA74A872CAF8ABD0AD413B76AB3A0866D1FC5963D8724C0CC4` 與目前 `1E1F3530D5ADE7519EE35DF57AFFA847154B653CB569FEEBA6D443897B509FB0` 不同；原因不明且沒有匹配副本。本輪只修改程式與文件，沒有回復、改寫或儲存五份關卡內容，須保留現況。
- 本次保護 `Assets/ScanPro/Levels/Level_01..05.asset`：驗證及 Player build 都使用專案隔離副本，未呼叫正式專案的 AssetDatabase 儲存。工作期間注意到 `Level_02.asset` 在 Unity Editor PID 18744 開啟期間於 2026-10-06 17:41 寫入；它從本次開始時觀察到的 SHA-256 `E2E8D7D347850C7ADF958EFB59C9E008EEA101D21A8FCE54F79479DC65258D8F` 變為 `4915944D86B01BC0BC0B1E7E310A2287FF0D89209C903CD0C4EB5C2DC3BB7820`，序列化物件記錄由 4 個變為 66 個。Editor log 提示該 loaded asset 當時為 dirty。此寫入來源無法確認；未覆寫、未還原、未再保存該資產，請依 `VALIDATION.md` 末列雜湊核對。
- 2026-10-06 記分板固定在東小牆整面：編輯區 `(380,0,40,100)`，輸出 face 6／`(2048,2432,640,256)`／CW90°。來源遮罩、輸出設定及 Inspector 已加入固定禁放保護；工作台平面預覽新增紅色標示，新增路徑點也檢查完整循環路徑。既有 EXE 不含這些更新。此次自動編譯曾因 `GameCanvasRenderer.cs` 的區域變數命名衝突失敗，程式已修正但尚未觀察到後續成功編譯。
- 正式 HUD 規則：每組關卡顯示 `1-5` 至 `5-5`（如 `2-5`），每關倒數 60 秒。固定記分板 Player HUD 已完成，顯示組內進度與剩餘秒數，倒數歸零時仍顯示；8 項 HUD 專項檢查及交付版 Player 自測通過。使用者已確認每組安全區只會在地板（Floor／face 2）；範圍與實際觸碰效果仍未定義，不能自行推定玩法。
- 使用者確認全螢幕修正理解後已完成：完整 atlas 等比例置中、黑色邊帶、不裁切，採指定顯示器原生解析度；顯示器索引只在 Unity 的 OutputProfile Inspector 設定。沒有任意旋轉七面配置，GPU 四象限圖已核對位置與 CW90° 方向。
- 使用者回報輸出畫面偏移。最新第一關展示版使用已確認的 -0.13 水平留黑補償並完成 Player 自測；1920×1200 時 atlas 左移約 140px，不裁切。若 704 顯示端是 2688×3840 原生比例，水平補償不會移動畫布；實際投影路由與物理位置仍待現場驗收。
- 先讀 [README](../README.md) 的操作方式、[規格](Specification.md) 的使用者澄清、[驗證](VALIDATION.md) 的實測範圍。
- 共用記憶：`C:/Users/momou/Desktop/Task/Research/Claude/memory/`。
- 根工作區及本專案尚無 Git repository；未 commit、未 push。

## 已製作內容

| 位置（相對 Assets/ScanPro） | 行為 |
| --- | --- |
| Art/704_blank.png、Runtime/VenueLayout.cs | 原始 520×420 圖未重採樣；52×42 編輯格、快取黑區遮罩與來源失效檢查、固定入口與記分板遮罩、完整循環路徑檢查，以及 SafeGreen 僅能放在 Floor 的足跡／路徑限制。 |
| Runtime/ScanLevel.cs、LevelValidation.cs | 六種物件與矩形／方形／圓形幾何、群組、Pro 標記、路徑、每關 60 秒限時預設；SafeGreen Floor 規則與 QR 掃碼格至少 2×2 格檢查。 |
| Runtime/GameSession.cs、ProSequence.cs | 心數／倒數／目標、綠保護、同群組紅格去重、紫長按、淺藍切換、排序／算式／配對／倒數執行器。 |
| Runtime/ScanRegistry.cs、ScannerFramer.cs、RawScannerInput.cs | 隨機唯一四位碼、跨機台原子去重、按 USB handle 組 A/0000 訊框、CR/LF 可選，避免鍵盤交錯混字。 |
| Runtime/TouchServiceClient.cs | 真正 ClientWebSocket、自動重連、主執行緒佇列、型別／序號／尺寸檢查、alive 全量對帳、反向座標映射。 |
| Runtime/OutputProfile.cs、OutputCompositor.cs | 七面正式配置、固定入口、雙向映射（入口觸控拒絕）、邊界 clamp、合成時恢復 RenderTexture.active，保留 GUI。 |
| Runtime/GameCanvasRenderer.cs、CaveRoom.cs | 快取紋理尺寸／底圖、實際 QR、點陣數字／算式、依平面／CAVE／atlas視圖調整牆面字向、色彩、三種過場、入口固定底色（移動藍色小方塊已移除）、CAVE 空間預覽；近牆為 cutaway。 |
| Runtime/PrototypeGame.cs、PrototypeGame.SmokeTest.cs | Player 自動開始／續關及觸控／掃碼；操作台、快捷鍵、滑鼠及雙點模擬只在 Editor。Player 自測原生全螢幕、GUI 排除及四種場域畫面。 |
| Runtime/VenueOutputPresenter.cs、Art/704_FullscreenFit.shader | 完整 atlas 等比例顯示，調整水平微移以置中實際遊戲內容，不拉伸或裁切；704 原尺寸無左右黑邊時不移動。保留底部黑帶；material 重用。 |
| Editor/ScanProWorkbench.cs、Editor/LevelPlacementGuard.cs | 六類蓋章／繪製與拖曳；目前五關及草稿可編輯／儲存，其他正式關卡唯讀；矩形／方形／圓形筆刷、左右對稱繪製與右鍵同步刪除、可拖曳／輸入中心、單件鏡射複製；藍觸摸手動／自動數字／加減算式、答案範圍與排序、清空 Console、套用預設圖層排序。工作台平面預覽標示入口／記分板禁放區，另含群組／大小／路徑、非 Play Mode 平面／CAVE／atlas 預覽及場域 PNG 匯出。輸出 Profile 選取器、編輯圖 PNG／圖層及 GIF 控制已從工作台畫面移除。 |
| Runtime/TouchNumberContent.cs、GameCanvasRenderer.cs、GameSession.cs | 整數答案解析、範圍內一般數字／只含加減的算式分配、方形／圓形視覺與實際命中、格內文字尺寸限制；藍色觸摸格數字 2、5、6、9 加底線。圓形 QR 內縮為方形白底卡片並保留淨空。 |
| Editor/LevelPlacementGuard.cs | 工作台及原生 Unity Inspector 原子拒絕入口／記分板占用、黑區物件與非法完整路徑；輸出 Inspector 不允許移動、刪除或改變兩個固定面的幾何。 |
| Editor/ProjectSetup.cs、PrototypeBuild.cs | 初始化錯誤已修；資料與五關試玩場景生成，選單「輸出 704 全螢幕遊戲」，依台北當日輸出一般 build、2688×3840、FullScreenWindow、禁止全螢幕切換。 |
| Editor/ProductionVerification.cs、TouchServiceVerification.cs | 規則／QR／映射與真正 WebSocket 整合測試。 |
| Editor/PerformanceVerification.cs、WorkbenchVerification.cs | 前後 renderer 量測／逐像素比對、真實工作台視窗的重畫／外部變更／播放／復原驗證。 |
| Editor/FullscreenVerification.cs | 三種實際 GPU 輸出比例的正方形、四角及七面方向驗證；獨立 ZXing 解碼全螢幕截圖，確認實際 QR 方形。 |
| Levels/Level_01..05.asset、Scenes/PrototypeGame.unity | 基本觸摸與掃碼、移動路徑、機關群組、Pro 數字排序、Pro 配對合作。 |
| Data/704_Output.asset | 正式 2688×3840／七面設定；歷史 UNCALIBRATED asset 保留，不作正式輸出。 |

第三方：QRCoder **1.6.0** netstandard1.3（MIT）供執行期 QR；ZXing.Net **0.16.10** netstandard2.0（Apache-2.0）僅供 Editor 的獨立解碼驗證。來源與授權在各 ThirdParty 目錄。空的 NayukiQR 目錄沒有實際依賴。

## 前一版 AspectFit 的實際驗證（不是目前 build）

- AspectFit版本 Unity exit **0**：基礎 **41**、production **81** 項通過，包含入口／規則／QR檢查，新增14項：1920×1200、1024×1920、2688×3840 三種 GPU 正方形及四角、七面位置／CW方向、非法顯示器索引。
- AspectFit版本 build **Succeeded**、Player **53** 項 exit **0**：五關／入口／觸控／掃碼／GUI排除／原生無邊框檢查通過；新增 shader 包含、實際內容比例、原生解析度。四種截圖各18511個取樣點，與獨立CPU等比例參考（包含黑邊）完全吻合。
- AspectFit版本全螢幕截圖獨立 QR **4** 項 exit **0**：兩個藍色 QR 物件維持方形，ZXing 解碼吻合執行期目標碼。1920×1200 顯示器中的內容為840×1200，左右各540px黑色；內部atlas及512px黑帶保留。
- AspectFit版本 **179** 項檢查（41+81+53+4）通過。先前195項只確認拉伸畫面和拉伸參考一致，沒有驗證長寬比。144／177／140也為歷史階段。
- AspectFit階段曾核對30個來源及9個關卡／設定／底圖檔；本次新增水平微調欄位及入口動畫變更後，未重做來源比對。建置用隔離副本，正式只新增Inspector設定欄位，沒有批次覆寫關卡或場景。AspectFit核對在 `TestResults/AspectFit/delivery-verification.json`。
- renderer逐像素30組及真實工作台7項是前入口階段通過的結果。本次核心canvas／工作台未變，未重跑；既有量測及報告保留。
- 隔離 TouchService 的 **6** 項是先前同日通過的結果，本次未重跑：模擬 raw UDP → 指定 checkout 原始碼快照的真實服務 → ClientWebSocket → Unity 藍格完成及離開清除。UDP 29001／WS 29111；原 config SHA-256 不變。
- QR 經獨立 ZXing 解碼，兩個生成碼皆吻合；GIF 經 Pillow 逐幀讀取 20 幀，520×420、100ms／幀；2688×3840 PNG 底部 512px 全黑。
- 新 EXE 的全螢幕 atlas 已目視檢查；成功／踩輸／逾時均有全螢幕截圖與 atlas 一致性檢查。Player 只顯示場域輸出，平面／CAVE 和操作台在 Unity 內使用。工作台完整人工操作流程尚未驗收。
- 同機單次完整預覽平均約 **61～69 ms → 0.9～3.4 ms**，沒有降低解析度。這不是完整 GUI FPS；GC 配置計數在此 Mono 不支援，不能宣稱零配置。詳見 [效能報告](PERFORMANCE.md)。
- `TestResults` 留存全部報告與截圖；`Logs` 留存本輪 build／驗證 log。早期初始化失敗 log 留在舊 Task/704/tmp，屬歷史，不是目前狀態。

## 尚未完成與原型假設

- **先前第一關升序展示版（歷史版本）**：`Builds/2026-10-07_704_Level01_Ascending_LeftCompensated_Fullscreen` 只含 Level_01；現行五關版已在上方記錄，交付時不可拿此歷史產物替代。

- 正式 **50 關**、完整待機／教學／語音、45 分鐘場次管理、low poly 美術製作、牆面紫格影響 Pro 題目容量仍待製作。
- 十類 Pro 執行器已寫入；目前關卡只展示 Numbers7／Pairs7，其他類型仍需完整題目、場域內容及驗收。
- 每段移動 1500ms、紫格起效門檻預設 0.1 秒、淺藍掃描靜默間隔預設 500ms 是可調原型值。紫格達門檻後本體變白；按住期間依 clearwrong 遮罩局部清除，放開恢復紫色與局部清除區。
- `cellWidthCm/cellHeightCm` 仍為 0；不能宣稱 QR 已達 **20 cm²**。物理驗收應量實際 QR（含白底 quiet zone），不能只量外側藍格。
- 後續待辦：安全區只能出現在地板已由程式限制並通過來源驗證；其實際範圍與觸碰效果仍待產品規格定義。Unity 工作台完整人工編輯／儲存／匯出、一鍵 Build 與 CAVE 右鍵旋轉仍待人工驗收。最新隔離副本 ProductionVerification 110 項與基礎 42 項通過；最新 VenueHud_04 Player 自測 106 項通過，QR 專項 18 項為 VenueHud_03 的既有結果，本次未重跑。VenueHud_03 的本機短片未重現閃爍；VenueHud_04 修正 DPI 座標跨界並通過主螢幕完整包含檢查，但未重錄影片。長時間操作、實體雙螢幕、TouchService、掃碼機與場域仍待驗收。
- 實體 USB 掃碼機、實際感測器、2～10 人並行、LED／投影路由、方向、掃碼距離、長時間穩定性仍待驗收。
- GPU shader 針對 704 固定幾何；更改 profile／場域尺寸時需同步修改合成與 CAVE，不是通用場域編輯器。


## TouchService 閱讀結果

指定 checkout：
`C:/Users/momou/Desktop/Task/Research/Claude/NangangProjection/NangangProjection-git/TouchService/`

已讀：`docs/HANDOVER.md`、`docs/PROTOCOL.md`、`docs/CALIBRATION.md` 相關段落、`docs/DEPLOY.md` 相關段落、`venues/704.json`、`config.json`、`Core/VenueProfile.cs`、`Core/SensorFaceConfig.cs`、`Output/FrameSerializer.cs`、`Output/UdpJsonPublisher.cs`。

- .NET 8 獨立服務，接感測器 UDP 20001，輸出乾淨的觸控 JSON。
- 目前本地設定：UDP JSON 至 `127.0.0.1:20110`；WebSocket `ws://127.0.0.1:20111/`；同 port `GET /venue` 可讀 profile。
- 新 Unity 遊戲訂閱輸出即可，不要與服務搶感測器原始 UDP 20001。
- frame：`v, seq, t, venue, canvas, events, alive`。
- event：`id, phase(down/move/up), face, cell, px, norm`。`face` 是 profile 陣列索引；`px` 是合併畫布像素；`norm` 是整張輸出畫布的正規化座標。
- 每包用 `alive` 全量清單清除已離開的 touch；不能只靠 up，UDP 會丟包。
- `type` 訊息非一般觸控 frame；hello 可讀幾何資訊，locks／log／raw／未知 type 不要當 touch frame。
- 實作需處理背景收包→主執行緒、斷線、服務重啟／seq 重置、舊封包、alive 對帳。
- config 的 releaseTimeoutMs=170、keepaliveIntervalMs=1000、autoLock 啟動窗=10 秒。這是本地檔案值，不代表目前現場執行值。
- `rawMax` 可能給出 `origin + 面尺寸` 的邊界 px，雖然 cell 已 clamp；轉回編輯圖前須明確處理邊界。
- TouchService 沒有提供掃碼機前綴解析；掃碼輸入是獨立串接項目。

## 關鍵：編輯圖到輸出幾何

**編輯圖 520×420；TouchService canvas 2688×3840。**

七面的來源矩形與 TouchService 矩形，以「整張編輯圖順時針旋轉 90°，再放大 6.4 倍」可以逐面精確吻合。轉換後內容高 3328，底下另有 **512px** 的不存在區域，保留黑色，不能拉伸填滿。

這是依圖片和本地 profile 的**數學核對結果**，不是已完成的場域投影驗收。現場方向與實際 LED output routing 尚需確認。

矩形以左上角 `(x,y,w,h)` 表示：

| face | TouchService 名稱 | 編輯圖來源矩形 | 場域輸出矩形 |
| --- | --- | --- | --- |
| 0 | Wall Left | (100,320,320,100) | (0,640,640,2048) |
| 1 | Wall Button | (420,100,100,220) | (640,2688,1408,640) |
| 2 | Floor | (100,100,320,220) | (640,640,1408,2048) |
| 3 | Wall Top | (0,100,100,220) | (640,0,1408,640) |
| 4 | Wall Right Big | (100,0,200,100) | (2048,640,640,1280) |
| 5 | Entrance | (300,0,80,100) | (2048,1920,640,512) |
| 6 | Wall Right little | (380,0,40,100) | (2048,2432,640,256) |

- JSON 真實名稱為 **Wall Button**（face 1）；使用者稱 Wall Bottom。保留服務原名／index，顯示名稱可另外對照。
- 編輯圖上方的黑色缺口對應 Entrance；依使用者追加要求固定且不可放任何物件／路徑，禁止互動。系統入口背景特效在相同位置繪製；修改底圖顏色不會解除禁止區域。
- 輸出 `y=3328..3839` 全寬保留黑色，是原圖未預留的部分。
- 用連續邊界座標描述，幾何關係為 `outputX=(420-editorY)*6.4`、`outputY=editorX*6.4`；實際像素取樣須使用像素中心與正確半開區間，不能直接套到最後一個像素索引而越界。
- 反向轉換必須先驗證 touch 位於有效面，不能把輸出底下 512px 映射成編輯格。
- `52×42` 編輯格與 `20×128` 等晶片格是不同解析度；經場域像素／面局部座標映射，不能直接拿 event.cell 當編輯格。
- 上述映射已套入正式 `Data/704_Output.asset` 與 GPU 合成；數學／像素檢查通過，現場路由仍待驗收。

## 既有 TouchService 工作樹

指定 checkout 的原始碼與 config 本輪未修改；測試服務使用獨立快照與設定。2026-10-06 完成前核對 HEAD=`088403e`，以下既有變更與接手時一致：

```text
 M NangangProjection/CLAUDE.md
 M TouchService/config.json
?? AGENTS.md
?? GEMINI.md
?? TouchService/start-704.cmd
```

舊 HANDOVER 的 219 測試是歷史記錄。本輪跑的是新原型檢查與隔離串接測試，未重跑服務全部測試。
