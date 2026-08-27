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
            lock(_items){
                if (_isCompleted){
                    throw new InvalidOperationException("Cannot enqueue a completed queue.");
                }
                _items.Enqueue(item);
                Monitor.PulseAll(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                while(_items.Count == 0)
                {
                    if (_isCompleted)
                    {
                        item = default;
                        return false;
                    }
                    Monitor.Wait(_items);
                }
                item = _items.Dequeue() ?? throw new InvalidOperationException("Queue is empty.");
                Monitor.PulseAll(_items);
                return true;
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
