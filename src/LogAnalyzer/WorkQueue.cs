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
                if (!_isCompleted)
                {
                    _items.Enqueue(item);
                    Monitor.Pulse(_items);
                }
                else
                {
                    throw new InvalidOperationException("Enqueue after complete");
                }
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                if (_items.Count > 0)
                {
                    item = _items.Dequeue();
                    return true;
                }
                else
                {
                    if (_isCompleted)
                    {
                        item = default;
                        return false;
                    }
                    else
                    {
                        while (_items.Count == 0 && !_isCompleted)
                        {
                            Monitor.Wait(_items);
                        }
                        if (_items.Count == 0)
                        {
                            item = default;
                            return false;
                        }
                        item = _items.Dequeue();
                        return true;
                    }
                }
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
