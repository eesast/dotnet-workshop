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
            throw new NotImplementedException("TODO: T2.1");
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            throw new NotImplementedException("TODO: T2.1");
        }

        public void CompleteAdding()
        {
            throw new NotImplementedException("TODO: T2.1");
        }
    }
}
