using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ScanPro704
{
    /// <summary>Formats the compact status shown in the fixed venue scoreboard region.</summary>
    public static class VenueScoreboardHud
    {
        public const int LevelsPerGroup = 5;
        static readonly Regex PackageIdentifier = new Regex(
            @"^(?<identifier>(?:[1-9]|10)-[1-5])(?=\s|\(|$)", RegexOptions.Compiled);

        public static string ProgressLabel(ScanLevel level)
        {
            if (level == null) return "0-0";
            if (TryPackageIdentifier(level.displayName, out var identifier) ||
                TryPackageIdentifier(level.importedPackageName, out identifier)) return identifier;
            return level.LevelIdentifier;
        }

        static bool TryPackageIdentifier(string packageName, out string identifier)
        {
            identifier = null;
            if (string.IsNullOrWhiteSpace(packageName)) return false;
            var fileName = Path.GetFileNameWithoutExtension(packageName.Trim());
            var match = PackageIdentifier.Match(fileName);
            if (!match.Success) return false;
            identifier = match.Groups["identifier"].Value;
            return true;
        }

        public static int RemainingWholeSeconds(int timeLimitSeconds, double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)) return 0;
            var remaining = Math.Max(0d, Math.Max(0, timeLimitSeconds) - Math.Max(0d, elapsedSeconds));
            return remaining >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(remaining);
        }

        public static string CountdownLabel(int timeLimitSeconds, double elapsedSeconds) =>
            RemainingWholeSeconds(timeLimitSeconds, elapsedSeconds).ToString("00") + "s";
    }
}
