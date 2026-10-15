using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    /// <summary>Geometry and preview rendering for drag-to-create rectangle, square, and circle tools.</summary>
    internal static class WorkbenchShapeTool
    {
        public static (Vector2Int position, Vector2Int size) DragBounds(Vector2Int start, Vector2Int end, TileShape shape)
        {
            var x = Mathf.Min(start.x, end.x);
            var y = Mathf.Min(start.y, end.y);
            var width = Mathf.Abs(end.x - start.x) + 1;
            var height = Mathf.Abs(end.y - start.y) + 1;
            if (shape != TileShape.Rectangle) width = height = Mathf.Max(width, height);
            return (new Vector2Int(x, y), new Vector2Int(width, height));
        }

        public static void DrawGhost(Rect canvas, float scale, Vector2Int position, Vector2Int size, TileShape shape, bool valid)
        {
            var rect = new Rect(canvas.x + position.x * scale, canvas.y + position.y * scale, size.x * scale, size.y * scale);
            Handles.BeginGUI();
            Handles.color = valid ? new Color(.25f, 1f, .4f, .9f) : new Color(1f, .25f, .2f, .95f);
            if (shape == TileShape.Circle)
            {
                var radius = Mathf.Min(rect.width, rect.height) * .5f;
                Handles.color = valid ? new Color(.2f, 1f, .35f, .18f) : new Color(1f, .15f, .15f, .24f);
                Handles.DrawSolidDisc(rect.center, Vector3.forward, radius);
                Handles.color = valid ? new Color(.25f, 1f, .4f, .9f) : new Color(1f, .25f, .2f, .95f);
                Handles.DrawWireDisc(rect.center, Vector3.forward, radius);
            }
            else
            {
                EditorGUI.DrawRect(rect, valid ? new Color(.2f, 1f, .35f, .18f) : new Color(1f, .15f, .15f, .24f));
                Handles.DrawAAPolyLine(2, new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y),
                    new Vector3(rect.xMax, rect.yMax), new Vector3(rect.x, rect.yMax), new Vector3(rect.x, rect.y));
            }
            Handles.EndGUI();
        }
    }
}
