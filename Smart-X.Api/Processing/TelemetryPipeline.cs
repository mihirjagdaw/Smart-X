using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Processing
{
    public enum PacketRank { Critical = 0, Normal = 1 }

    public enum Lane { Priority, Standard }

    public record Packet(string Mac, SensorCategory Category, float Value, DateTimeOffset Timestamp, PacketRank Rank);

    /// <summary>
    /// Two-lane ingest buffer. Normal telemetry waits in a FIFO Queue; critical packets go into a
    /// PriorityQueue and are always dequeued first, so an alert never waits behind a backlog.
    /// </summary>
    public class TelemetryPipeline
    {
        private const int RecentCapacity = 100;

        private readonly Queue<Packet> _standard = new();
        // The sequence number keeps arrival order among equal ranks; PriorityQueue alone is not stable.
        private readonly PriorityQueue<Packet, (int Rank, long Sequence)> _priority = new();
        private readonly Queue<ProcessedEvent> _recent = new();
        // Counts packets across both lanes, so a successful wait always finds something to dequeue.
        private readonly SemaphoreSlim _signal = new(0);
        private readonly object _lock = new();

        private long _sequence;
        private long _processedStandard;
        private long _processedPriority;
        private long _dropped;

        public void Enqueue(Packet packet)
        {
            lock (_lock)
            {
                if (packet.Rank == PacketRank.Normal)
                {
                    _standard.Enqueue(packet);
                }
                else
                {
                    _priority.Enqueue(packet, ((int)packet.Rank, _sequence++));
                }
            }
            _signal.Release();
        }

        public async Task<(Packet Packet, Lane Lane)> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            lock (_lock)
            {
                return _priority.TryDequeue(out var urgent, out _)
                    ? (urgent, Lane.Priority)
                    : (_standard.Dequeue(), Lane.Standard);
            }
        }

        public void RecordProcessed(Packet packet, Lane lane)
        {
            lock (_lock)
            {
                if (lane == Lane.Priority) _processedPriority++; else _processedStandard++;

                _recent.Enqueue(new ProcessedEvent(
                    _processedPriority + _processedStandard, packet.Mac, packet.Category, packet.Value,
                    lane.ToString(), packet.Timestamp, DateTimeOffset.UtcNow));
                while (_recent.Count > RecentCapacity) _recent.Dequeue();
            }
        }

        public void RecordDropped()
        {
            lock (_lock)
            {
                _dropped++;
            }
        }

        public PipelineStats GetStats()
        {
            lock (_lock)
            {
                return new PipelineStats(_standard.Count, _priority.Count, _processedStandard, _processedPriority, _dropped);
            }
        }

        // Newest first, which is how the live feed wants to show it.
        public List<ProcessedEvent> GetRecent()
        {
            lock (_lock)
            {
                return _recent.Reverse().ToList();
            }
        }
    }
}
