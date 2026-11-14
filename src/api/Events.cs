using System.Threading.Channels;

namespace MockBucket.Core;

/// <summary>
/// Per-bucket fan-out for Server-Sent Events. One channel per live subscriber,
/// bounded; a stalled consumer gets its channel cleared and a `reset` event telling the client
/// to resync via `GET /api/buckets/{id}/entries?since=`. Capture never blocks on a slow UI.
/// </summary>
public sealed class EventHub
{
    public sealed record Event(string Name, string Data);

    public sealed class Subscription
    {
        internal readonly System.Threading.Channels.Channel<Event> Channel = System.Threading.Channels.Channel.CreateBounded<Event>(
            new System.Threading.Channels.BoundedChannelOptions(256) { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite, SingleReader = true });

        public async IAsyncEnumerable<Event> ReadAll()
        {
            await foreach (var e in Channel.Reader.ReadAllAsync())
            {
                yield return e;
                if (e.Name == "close") yield break;
            }
        }

        internal bool Offer(Event e)
        {
            if (Channel.Writer.TryWrite(e)) return true;
            while (Channel.Reader.TryRead(out var _)) { }   // overflow: drop backlog…
            return Channel.Writer.TryWrite(new Event("reset", "{}")) && Channel.Writer.TryWrite(e);
        }

        public ValueTask DisposeAsync() { Channel.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Subscription>> _subs = new();

    public Subscription Subscribe(string bucketId)
    {
        var sub = new Subscription();
        var list = _subs.GetOrAdd(bucketId, _ => []);
        lock (list) list.Add(sub);
        return sub;
    }

    public void Publish(string bucketId, string name, string data)
    {
        if (!_subs.TryGetValue(bucketId, out var list)) return;
        Subscription[] snapshot;
        lock (list) snapshot = list.ToArray();
        foreach (var s in snapshot) s.Offer(new Event(name, data));
    }

    public void Unsubscribe(string bucketId, Subscription sub)
    {
        if (!_subs.TryGetValue(bucketId, out var list)) return;
        lock (list) list.Remove(sub);
    }
}
