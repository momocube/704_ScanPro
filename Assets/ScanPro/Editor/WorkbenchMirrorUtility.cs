using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704.Editor
{
    /// <summary>Shared mirror math for brush placement, movement paths, and mirrored deletion.</summary>
    internal static class WorkbenchMirrorUtility
    {
        public static List<LevelItem> Variants(LevelItem source, bool mirrorX, bool mirrorY, float axisX, float axisY)
        {
            var variants = new List<LevelItem>();
            if (source == null) return variants;
            if (mirrorX) variants.Add(Reflect(source, true, false, axisX, axisY));
            if (mirrorY) variants.Add(Reflect(source, false, true, axisX, axisY));
            if (mirrorX && mirrorY) variants.Add(Reflect(source, true, true, axisX, axisY));
            return variants.GroupBy(item => item.position).Select(group => group.First())
                .Where(item => item.position != source.position).ToList();
        }

        public static int FindMirrorIndex(IReadOnlyList<LevelItem> items, int sourceIndex, float axisX)
        {
            if (items == null || sourceIndex < 0 || sourceIndex >= items.Count) return -1;
            var source = items[sourceIndex];
            var reflected = ReflectPosition(source.position, source.size, true, false, axisX, 0);
            if (reflected == source.position) return -1;
            for (var index = items.Count - 1; index >= 0; index--)
            {
                var candidate = items[index];
                if (index != sourceIndex && candidate.kind == source.kind && candidate.position == reflected &&
                    candidate.size == source.size && candidate.shape == source.shape) return index;
            }
            return -1;
        }

        static LevelItem Reflect(LevelItem source, bool reflectX, bool reflectY, float axisX, float axisY)
        {
            var copy = new LevelItem
            {
                id = Guid.NewGuid().ToString("N"), kind = source.kind, shape = source.shape,
                position = ReflectPosition(source.position, source.size, reflectX, reflectY, axisX, axisY),
                size = source.size, label = source.label, useGeneratedContent = source.useGeneratedContent,
                generatedAnswer = source.generatedAnswer, generatedText = source.generatedText,
                group = source.group, proTarget = source.proTarget, startHoldSeconds = source.startHoldSeconds
            };
            if (source.targetGroups != null) copy.targetGroups.AddRange(source.targetGroups);
            if (source.waypoints != null)
                copy.waypoints.AddRange(source.waypoints.Select(point => ReflectPosition(point, source.size, reflectX, reflectY, axisX, axisY)));
            if (source.waypointHoldSeconds != null) copy.waypointHoldSeconds.AddRange(source.waypointHoldSeconds);
            return copy;
        }

        static Vector2Int ReflectPosition(Vector2Int position, Vector2Int size, bool reflectX, bool reflectY, float axisX, float axisY)
        {
            if (reflectX) position.x = Mathf.RoundToInt(2f * axisX - position.x - size.x);
            if (reflectY) position.y = Mathf.RoundToInt(2f * axisY - position.y - size.y);
            return position;
        }
    }
}
