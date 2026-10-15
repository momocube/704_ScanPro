using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704
{
    public static class LevelValidation
    {
        public static IReadOnlyList<string> Errors(ScanLevel level)
        {
            var errors = new List<string>();
            if (level == null || level.venue == null) { errors.Add("缺少關卡／場域資料。"); return errors; }
            if (level.items.Any(i => string.IsNullOrWhiteSpace(i.id)) || level.items.Select(i => i.id).Distinct().Count() != level.items.Count)
                errors.Add("物件 ID 不可空白或重複。");
            if (!level.items.Any(GameSession.IsObjective)) errors.Add("關卡至少需要一個藍色目標。");
            if (level.proMechanic != ProMechanic.None &&
                (level.newTouchContentMode == TouchContentMode.Automatic || level.items.Any(i => i.useGeneratedContent)))
                errors.Add("自動觸摸數字與算式需將舊版 Pro 機制設為「無」，才能保持指定答案及顯示內容。");
            foreach (var item in level.items)
            {
                var name = TilePalette.Names[(int)item.kind] + $" ({item.position.x},{item.position.y})";
                if (item.shape != TileShape.Rectangle && !TileShapeUtility.HasSquareBounds(item))
                    errors.Add(name + "：方形與圓形物件需使用相同寬高。請在工作台調整邊長／直徑。");
                if (level.venue.TouchesEntrance(item)) errors.Add(name + "：入口位置固定，禁止放置任何物件或穿越入口的路徑。");
                if (level.venue.TouchesScoreboard(item)) errors.Add(name + "：東小牆整面為記分板保留區，禁止放置任何物件或穿越路徑。");
                if (item.kind == TileKind.SafeGreen && !item.allowSafeGreenOutsideFloor && !level.venue.PathIsOnFloor(item))
                    errors.Add(name + "：安全區只能放在地板（Floor／face 2），完整移動路徑也必須留在地板範圍內。");
                if (!level.venue.PathIsAllowed(item)) errors.Add(name + "：位置或完整循環路徑穿過黑區、入口或記分板保留區。");
                errors.AddRange(ContentErrors(item));
                if (item.kind == TileKind.TouchBlue)
                {
                    var label = TouchNumberContent.DisplayText(item);
                    if (item.useGeneratedContent && !TouchNumberContent.TryGetAnswer(item, out var generatedAnswer))
                        errors.Add(name + "：自動數字缺少有效答案。");
                }
            }
            return errors;
        }

        public static IReadOnlyList<string> ContentErrors(LevelItem item)
        {
            var errors = new List<string>();
            if (item == null || (!item.IsScan && item.kind != TileKind.TouchBlue)) return errors;
            var name = TilePalette.Names[(int)item.kind] + $" ({item.position.x},{item.position.y})";
            var count = item.HasAnimation ? item.animationFrames.Count : 1;
            for (var index = 0; index < count; index++)
            {
                if (item.IsScan)
                {
                    if (!item.TryGetQrContentBounds(item.position, index, out var qrBounds)) continue;
                    var qrSize = item.HasAnimation
                        ? new UnityEngine.Vector2Int(Mathf.RoundToInt(qrBounds.width), Mathf.RoundToInt(qrBounds.height))
                        : item.size;
                    if (!TileShapeUtility.HasMinimumScanBounds(qrSize))
                    {
                        var qrFrameLabel = item.HasAnimation ? $" 第 {index + 1} 影格" : "";
                        errors.Add(name + qrFrameLabel + $"：QR 區域至少 {TileShapeUtility.MinimumScanSizeCells}×{TileShapeUtility.MinimumScanSizeCells} 格；圓形依外框尺寸計算。");
                        break;
                    }
                }
                if (!item.TryGetContentBounds(item.position, index, out var rect)) continue;
                var shape = item.HasAnimation ? TileShape.Rectangle : item.shape;
                var size = new UnityEngine.Vector2Int((int)rect.width, (int)rect.height);
                var contentFrameLabel = item.HasAnimation ? $" 第 {index + 1} 影格有效區域 {size.x}×{size.y} 格" : "";
                if (item.kind == TileKind.TouchBlue && !TouchNumberContent.FitsCellFootprint(TouchNumberContent.DisplayText(item), size, shape))
                {
                    errors.Add(name + contentFrameLabel + "：數字／算式太長，請加大藍格，或縮短答案／算式。");
                    break;
                }
            }
            return errors;
        }
    }
}
