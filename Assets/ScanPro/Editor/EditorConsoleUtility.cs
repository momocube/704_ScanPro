using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    internal static class EditorConsoleUtility
    {
        static readonly MethodInfo ClearMethod = typeof(EditorWindow).Assembly
            .GetType("UnityEditor.LogEntries")?
            .GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly MethodInfo GetCountMethod = typeof(EditorWindow).Assembly
            .GetType("UnityEditor.LogEntries")?
            .GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
        static readonly Type ConsoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");

        public static bool TryClear(out string error, out int remainingCount)
        {
            error = null;
            remainingCount = -1;
            if (ClearMethod == null)
            {
                error = "此 Unity 版本沒有可用的 Console 清除入口。";
                return false;
            }
            try
            {
                ClearMethod.Invoke(null, null);
                RefreshConsoleWindows();
                if (GetCountMethod != null) remainingCount = (int)GetCountMethod.Invoke(null, null);
                return true;
            }
            catch (Exception exception)
            {
                error = exception is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException.Message
                    : exception.Message;
                return false;
            }
        }

        static void RefreshConsoleWindows()
        {
            if (ConsoleWindowType == null) return;
            foreach (var window in Resources.FindObjectsOfTypeAll(ConsoleWindowType))
            {
                try { if (window is EditorWindow editorWindow) editorWindow.Repaint(); }
                catch { /* The clear operation remains successful if a window is closing. */ }
            }
        }
    }
}
