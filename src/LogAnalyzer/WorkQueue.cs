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
                    throw new InvalidOperationException(
                        "Cannot enqueue after CompleteAdding has been called.");
                }
                _items.Enqueue(item);
                // 唤醒一个正在等待的消费者（signal 操作）
                Monitor.Pulse(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            lock (_items)
            {
                // 用 while 而非 if：避免虚假唤醒（spurious wakeup）带来的错误判断
                while (_items.Count == 0 && !_isCompleted)
                {
                    // 队列为空且尚未结束放入：解锁互斥量并等待（wait 操作）
                    Monitor.Wait(_items);
                }

                if (_items.Count > 0)
                {
                    item = _items.Dequeue();
                    return true;
                }

                // 队列为空且已结束放入：返回 false
                item = default;
                return false;
            }
        }

        public void CompleteAdding()
        {
            lock (_items)
            {
                _isCompleted = true;
                // 唤醒全部正在等待的消费者（broadcast 操作），使其能正常退出
                Monitor.PulseAll(_items);
            }
        }
    }
}
