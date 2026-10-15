using System;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    internal static class PathTimingEditor
    {
        public static void Draw(ScanLevel level, SerializedObject serialized, SerializedProperty itemProperty, LevelItem item)
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("路徑點與停留", EditorStyles.boldLabel);
            var fpsProperty = serialized.FindProperty("timingFramesPerSecond");
            var fps = Mathf.Max(1, fpsProperty == null ? 60 : fpsProperty.intValue);
            var points = itemProperty.FindPropertyRelative("waypoints");
            var holds = itemProperty.FindPropertyRelative("waypointHoldSeconds");
            var startHold = itemProperty.FindPropertyRelative("startHoldSeconds");
            if (points == null || holds == null || startHold == null) return;

            DrawDurationRow("起點停留", startHold, fps);
            EditorGUILayout.LabelField($"時間可輸入小數；每段移動 {level.movementSegmentMs} ms，換算基準 {fps} FPS。", EditorStyles.wordWrappedMiniLabel);

            while (holds.arraySize < points.arraySize)
            {
                holds.InsertArrayElementAtIndex(holds.arraySize);
                holds.GetArrayElementAtIndex(holds.arraySize - 1).floatValue = 0;
            }
            while (holds.arraySize > points.arraySize) holds.DeleteArrayElementAtIndex(holds.arraySize - 1);

            for (var i = 0; i < points.arraySize; i++)
            {
                var point = points.GetArrayElementAtIndex(i);
                var hold = holds.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    point.vector2IntValue = EditorGUILayout.Vector2IntField($"路徑點 {i + 1}", point.vector2IntValue);
                    if (GUILayout.Button("刪除", GUILayout.Width(44)))
                    {
                        points.DeleteArrayElementAtIndex(i);
                        holds.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
                DrawDurationRow($"點 {i + 1} 停留", hold, fps);
            }
        }

        static void DrawDurationRow(string label, SerializedProperty secondsProperty, int fps)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(82));
                var currentSeconds = Mathf.Max(0, secondsProperty.floatValue);
                var frames = FramesForSeconds(currentSeconds, fps);
                var editedSeconds = EditorGUILayout.FloatField(currentSeconds, GUILayout.Width(58));
                GUILayout.Label("秒", GUILayout.Width(20));
                var editedFrames = EditorGUILayout.IntField(frames, GUILayout.Width(52));
                GUILayout.Label("幀", GUILayout.Width(20));
                if (!Mathf.Approximately(editedSeconds, currentSeconds))
                    secondsProperty.floatValue = Mathf.Max(0, editedSeconds);
                else if (editedFrames != frames)
                    secondsProperty.floatValue = SecondsForFrames(editedFrames, fps);
            }
        }

        public static int FramesForSeconds(float seconds, int fps) => Mathf.RoundToInt(Mathf.Max(0, seconds) * Mathf.Max(1, fps));
        public static float SecondsForFrames(int frames, int fps) => Mathf.Max(0, frames) / (float)Mathf.Max(1, fps);
    }
}
