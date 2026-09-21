using System.Threading.Channels;

namespace HookRelay.Services;

public sealed class EventBus
{
    private const int SubscriberCapacity = 100;

    private readonly object _lock = new();
    private readonly Dictionary<Type, List<object>> _subscribers = [];

    public ChannelReader<T> Subscribe<T>()
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
            {
                subscribers = [];
                _subscribers.Add(typeof(T), subscribers);
            }

            subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Unsubscribe<T>(ChannelReader<T> reader)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(typeof(T), out var subscribers))
            {
                subscribers.RemoveAll(channel => ((Channel<T>)channel).Reader == reader);
            }
        }
    }

    public void Publish<T>(T @event)
    {
        Channel<T>[] subscribers;
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var existing))
            {
                return;
            }

            subscribers = existing.Cast<Channel<T>>().ToArray();
        }

        foreach (var channel in subscribers)
        {
            channel.Writer.TryWrite(@event);
        }
    }
}
