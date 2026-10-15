using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    internal static class EditorConsoleUtilityVerification
    {
        [MenuItem("704/驗證/工作台清空 Console")]
        public static void Run()
        {
            var entriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var getCount = entriesType?.GetMethod("GetCount", flags, null, Type.EmptyTypes, null);
            if (getCount == null)
                throw new InvalidOperationException("找不到 Unity Console 訊息數量查詢入口，無法驗證清除結果。");
            Debug.Log("SCANPRO_CONSOLE_CLEAR_TEST_MARKER");
            if ((int)getCount.Invoke(null, null) < 1)
                throw new InvalidOperationException("測試訊息未進入 Unity Console。");
            if (!EditorConsoleUtility.TryClear(out var error, out var remainingCount))
                throw new InvalidOperationException("清空 Unity Console 失敗：" + error);
            if ((int)getCount.Invoke(null, null) != 0 || remainingCount != 0)
                throw new InvalidOperationException("呼叫清除入口後，Unity Console 仍有訊息。");

            Debug.Log("SCANPRO_CONSOLE_CLEAR_OK");

            var workbench = ScriptableObject.CreateInstance<ScanProWorkbench>();
            try
            {
                Debug.Log("SCANPRO_CONSOLE_BUTTON_TEST_MARKER");
                if ((int)getCount.Invoke(null, null) < 1)
                    throw new InvalidOperationException("工作台按鈕測試訊息未進入 Unity Console。");
                typeof(ScanProWorkbench).GetMethod("ClearConsoleFromWorkbench", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(workbench, null);
                if ((int)getCount.Invoke(null, null) != 0)
                    throw new InvalidOperationException("工作台清空按鈕 callback 未清除 Unity Console。");
                var feedback = (string)typeof(ScanProWorkbench).GetField("consoleClearFeedback", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(workbench);
                if (feedback != "已清空")
                    throw new InvalidOperationException("工作台清空按鈕未正確回報清除結果。");
            }
            finally { UnityEngine.Object.DestroyImmediate(workbench); }
            Debug.Log("SCANPRO_CONSOLE_BUTTON_OK");
        }
    }
}
