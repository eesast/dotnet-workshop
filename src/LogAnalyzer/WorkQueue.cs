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
                _isCompleted = false;
                if (item == null)
                    throw new ArgumentNullException(nameof(item));
                _items.Enqueue(item);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                if (_items.Count > 0)
                {
                    item = _items.Dequeue();
                    if (item != null)
                    {
                        return true;
                    }
                }
                else
                {
                    while (!_isCompleted)
                    {
                        Monitor.Wait(_items);
                    }
                    if (_items.Count > 0)
                    {
                        item = _items.Dequeue();
                        if (item != null)
                        {
                            return true;
                        }
                    }
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
                Monitor.PulseAll(_items);
            }
        }
    }
}
