using System.Diagnostics.CodeAnalysis;

namespace LogAnalyzer
{
    public class WorkQueue<T>
    {
        private readonly Queue<T> _items = new();
        private bool _isCompleted = false;

        public bool IsCompleted
        {
            get
            {
                lock (_items)
                {
                    return _isCompleted;
                }
            }
        }

        public void Enqueue(T item)
        {
            lock (_items)
            {
                if (_isCompleted)
                {
                    throw new InvalidOperationException("Cannot enqueue after CompleteAdding has been called.");
                }
                _items.Enqueue(item);
                System.Threading.Monitor.Pulse(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                // while 而非 if：防止虚假唤醒（spurious wakeup）
                while (_items.Count == 0)
                {
                    if (_isCompleted)
                    {
                        item = default;
                        return false;
                    }
                    System.Threading.Monitor.Wait(_items);
                }
                item = _items.Dequeue()!;
                return true;
            }
        }

        public void CompleteAdding()
        {
            lock (_items)
            {
                _isCompleted = true;
                System.Threading.Monitor.PulseAll(_items);
            }
        }
    }
}
