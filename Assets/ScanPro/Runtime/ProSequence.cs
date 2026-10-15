using System;
using System.Collections.Generic;
using System.Linq;

namespace ScanPro704
{
    public sealed class ProSequence
    {
        public sealed class Question
        {
            public string Id, Text;
            public int Answer;
            public double Countdown;
            public bool Completed, Expired;
        }
        readonly Dictionary<string, Question> questions = new Dictionary<string, Question>();
        readonly Dictionary<string, (int touch, double time)> pairPresses = new Dictionary<string, (int, double)>();
        readonly bool paired, countdown;
        public bool IsPaired => paired;
        public IReadOnlyDictionary<string, Question> Questions => questions;
        public IEnumerable<string> CompletedIds => questions.Values.Where(q => q.Completed).Select(q => q.Id);
        public bool Contains(string id) => questions.ContainsKey(id);

        public ProSequence(ProMechanic mechanic, IEnumerable<string> availableIds, int seed)
        {
            if (mechanic == ProMechanic.None) return;
            var rng = new Random(seed);
            var ids = availableIds.OrderBy(_ => rng.Next()).ToArray();
            paired = mechanic == ProMechanic.Pairs7 || mechanic == ProMechanic.MathPairs7 || mechanic == ProMechanic.RangePairs7;
            countdown = mechanic == ProMechanic.Countdown;
            var fifteen = mechanic == ProMechanic.Numbers15 || mechanic == ProMechanic.Math15 || mechanic == ProMechanic.Range15;
            var count = Math.Min(ids.Length, paired ? 14 : countdown ? 6 : fifteen ? 15 : 7);
            if (paired) count -= count % 2;
            var range = mechanic == ProMechanic.Range7 || mechanic == ProMechanic.Range15 || mechanic == ProMechanic.RangePairs7;
            var math = mechanic == ProMechanic.Math7 || mechanic == ProMechanic.Math15 || mechanic == ProMechanic.MathPairs7;
            var values = (range ? Enumerable.Range(20, 80).OrderBy(_ => rng.Next()).Take(paired ? count / 2 : count) : Enumerable.Range(1, paired ? count / 2 : count)).OrderBy(v => v).ToArray();
            for (var i = 0; i < count; i++)
            {
                var answer = countdown ? 0 : values[paired ? i / 2 : i];
                questions.Add(ids[i], new Question { Id = ids[i], Answer = answer,
                    Text = countdown ? "" : math ? Expression(answer, rng) : answer.ToString(), Countdown = countdown ? (rng.Next(3) + 1) * 10 : 0 });
            }
        }

        public ProSequence(IEnumerable<LevelItem> orderedTargets)
        {
            foreach (var item in orderedTargets)
            {
                if (item == null || item.kind != TileKind.TouchBlue) continue;
                if (!TouchNumberContent.TryGetAnswer(item, out var answer)) continue;
                questions[item.id] = new Question { Id = item.id, Answer = answer, Text = TouchNumberContent.DisplayText(item) };
            }
        }

        static string Expression(int answer, Random rng)
        {
            // For solutions 1..15, two-digit addition/multiplication cannot fit.
            // Valid two-digit subtraction and exact division preserve the requested range.
            if (rng.Next(2) == 0)
            {
                var b = rng.Next(10, 100 - answer);
                return (b + answer) + "-" + b;
            }
            var limit = 99 / answer;
            if (limit >= 10) { var b = rng.Next(10, limit + 1); return (b * answer) + "/" + b; }
            return (answer + 20) + "-20";
        }

        public IEnumerable<string> Press(string id, int touch, double seconds)
        {
            if (!questions.TryGetValue(id, out var q) || q.Completed) return Array.Empty<string>();
            if (countdown) { q.Completed = true; return new[] { id }; }
            var next = questions.Values.Where(v => !v.Completed).Min(v => v.Answer);
            if (q.Answer != next) return Array.Empty<string>();
            if (!paired) { q.Completed = true; return new[] { id }; }
            pairPresses[id] = (touch, seconds);
            foreach (var other in questions.Values.Where(v => v.Id != id && !v.Completed && v.Answer == q.Answer))
                if (pairPresses.TryGetValue(other.Id, out var press) && press.touch != touch && Math.Abs(seconds - press.time) <= .20)
                { q.Completed = true; other.Completed = true; return new[] { id, other.Id }; }
            return Array.Empty<string>();
        }
        public void ReconcileContacts(IEnumerable<(string id, int touch)> activeContacts)
        {
            if (!paired) return;
            var active = new HashSet<(string, int)>(activeContacts);
            foreach (var id in pairPresses.Keys.Where(id => !active.Contains((id, pairPresses[id].touch))).ToArray())
                pairPresses.Remove(id);
        }
        public IEnumerable<string> Tick(double seconds)
        {
            var expired = new List<string>();
            if (countdown) foreach (var q in questions.Values)
                if (!q.Completed && !q.Expired && seconds >= q.Countdown) { q.Expired = true; expired.Add(q.Id); }
            return expired;
        }
        public string Label(string id, double seconds) => questions.TryGetValue(id, out var q) ? (countdown ? Math.Max(0, (int)Math.Ceiling(q.Countdown - seconds)).ToString() : q.Text) : null;
    }
}
