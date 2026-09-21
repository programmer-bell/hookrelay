using System.Threading.Channels;

namespace HookRelay.Services;

public sealed class EventBus
{
    private const int SubscriberCapacity = 100;

    private readonly object _lock = new();
    private readonly List<Channel<RequestCapturedEvent>> _subscribers = [];

    public ChannelReader<RequestCapturedEvent> Subscribe()
    {
        var channel = Channel.CreateBounded<RequestCapturedEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<RequestCapturedEvent> reader)
    {
        lock (_lock)
        {
            _subscribers.RemoveAll(channel => channel.Reader == reader);
        }
    }

    public void Publish(RequestCapturedEvent @event)
    {
        Channel<RequestCapturedEvent>[] subscribers;
        lock (_lock)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var channel in subscribers)
        {
            channel.Writer.TryWrite(@event);
        }
    }
}
