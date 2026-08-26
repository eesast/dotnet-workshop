using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

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
                    throw new InvalidOperationException("Cannot enqueue to a completed work queue.");
                }
                _items.Enqueue(item);
                Monitor.Pulse(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
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
                if (_isCompleted) return;
                _isCompleted = true;
                Monitor.PulseAll(_items);
            }
        }
    }
}
