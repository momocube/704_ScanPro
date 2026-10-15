using System;
using System.Collections.Generic;
using System.Linq;

namespace ScanPro704
{
    public enum ScanDisposition { Accepted, Duplicate, UnknownCode, InvalidFrame, WrongRound }
    public sealed class ScanTarget
    {
        public string Id { get; }
        public bool Repeatable { get; }
        public ScanTarget(string id, bool repeatable) { Id = id; Repeatable = repeatable; }
    }

    public readonly struct ScanResult
    {
        public readonly ScanDisposition Disposition;
        public readonly string TargetId;
        public readonly string DevicePrefix;
        public ScanResult(ScanDisposition disposition, string targetId, string prefix)
        { Disposition = disposition; TargetId = targetId; DevicePrefix = prefix; }
    }

    // Accept complete device-tagged frames only. No assumptions about a scanner's
    // transport, delimiter, or actual prefix format are made in this layer.
    public sealed class ScanRegistry
    {
        readonly object gate = new object();
        readonly Random random;
        readonly int[] availableCodes = new int[10000];
        int remainingCodes = 10000;
        readonly Dictionary<string, ScanTarget> targets = new Dictionary<string, ScanTarget>();
        readonly HashSet<string> consumed = new HashSet<string>();
        readonly Dictionary<string, double> lastSeen = new Dictionary<string, double>();
        long round;
        public double RepeatCooldownMs { get; private set; }
        public long Round { get { lock (gate) return round; } }

        public ScanRegistry(int seed, double repeatCooldownMs = 500)
        {
            if (repeatCooldownMs < 0 || double.IsNaN(repeatCooldownMs) || double.IsInfinity(repeatCooldownMs))
                throw new ArgumentOutOfRangeException(nameof(repeatCooldownMs));
            random = new Random(seed);
            for (var i = 0; i < availableCodes.Length; i++) availableCodes[i] = i;
            RepeatCooldownMs = repeatCooldownMs;
        }
        public void ConfigureQuietInterval(double milliseconds)
        {
            if (milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            lock (gate) RepeatCooldownMs = milliseconds;
        }

        public Dictionary<string, string> BeginRound(IEnumerable<ScanTarget> nextTargets)
        {
            lock (gate)
            {
                var next = nextTargets.ToArray();
                if (next.Any(t => t == null || string.IsNullOrWhiteSpace(t.Id)) || next.Select(t => t.Id).Distinct().Count() != next.Length)
                    throw new ArgumentException("Scan target ids must be present and unique.");
                if (next.Length > remainingCodes) throw new InvalidOperationException("Four-digit code pool exhausted; explicitly start a new session.");
                targets.Clear(); consumed.Clear(); lastSeen.Clear(); round++;
                var result = new Dictionary<string, string>();
                foreach (var target in next)
                {
                    var index = random.Next(remainingCodes);
                    var code = availableCodes[index].ToString("D4");
                    availableCodes[index] = availableCodes[--remainingCodes];
                    targets.Add(code, target);
                    result.Add(target.Id, code);
                }
                return result;
            }
        }

        public ScanResult Submit(long frameRound, string devicePrefix, string fourDigitCode, double monotonicMilliseconds)
        {
            lock (gate)
            {
                if (frameRound != round) return new ScanResult(ScanDisposition.WrongRound, null, devicePrefix);
                if (string.IsNullOrWhiteSpace(devicePrefix) || fourDigitCode == null || fourDigitCode.Length != 4 ||
                    fourDigitCode.Any(c => c < '0' || c > '9') || double.IsNaN(monotonicMilliseconds) ||
                    double.IsInfinity(monotonicMilliseconds) || monotonicMilliseconds < 0)
                    return new ScanResult(ScanDisposition.InvalidFrame, null, devicePrefix);
                if (!targets.TryGetValue(fourDigitCode, out var target))
                    return new ScanResult(ScanDisposition.UnknownCode, null, devicePrefix);
                if (consumed.Contains(target.Id)) return new ScanResult(ScanDisposition.Duplicate, target.Id, devicePrefix);
                if (target.Repeatable)
                {
                    // Every observation extends the quiet interval. A held scanner
                    // must not toggle repeatedly once a fixed acceptance timer expires.
                    var seenBefore = lastSeen.TryGetValue(target.Id, out var last);
                    lastSeen[target.Id] = seenBefore ? Math.Max(last, monotonicMilliseconds) : monotonicMilliseconds;
                    if (seenBefore && monotonicMilliseconds - last < RepeatCooldownMs)
                        return new ScanResult(ScanDisposition.Duplicate, target.Id, devicePrefix);
                }
                else consumed.Add(target.Id);
                return new ScanResult(ScanDisposition.Accepted, target.Id, devicePrefix);
            }
        }
    }
}
