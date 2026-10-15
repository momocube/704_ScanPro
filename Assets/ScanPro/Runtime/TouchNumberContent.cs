using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ScanPro704
{
    public static class TouchNumberContent
    {
        public static string DisplayText(LevelItem item)
        {
            if (item == null) return string.Empty;
            return item.useGeneratedContent ? item.generatedText ?? string.Empty : item.label ?? string.Empty;
        }

        public static bool TryGetAnswer(LevelItem item, out int answer)
        {
            answer = 0;
            if (item == null || item.kind != TileKind.TouchBlue) return false;
            if (item.useGeneratedContent) { answer = item.generatedAnswer; return true; }
            return TryEvaluate(item.label, out answer);
        }

        public static bool TryEvaluate(string expression, out int answer)
        {
            answer = 0;
            if (string.IsNullOrWhiteSpace(expression)) return false;
            var text = expression.Replace(" ", string.Empty).Replace("−", "-");
            var operation = -1;
            for (var i = 1; i < text.Length; i++)
                if (text[i] == '+' || text[i] == '-') { if (operation >= 0) return false; operation = i; }
            if (operation < 0)
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out answer);
            if (!int.TryParse(text.Substring(0, operation), NumberStyles.None, CultureInfo.InvariantCulture, out var left) ||
                !int.TryParse(text.Substring(operation + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var right))
                return false;
            try
            {
                answer = text[operation] == '+' ? checked(left + right) : checked(left - right);
                return answer >= 0;
            }
            catch (OverflowException) { answer = 0; return false; }
        }

        public static bool TryMakeArithmetic(int answer, int operandMinimum, int operandMaximum, Vector2Int size,
            System.Random random, out string expression, TileShape shape = TileShape.Rectangle)
        {
            expression = null;
            if (random == null || operandMinimum < 0 || operandMaximum > 9999 || operandMaximum < operandMinimum ||
                operandMaximum - (long)operandMinimum > 1000) return false;
            var candidates = new List<string>();
            for (var left = operandMinimum; left <= operandMaximum; left++)
            {
                var addend = answer - (long)left;
                if (addend >= operandMinimum && addend <= operandMaximum)
                {
                    var candidate = left + "+" + (int)addend;
                    if (FitsCellFootprint(candidate, size, shape)) candidates.Add(candidate);
                }
                var subtrahend = left - (long)answer;
                if (subtrahend >= operandMinimum && subtrahend <= operandMaximum)
                {
                    var candidate = left + "-" + (int)subtrahend;
                    if (FitsCellFootprint(candidate, size, shape)) candidates.Add(candidate);
                }
            }
            if (candidates.Count == 0) return false;
            expression = candidates[random.Next(candidates.Count)];
            return true;
        }

        public static bool TryAssignGenerated(ScanLevel level, LevelItem item, IList<int> additionallyUsed, out string error)
        {
            error = null;
            if (level == null || item == null || item.kind != TileKind.TouchBlue)
            { error = "自動數字只適用於藍色觸摸格。"; return false; }
            if (level.autoAnswerMinimum < 0 || level.autoAnswerMaximum > 9999 ||
                level.autoAnswerMaximum < level.autoAnswerMinimum)
            { error = "答案範圍無效；答案必須介於 0～9999。"; return false; }

            var used = new HashSet<int>(additionallyUsed ?? Array.Empty<int>());
            foreach (var other in level.items)
            {
                if (other == item || other.kind != TileKind.TouchBlue) continue;
                if (TryGetAnswer(other, out var existing)) used.Add(existing);
            }

            var candidates = new List<int>();
            for (var value = level.autoAnswerMinimum; value <= level.autoAnswerMaximum; value++)
                if (!used.Contains(value)) candidates.Add(value);
            if (level.autoNumberOrder == AutoNumberOrder.Random)
                for (var i = candidates.Count - 1; i > 0; i--)
                {
                    var j = UnityEngine.Random.Range(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
            if (candidates.Count == 0) { error = "此範圍的答案已用完，請擴大範圍或切回手動模式。"; return false; }

            var random = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));
            foreach (var answer in candidates)
            {
                var text = answer.ToString(CultureInfo.InvariantCulture);
                if (level.autoTouchStyle == AutoTouchContentStyle.Arithmetic &&
                    !TryMakeArithmetic(answer, level.arithmeticOperandMinimum, level.arithmeticOperandMaximum,
                        ContentFootprint(item), random, out text, item.HasAnimation ? TileShape.Rectangle : item.shape)) continue;
                if (!FitsItemContent(text, item)) continue;

                item.useGeneratedContent = true;
                item.generatedAnswer = answer;
                item.generatedText = text;
                return true;
            }
            error = level.autoTouchStyle == AutoTouchContentStyle.Arithmetic
                ? "此答案範圍內找不到符合運算元範圍且能放入藍格的加減算式。"
                : "此答案範圍內沒有能放入藍格的數字，請放大藍格或縮小範圍。";
            return false;
        }

        public static Vector2Int ContentFootprint(LevelItem item)
        {
            if (!item.HasAnimation) return item.size;
            var size = new Vector2Int(int.MaxValue, int.MaxValue);
            var visible = false;
            for (var i = 0; i < item.animationFrames.Count; i++)
                if (item.TryGetContentBounds(item.position, i, out var bounds))
                {
                    visible = true;
                    size.x = Math.Min(size.x, (int)bounds.width);
                    size.y = Math.Min(size.y, (int)bounds.height);
                }
            return visible ? size : Vector2Int.zero;
        }

        public static bool FitsItemContent(string text, LevelItem item) =>
            FitsCellFootprint(text, ContentFootprint(item), item.HasAnimation ? TileShape.Rectangle : item.shape,
                item.kind == TileKind.TouchBlue);

        public static bool FitsCellFootprint(string text, Vector2Int size, TileShape shape = TileShape.Rectangle,
            bool underlineOrientationDigits = true)
        {
            if (string.IsNullOrEmpty(text)) return true;
            var width = Math.Max(1, size.x) * 10;
            var height = Math.Max(1, size.y) * 10;
            if (shape == TileShape.Circle)
            {
                var inscribed = (int)Math.Floor(Math.Min(width, height) * TileShapeUtility.InscribedSquareFactor);
                width = height = inscribed;
            }
            var required = GameCanvasRenderer.MeasureTextGrid(text, underlineOrientationDigits);
            return required.x > 0 && width >= required.x && height >= required.y;
        }
    }
}
