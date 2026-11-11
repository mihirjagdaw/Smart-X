using System.Threading.Channels;

namespace Smart_X.Api.Processing
{
    /// <summary>Anything that can be told "the operator did this" or "this alert fired".</summary>
    public interface IActionRecorder
    {
        void Record(string key);
    }

    public record ActionEvent(string Key, DateTimeOffset At);

    /// <summary>
    /// How requests hand actions to the prediction service. Record only drops an event into a channel and
    /// returns, so a search, a command or an alert never waits for any learning to happen. Each event is
    /// stamped with the time it happened, not the time it is eventually processed.
    /// </summary>
    public class ActionFeed : IActionRecorder
    {
        // shortcut: if a flood ever fills the channel, the oldest events are dropped rather than slowing the
        // request down; raise the capacity if losing old events during a burst matters.
        private readonly Channel<ActionEvent> _channel = Channel.CreateBounded<ActionEvent>(
            new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        private readonly Func<DateTimeOffset> _clock;

        // The clock is injectable so a test can control the time an event is stamped with.
        public ActionFeed(Func<DateTimeOffset>? clock = null)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public void Record(string key) => _channel.Writer.TryWrite(new ActionEvent(key, _clock()));

        public ChannelReader<ActionEvent> Reader => _channel.Reader;
    }
}
