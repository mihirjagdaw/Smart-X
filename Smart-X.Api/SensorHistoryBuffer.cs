using Smart_X.Shared;

namespace Smart_X.Api
{
    /// <summary>
    /// A circular buffer that stores a fixed number of the most recent sensor readings for a specific device. 
    /// When the buffer reaches its capacity, the oldest readings are overwritten by new ones.
    /// </summary>
    public class SensorHistoryBuffer
    {
        private readonly SensorReading?[] _buffer;
        private int _start;
        private int _count;

        public SensorHistoryBuffer(int capacity)
        {
            _buffer = new SensorReading?[capacity];
        }

        public void Add(SensorReading reading)
        {
            int index = (_start + _count) % _buffer.Length;
            _buffer[index] = reading;

            if (_count < _buffer.Length)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % _buffer.Length;
            }
        }

        public List<SensorReading> ToList()
        {
            var result = new List<SensorReading>(_count);
            for (int i = 0; i < _count; i++)
            {
                result.Add(_buffer[(_start + i) % _buffer.Length]!);
            }
            return result;
        }

        public SensorReading? Latest => _count == 0 ? null : _buffer[(_start + _count - 1) % _buffer.Length];
    }
}
