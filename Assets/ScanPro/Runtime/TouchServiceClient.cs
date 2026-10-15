using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ScanPro704
{
    [Serializable] public sealed class TouchCanvas { public int w, h; }
    [Serializable] public sealed class TouchEvent { public int id, face; public string phase; public float[] px; }
    [Serializable] public sealed class TouchFrame
    { public string type, venue; public int v; public long seq, t; public TouchCanvas canvas; public TouchEvent[] events; public int[] alive; }
    public sealed class TouchState
    {
        readonly Dictionary<int, GameTouch> points = new Dictionary<int, GameTouch>();
        long lastSequence = -1;
        public IReadOnlyList<GameTouch> Points => points.Values.ToArray();
        public string Status { get; private set; } = "等待事件";
        public void Reset() { points.Clear(); lastSequence = -1; }
        public bool Apply(string json, OutputProfile profile)
        {
            TouchFrame frame;
            try { frame = JsonUtility.FromJson<TouchFrame>(json); }
            catch { Status = "忽略無效 JSON"; return false; }
            if (frame == null || !string.IsNullOrEmpty(frame.type)) return false;
            if (frame.v != 1 || frame.venue != "704" || frame.canvas == null || frame.canvas.w != profile.outputWidth || frame.canvas.h != profile.outputHeight || frame.alive == null || frame.events == null)
            { Status = "觸控 frame 的場域／尺寸／協議不符"; return false; }
            if (frame.seq <= lastSequence) return false;
            lastSequence = frame.seq;
            foreach (var e in frame.events)
            {
                if (e.phase == "up") { points.Remove(e.id); continue; }
                if ((e.phase != "down" && e.phase != "move") || e.px == null || e.px.Length != 2) continue;
                if (profile.TryUnmap(e.face, new Vector2(e.px[0], e.px[1]), out var source))
                    points[e.id] = new GameTouch(e.id, source / profile.editorLayout.pixelsPerCell);
                else points.Remove(e.id);
            }
            var alive = new HashSet<int>(frame.alive);
            foreach (var id in points.Keys.Where(id => !alive.Contains(id)).ToArray()) points.Remove(id);
            Status = $"frame {frame.seq}｜觸點 {points.Count}";
            return true;
        }
    }

    public sealed class TouchServiceClient : IDisposable
    {
        public readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();
        public string Status { get; private set; } = "連線中";
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly Task task;
        ClientWebSocket socket;
        public TouchServiceClient(string uri) { task = Task.Run(() => Run(uri)); }
        async Task Run(string uri)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using (var current = new ClientWebSocket())
                    {
                        socket = current;
                        await current.ConnectAsync(new Uri(uri), cancellation.Token);
                        Messages.Enqueue("__RESET__"); Status = "TouchService 已連線";
                        var bytes = new byte[8192];
                        while (current.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                        {
                            using (var stream = new MemoryStream())
                            {
                                WebSocketReceiveResult result;
                                do
                                {
                                    result = await current.ReceiveAsync(new ArraySegment<byte>(bytes), cancellation.Token);
                                    if (result.MessageType == WebSocketMessageType.Close) break;
                                    stream.Write(bytes, 0, result.Count);
                                    if (stream.Length > 262144) throw new InvalidDataException("Touch frame too large");
                                } while (!result.EndOfMessage);
                                if (result.MessageType == WebSocketMessageType.Close) break;
                                if (result.MessageType == WebSocketMessageType.Text)
                                {
                                    while (Messages.Count > 256) Messages.TryDequeue(out _);
                                    Messages.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Status = "TouchService 未連線：" + e.GetType().Name; }
                finally { socket = null; Messages.Enqueue("__RESET__"); }
                try { await Task.Delay(1500, cancellation.Token); } catch (OperationCanceledException) { break; }
            }
        }
        public void Dispose() { cancellation.Cancel(); socket?.Abort(); }
    }
}
