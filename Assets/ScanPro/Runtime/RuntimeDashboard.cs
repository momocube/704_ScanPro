using System;
using UnityEngine;

namespace ScanPro704
{
    /// <summary>Operator controls shown in the resizable primary-display Player window.</summary>
    public sealed class RuntimeDashboard : MonoBehaviour
    {
        GameSession session;
        ScanLevel[] levels;
        Func<int> currentLevel;
        Action<int> startLevel;
        Action showLockGrid;
        Action locksChanged;
        Func<string> touchStatus, scannerStatus;
        string message;
        GUIStyle titleStyle, bodyStyle, smallStyle;

        public void Bind(GameSession game, ScanLevel[] levelSet, Func<int> current, Action<int> restart,
            Func<string> touch, Func<string> scanner, Action openLockGrid, Action onLocksChanged)
        {
            session = game;
            levels = levelSet ?? Array.Empty<ScanLevel>();
            currentLevel = current;
            startLevel = restart;
            touchStatus = touch;
            scannerStatus = scanner;
            showLockGrid = openLockGrid;
            locksChanged = onLocksChanged;
        }

        public void SetMessage(string value) { message = value; }

        void OnGUI()
        {
            if (session == null || session.Level == null || session.Controls == null) return;
            EnsureStyles();
            var scale = Mathf.Clamp(Mathf.Min(Screen.width / 920f, Screen.height / 780f), .78f, 1.25f);
            GUI.skin.label.fontSize = Mathf.RoundToInt(16 * scale);
            GUI.skin.button.fontSize = Mathf.RoundToInt(15 * scale);
            GUI.color = Color.white;

            var padding = 22 * scale;
            var contentWidth = Mathf.Min(Screen.width - padding * 2, 720 * scale);
            var left = (Screen.width - contentWidth) * .5f;
            var content = new Rect(left, padding, contentWidth, Screen.height - padding * 2);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
            GUI.Label(new Rect(content.x, content.y, content.width, 38 * scale),
                $"704 掃碼 Pro｜{session.Level.DisplayTitle}", titleStyle);

            var y = content.y + 48 * scale;
            var rowHeight = 42 * scale;
            GUI.Box(new Rect(content.x, y, content.width, 112 * scale), GUIContent.none);
            GUI.Label(new Rect(content.x + 14 * scale, y + 9 * scale, content.width - 28 * scale, 28 * scale),
                $"心數　{Math.Max(0, session.Hearts)}　　剩餘目標　{session.Remaining}", bodyStyle);
            GUI.Label(new Rect(content.x + 14 * scale, y + 43 * scale, content.width - 28 * scale, 26 * scale),
                $"剩餘時間　{Math.Max(0, session.Level.timeLimitSeconds - session.Elapsed):0.0} 秒" +
                (session.Outcome.HasValue ? "　｜　" + OutcomeLabel(session.Outcome.Value) : string.Empty), bodyStyle);
            y += 124 * scale;

            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = session.Controls.Invulnerable
                ? new Color(.23f, .68f, .39f)
                : new Color(.25f, .3f, .38f);
            if (GUI.Button(new Rect(content.x, y, content.width, rowHeight),
                session.Controls.Invulnerable ? "無敵模式：開啟（不扣心）" : "無敵模式：關閉"))
            {
                session.Controls.Invulnerable = !session.Controls.Invulnerable;
                message = session.Controls.Invulnerable ? "無敵模式已開啟；扣心來源不會減少心數。" : "無敵模式已關閉。";
            }
            GUI.backgroundColor = oldColor;
            y += rowHeight + 10 * scale;

            var current = currentLevel == null ? 0 : currentLevel();
            var halfGap = 10 * scale;
            var halfWidth = (content.width - halfGap) * .5f;
            if (GUI.Button(new Rect(content.x, y, halfWidth, rowHeight), "重新開始本關"))
                startLevel?.Invoke(current);
            if (GUI.Button(new Rect(content.x + halfWidth + halfGap, y, halfWidth, rowHeight), "下一關"))
                startLevel?.Invoke(levels.Length == 0 ? current : (current + 1) % levels.Length);
            y += rowHeight + 16 * scale;

            GUI.Label(new Rect(content.x, y, content.width, 28 * scale), "觸碰鎖格", bodyStyle);
            y += 30 * scale;
            GUI.Label(new Rect(content.x, y, content.width, 48 * scale),
                $"鎖格視窗目前標示 {session.Controls.LockedCellCount} 格。點擊格子即可鎖定或解鎖；鎖定會立即停用該位置的觸碰。", smallStyle);
            y += 52 * scale;
            if (GUI.Button(new Rect(content.x, y, content.width, rowHeight), "開啟／顯示獨立鎖格視窗"))
            {
                showLockGrid?.Invoke();
                message = "鎖格視窗已顯示，可獨立移動及縮放。";
            }
            y += rowHeight + 8 * scale;
            if (GUI.Button(new Rect(content.x, y, content.width, 36 * scale),
                $"清除全部鎖格（{session.Controls.LockedCellCount}）"))
            {
                session.Controls.ClearLocks();
                message = "已清除鎖格；原本持續按住的觸點須先放開。";
                locksChanged?.Invoke();
            }
            y += 44 * scale;

            var status = string.IsNullOrEmpty(message) ? "無敵與鎖格只在本次程式執行期間保留。" : message;
            GUI.Label(new Rect(content.x, y, content.width, 38 * scale), status, smallStyle);
            y += 42 * scale;
            GUI.Label(new Rect(content.x, y, content.width, 48 * scale), "TouchService：" + SafeStatus(touchStatus), smallStyle);
            y += 48 * scale;
            GUI.Label(new Rect(content.x, y, content.width, 48 * scale), "掃碼機：" + SafeStatus(scannerStatus), smallStyle);
        }

        static string OutcomeLabel(LevelOutcome outcome)
        {
            switch (outcome)
            {
                case LevelOutcome.Success: return "本關完成";
                case LevelOutcome.Timeout: return "時間到";
                default: return "心數耗盡";
            }
        }

        static string SafeStatus(Func<string> provider)
        {
            try { return provider == null ? "未啟動" : provider(); }
            catch { return "狀態讀取中"; }
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        }
    }
}
