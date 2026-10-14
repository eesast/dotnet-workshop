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
            // 1. 加锁，确保对 _items 和 _isCompleted 的操作是互斥的
            lock (_items)
            {
                // 如果已经标记为结束，则不再允许放入（可选，根据健壮性要求添加）
                if (_isCompleted) return;

                // 2. 放入商品
                _items.Enqueue(item);

                // 3. 唤醒一个正在等待的消费者（如有）
                Monitor.Pulse(_items);
            }
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
            // 1. 加锁
            lock (_items)
            {
                // 2. 核心：如果仓库为空且还没下班，就原地等待
                // 使用 while 防止“虚假唤醒”或“被截胡”
                while (_items.Count == 0 && !_isCompleted)
                {
                    // 释放锁并进入等待状态，直到被 Pulse 或 PulseAll 唤醒
                    Monitor.Wait(_items);
                }

                // 3. 检查是否有商品可以取出
                if (_items.Count > 0)
                {
                    item = _items.Dequeue()!;
                    return true;
                }

                // 4. 执行到这里说明：仓库为空 且 _isCompleted 为 true
                item = default;
                return false;
            }
        }

        public void CompleteAdding()
        {
            // 1. 加锁
            lock (_items)
            {
                // 2. 标记生产结束
                _isCompleted = true;

                // 3. 核心：广播给所有正在等待的消费者，告诉他们下班了，不用再等了
                Monitor.PulseAll(_items);
            }
        }
    }
}