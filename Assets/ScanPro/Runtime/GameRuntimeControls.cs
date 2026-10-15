using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704
{
    /// <summary>Per-run controls exposed by the packaged runtime dashboard.</summary>
    public sealed class GameRuntimeControls
    {
        readonly HashSet<int> lockedCells = new HashSet<int>();
        readonly HashSet<int> contactsSuppressedUntilLift = new HashSet<int>();
        int boardWidth = 52;
        int boardHeight = 42;

        public bool Invulnerable { get; set; }
        public int LockedCellCount => lockedCells.Count;
        public int BoardWidth => boardWidth;
        public int BoardHeight => boardHeight;

        public void ConfigureBoard(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (width == boardWidth && height == boardHeight) return;
            var oldWidth = boardWidth;
            var remapped = lockedCells.Select(key => new Vector2Int(key % oldWidth, key / oldWidth))
                .Where(cell => cell.x < width && cell.y < height)
                .Select(cell => cell.y * width + cell.x).ToArray();
            lockedCells.Clear();
            foreach (var key in remapped) lockedCells.Add(key);
            boardWidth = width;
            boardHeight = height;
        }

        public bool IsLocked(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < boardWidth && cell.y >= 0 && cell.y < boardHeight &&
                lockedCells.Contains(cell.y * boardWidth + cell.x);
        }

        public bool IsLocked(Vector2 cellPosition)
        {
            var cell = Vector2Int.FloorToInt(cellPosition);
            return IsLocked(cell);
        }

        public bool ToggleLock(Vector2Int cell)
        {
            if (cell.x < 0 || cell.x >= boardWidth || cell.y < 0 || cell.y >= boardHeight) return false;
            var key = cell.y * boardWidth + cell.x;
            if (lockedCells.Remove(key)) return false;
            lockedCells.Add(key);
            return true;
        }

        public bool SetLock(Vector2Int cell, bool locked)
        {
            if (cell.x < 0 || cell.x >= boardWidth || cell.y < 0 || cell.y >= boardHeight) return false;
            var key = cell.y * boardWidth + cell.x;
            return locked ? lockedCells.Add(key) : lockedCells.Remove(key);
        }

        public void ClearLocks() => lockedCells.Clear();

        public IEnumerable<Vector2Int> LockedCells()
        {
            foreach (var key in lockedCells) yield return new Vector2Int(key % boardWidth, key / boardWidth);
        }

        public IReadOnlyList<GameTouch> FilterTouches(IEnumerable<GameTouch> touches)
        {
            var current = touches == null ? new List<GameTouch>() : touches.ToList();
            var activeIds = new HashSet<int>(current.Select(t => t.Id));
            contactsSuppressedUntilLift.RemoveWhere(id => !activeIds.Contains(id));
            var accepted = new List<GameTouch>(current.Count);
            foreach (var touch in current)
            {
                if (IsLocked(touch.CellPosition))
                {
                    contactsSuppressedUntilLift.Add(touch.Id);
                    continue;
                }
                if (!contactsSuppressedUntilLift.Contains(touch.Id)) accepted.Add(touch);
            }
            return accepted;
        }
    }
}
