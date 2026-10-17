namespace Smart_X.Api.Processing
{
    // What a device looked like before a command touched it, so the command can be reverted.
    public record DeviceChange(string Mac, float? PreviousOverride, float? PreviousValue);

    public record CommandRecord(int Id, string Description, DateTimeOffset At, List<DeviceChange> Changes);

    /// <summary>
    /// Manual override commands in the order they were issued. A Stack is last-in-first-out, which is exactly
    /// what undo needs: the most recent command is always the next one reverted, including a bulk
    /// "shut everything down", because one record carries the previous state of every device it touched.
    /// </summary>
    public class CommandHistory
    {
        private readonly Stack<CommandRecord> _stack = new();
        private readonly object _lock = new();
        private int _nextId = 1;

        public CommandRecord Push(string description, List<DeviceChange> changes)
        {
            lock (_lock)
            {
                var record = new CommandRecord(_nextId++, description, DateTimeOffset.UtcNow, changes);
                _stack.Push(record);
                return record;
            }
        }

        // Null when there is nothing left to undo.
        public CommandRecord? TryPop()
        {
            lock (_lock)
            {
                return _stack.TryPop(out var record) ? record : null;
            }
        }

        // Most recent first, which is the order they would be undone in.
        public List<CommandRecord> Snapshot()
        {
            lock (_lock)
            {
                return _stack.ToList();
            }
        }
    }
}
