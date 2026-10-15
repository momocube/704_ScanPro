using UnityEngine;

namespace ScanPro704
{
    // One Resources asset supplies authoring, exports and built players alike.
    public sealed class GameColorSettings : ScriptableObject
    {
        public const string ResourceName = "704_GameColors";
        static GameColorSettings shared;
        public static GameColorSettings Shared => shared != null ? shared :
            (shared = Resources.Load<GameColorSettings>(ResourceName));

        [Range(0, 1), Tooltip("所有關卡紅色區域的飽和度倍率；1 保留原色，0 為灰階。不影響失敗過渡。")]
        public float hazardRedSaturation = 1;
        [Range(0, 1), Tooltip("只調整過關藍色過渡；不影響關卡內藍格、QR 或入口。")]
        public float successBlueSaturation = 1;

        public static Color HazardColor => WithSaturation(TilePalette.ColorOf(TileKind.HazardRed),
            Shared == null ? 1 : Shared.hazardRedSaturation);
        public static Color SuccessColor => WithSaturation(TilePalette.ColorOf(TileKind.TouchBlue),
            Shared == null ? 1 : Shared.successBlueSaturation);

        public static Color WithSaturation(Color color, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 1;
            multiplier = Mathf.Clamp01(multiplier);
            if (multiplier == 1) return color;
            Color.RGBToHSV(color, out var hue, out var saturation, out var value);
            var result = Color.HSVToRGB(hue, saturation * multiplier, value);
            result.a = color.a;
            return result;
        }
    }
}
