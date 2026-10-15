using UnityEngine;

namespace ScanPro704
{
    public enum LevelOutcome { Success, HeartsDepleted, Timeout }
    public readonly struct TransitionSpec
    {
        public readonly Color Color;
        public readonly bool Expands;
        public readonly Vector2 Origin;
        public TransitionSpec(Color color, bool expands, Vector2 origin)
        { Color = color; Expands = expands; Origin = origin; }
    }
    public static class TransitionRules
    {
        // Success uses the last completed target's position, including scan targets.
        public static TransitionSpec For(LevelOutcome outcome, Vector2 lastTargetPosition)
        {
            return new TransitionSpec(outcome == LevelOutcome.Success ? GameColorSettings.SuccessColor : TilePalette.ColorOf(TileKind.HazardRed),
                outcome != LevelOutcome.Timeout, lastTargetPosition);
        }
    }
}
