using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Beauty.Api.Services;

public record GiftEvent(string Emoji, string GiftName, string SenderId, bool IsBattleGift);

public class GiftBroadcastService
{
    private readonly ConcurrentDictionary<int, ConcurrentBag<ChannelWriter<GiftEvent>>> _writers = new();

    public IAsyncEnumerable<GiftEvent> SubscribeAsync(int streamId, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<GiftEvent>();
        var bag = _writers.GetOrAdd(streamId, _ => new ConcurrentBag<ChannelWriter<GiftEvent>>());
        bag.Add(channel.Writer);

        ct.Register(() => channel.Writer.TryComplete());

        return channel.Reader.ReadAllAsync(ct);
    }

    public void Broadcast(int streamId, GiftEvent evt)
    {
        if (!_writers.TryGetValue(streamId, out var bag)) return;
        foreach (var writer in bag)
            writer.TryWrite(evt);
    }
}
