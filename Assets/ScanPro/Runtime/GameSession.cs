using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704
{
    public readonly struct GameTouch
    {
        public readonly int Id;
        public readonly Vector2 CellPosition;
        public GameTouch(int id, Vector2 position) { Id = id; CellPosition = position; }
    }
    public sealed class GameSession
    {
        public sealed class ItemState
        {
            public LevelItem Item;
            public bool Completed, Disabled;
            public bool PurpleHoldActive;
        }
        readonly ScanRegistry registry;
        readonly Dictionary<string, ItemState> states = new Dictionary<string, ItemState>();
        readonly Dictionary<string, double> holds = new Dictionary<string, double>();
        readonly Dictionary<string, int> switches = new Dictionary<string, int>();
        readonly HashSet<string> removedGroups = new HashSet<string>();
        readonly Dictionary<string, int> ascendingAnswers = new Dictionary<string, int>();
        HashSet<string> contacts = new HashSet<string>();
        ProSequence pro;
        public GameRuntimeControls Controls { get; } = new GameRuntimeControls();
        public ScanLevel Level { get; private set; }
        public IReadOnlyDictionary<string, ItemState> States => states;
        public Dictionary<string, string> Codes { get; private set; } = new Dictionary<string, string>();
        public double Elapsed { get; private set; }
        public int Hearts { get; private set; }
        public const int MaximumHearts = 15;
        public int CompletedCount => states.Values.Count(s => s.Completed && !s.Disabled && IsObjective(s.Item));
        public int Remaining => states.Values.Count(s => !s.Completed && IsObjective(s.Item));
        public LevelOutcome? Outcome { get; private set; }
        public Vector2 LastPosition { get; private set; }
        public double TransitionAge { get; private set; }
        public long Round => registry.Round;
        public ProSequence Pro => pro;

        public bool IsInactiveShutterHazard(LevelItem item)
        {
            if (item == null || item.kind != TileKind.HazardRed || string.IsNullOrEmpty(item.group)) return false;
            foreach (var pair in switches)
            {
                if (!states.TryGetValue(pair.Key, out var switchState)) continue;
                var groupIndex = switchState.Item.targetGroups.IndexOf(item.group);
                if (groupIndex >= 0 && groupIndex != pair.Value) return true;
            }
            return false;
        }

        public bool IsPurpleClearTarget(LevelItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.group)) return false;
            return states.Values.Any(state => state.PurpleHoldActive && state.Item.targetGroups.Contains(item.group));
        }

        public bool IsPurpleClearedAt(LevelItem item, Vector2 point)
        {
            if (item == null || string.IsNullOrEmpty(item.group)) return false;
            var cellX = Mathf.FloorToInt(point.x);
            var cellY = Mathf.FloorToInt(point.y);
            foreach (var state in states.Values)
            {
                var purple = state.Item;
                if (!state.PurpleHoldActive || purple.kind != TileKind.HoldPurple ||
                    !purple.targetGroups.Contains(item.group)) continue;

                var clearMask = purple.purpleClearMask;
                if (clearMask != null)
                {
                    if (clearMask.Contains(cellX - clearMask.offset.x, cellY - clearMask.offset.y)) return true;
                    continue;
                }

                // Older hand-authored/imported levels do not contain clearwrong data.
                // Keep them local by clipping only where the active purple artwork overlaps.
                var frameIndex = purple.AnimationFrameIndex(Elapsed, Level.animationSpeedMilliseconds);
                if (TileShapeUtility.Contains(purple, Position(purple), point, frameIndex)) return true;
            }
            return false;
        }

        public GameSession(int seed = 704, double quietMs = 500) { registry = new ScanRegistry(seed, quietMs); }
        public void Start(ScanLevel level, int questionSeed = 704)
        {
            Level = level; Elapsed = 0; Hearts = Mathf.Clamp(level.initialHearts, 1, MaximumHearts); Outcome = null; TransitionAge = 0;
            Controls.ConfigureBoard(level.venue.Width, level.venue.Height);
            states.Clear(); holds.Clear(); switches.Clear(); contacts.Clear(); removedGroups.Clear(); ascendingAnswers.Clear();
            foreach (var item in level.items)
            {
                if (string.IsNullOrEmpty(item.id) || states.ContainsKey(item.id)) throw new ArgumentException("物件 ID 不可為空或重複。");
                states.Add(item.id, new ItemState { Item = item });
            }
            pro = level.proMechanic != ProMechanic.None
                ? new ProSequence(level.proMechanic, level.items.Where(i => i.proTarget && i.kind == TileKind.TouchBlue).Select(i => i.id), questionSeed)
                : new ProSequence(level.items.Where(i => i.kind == TileKind.TouchBlue));
            foreach (var item in level.items.Where(i => i.kind == TileKind.TouchBlue))
            {
                if (pro.Questions.TryGetValue(item.id, out var question)) ascendingAnswers[item.id] = question.Answer;
                else if (TouchNumberContent.TryGetAnswer(item, out var answer)) ascendingAnswers[item.id] = answer;
            }
            if (level.proMechanic != ProMechanic.None)
                foreach (var state in states.Values.Where(s => s.Item.proTarget && s.Item.kind == TileKind.TouchBlue && !pro.Contains(s.Item.id)))
                { state.Completed = true; state.Disabled = true; }
            registry.ConfigureQuietInterval(level.switchQuietIntervalMs);
            Codes = registry.BeginRound(level.items.Where(i => i.IsScan).Select(i => new ScanTarget(i.id, i.kind == TileKind.SwitchCyan)));
            foreach (var item in level.items.Where(i => i.kind == TileKind.SwitchCyan))
            {
                switches[item.id] = 0;
                SetSwitchedGroups(item, 0);
            }
        }
        public static bool IsObjective(LevelItem item) => item.kind == TileKind.TouchBlue || item.kind == TileKind.ScanBlue;
        public Vector2 Position(LevelItem item) => item.PositionAt(Elapsed, Level.movementSegmentMs);
        public bool IsInside(LevelItem item, Vector2 point) => TileShapeUtility.Contains(item, Position(item), point,
            item.AnimationFrameIndex(Elapsed, Level.animationSpeedMilliseconds));
        public bool TryScanAt(Vector2 point, string devicePrefix, double monotonicMs, out ScanResult result)
        {
            result = default;
            if (Level == null || Outcome.HasValue) return false;
            var state = TopmostHit(new GameTouch(-1, point));
            if (state == null || !state.Item.IsScan || !Codes.TryGetValue(state.Item.id, out var code)) return false;
            result = Scan(devicePrefix, code, monotonicMs);
            return true;
        }
        public void AddHeart() { if (Hearts < MaximumHearts) Hearts++; }
        public string Label(LevelItem item) => pro.Contains(item.id) ? pro.Label(item.id, Elapsed) : TouchNumberContent.DisplayText(item);

        public void Tick(double delta, IReadOnlyList<GameTouch> touches)
        {
            if (Level == null) return;
            delta = Math.Max(0, delta);
            if (Outcome.HasValue) { TransitionAge += delta; return; }
            Elapsed += delta;
            if (Elapsed >= Level.timeLimitSeconds) { Finish(LevelOutcome.Timeout); return; }
            foreach (var id in pro.Tick(Elapsed))
            {
                if (!Controls.Invulnerable)
                {
                    Hearts--; LastPosition = Position(states[id].Item) + (Vector2)states[id].Item.size / 2;
                    if (Hearts <= 0) { Finish(LevelOutcome.HeartsDepleted); return; }
                }
            }
            var acceptedTouches = Controls.FilterTouches(touches);
            var nextContacts = new HashSet<string>();
            var nextHolds = new HashSet<string>();
            var nextPurpleItems = new HashSet<string>();
            pro.ReconcileContacts(acceptedTouches.Select(t =>
            {
                var top = TopmostHit(t);
                return top != null && pro.Contains(top.Item.id) ? (top.Item.id, t.Id) : (null, t.Id);
            }).Where(pair => pair.Item1 != null));
            foreach (var touch in acceptedTouches)
            {
                var state = TopmostHit(touch);
                if (state == null) continue;
                var safe = (state.Item.kind == TileKind.SafeGreen && !state.Disabled) ||
                    (state.Item.kind == TileKind.HazardRed && state.Disabled);
                var item = state.Item;
                var contact = touch.Id + ":" + item.id;
                nextContacts.Add(contact);
                if (item.kind == TileKind.HoldPurple)
                {
                    nextHolds.Add(contact);
                    if (!holds.ContainsKey(contact)) holds.Add(contact, Elapsed);
                    if (Elapsed - holds[contact] >= Level.purpleHoldSeconds)
                    {
                        nextPurpleItems.Add(item.id);
                        state.PurpleHoldActive = true;
                    }
                }
                if (item.kind == TileKind.TouchBlue && !contacts.Contains(contact) && IsNextAscendingTouch(item.id))
                {
                    if (pro.Contains(item.id))
                        foreach (var id in pro.Press(item.id, touch.Id, Elapsed)) Complete(states[id]);
                    else if (!state.Disabled) Complete(state);
                }
                if (item.kind == TileKind.HazardRed && !state.Disabled && !safe)
                {
                    var damageContact = touch.Id + ":hazard:" + (string.IsNullOrWhiteSpace(item.group) ? item.id : item.group);
                    if (!nextContacts.Add(damageContact) || contacts.Contains(damageContact)) continue;
                    if (Controls.Invulnerable) continue;
                    Hearts--; LastPosition = touch.CellPosition;
                    if (Hearts <= 0) { Finish(LevelOutcome.HeartsDepleted); return; }
                }
            }
            foreach (var key in holds.Keys.Where(k => !nextHolds.Contains(k)).ToArray()) holds.Remove(key);
            foreach (var state in states.Values.Where(state => state.Item.kind == TileKind.HoldPurple))
                state.PurpleHoldActive = nextPurpleItems.Contains(state.Item.id);
            contacts = nextContacts;
            CheckSuccess();
        }
        ItemState TopmostHit(GameTouch touch)
        {
            foreach (var item in Level.OrderedItems().Reverse())
                if (states.TryGetValue(item.id, out var state) && !state.Completed &&
                    !IsInactiveShutterHazard(item) &&
                    !(item.kind == TileKind.SafeGreen && state.Disabled) &&
                    TileShapeUtility.Contains(item, Position(item), touch.CellPosition,
                        item.AnimationFrameIndex(Elapsed, Level.animationSpeedMilliseconds)) &&
                    !IsPurpleClearedAt(item, touch.CellPosition)) return state;
            return null;
        }
        bool IsNextAscendingTouch(string id)
        {
            if (!ascendingAnswers.TryGetValue(id, out var answer)) return true;
            var remaining = ascendingAnswers.Where(pair => states.TryGetValue(pair.Key, out var state) && !state.Completed)
                .Select(pair => pair.Value).ToArray();
            return remaining.Length == 0 || answer == remaining.Min();
        }
        void Complete(ItemState state)
        {
            if (state.Completed) return;
            state.Completed = true; LastPosition = Position(state.Item) + (Vector2)state.Item.size / 2;
        }
        public ScanResult Scan(string prefix, string code, double monotonicMs, long? round = null)
        {
            if (Level == null || Outcome.HasValue) return new ScanResult(ScanDisposition.WrongRound, null, prefix);
            var result = registry.Submit(round ?? Round, prefix, code, monotonicMs);
            if (result.Disposition != ScanDisposition.Accepted) return result;
            var state = states[result.TargetId];
            LastPosition = Position(state.Item) + (Vector2)state.Item.size / 2;
            if (state.Item.kind == TileKind.ScanBlue) Complete(state);
            else
            {
                var count = state.Item.targetGroups.Count;
                var next = count == 0 ? 0 : (switches[state.Item.id] + 1) % count;
                switches[state.Item.id] = next; SetSwitchedGroups(state.Item, next);
            }
            CheckSuccess(); return result;
        }
        void SetSwitchedGroups(LevelItem item, int active)
        {
            for (var i = 0; i < item.targetGroups.Count; i++)
            {
                var group = item.targetGroups[i];
                SetGroup(group, i != active || removedGroups.Contains(group));
            }
        }
        void ReapplyGroupState(string group)
        {
            var disabled = removedGroups.Contains(group);
            foreach (var pair in switches)
            {
                var item = states[pair.Key].Item;
                var index = item.targetGroups.IndexOf(group);
                if (index >= 0) disabled = index != pair.Value || removedGroups.Contains(group);
            }
            SetGroup(group, disabled);
        }
        void SetGroup(string group, bool disabled)
        {
            if (string.IsNullOrWhiteSpace(group)) return;
            foreach (var s in states.Values.Where(s => (s.Item.kind == TileKind.HazardRed || s.Item.kind == TileKind.SafeGreen) && s.Item.group == group))
                s.Disabled = disabled;
        }
        void CheckSuccess() { if (!Outcome.HasValue && Remaining == 0) Finish(LevelOutcome.Success); }
        void Finish(LevelOutcome outcome) { Outcome = outcome; TransitionAge = 0; }
    }
}
