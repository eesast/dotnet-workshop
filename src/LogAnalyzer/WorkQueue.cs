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
                // Wake up one (or all) waiting consumer(s).
                Monitor.Pulse(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                // Wait while the queue is empty and adding is not yet complete.
                // A while loop is required to handle spurious wakeups (MESA model).
                while (_items.Count == 0 && !_isCompleted)
                {
                    Monitor.Wait(_items);
                }

                if (_items.Count > 0)
                {
                    item = _items.Dequeue()!;
                    return true;
                }

                item = default;
                return false;
            }
        }

        public void CompleteAdding()
        {
            lock (_items)
            {
                _isCompleted = true;
                // Wake up all consumers so they can notice completion and exit.
                Monitor.PulseAll(_items);
            }
        }
    }
}
