using LogAnalyzer;
using System.Dynamic;

namespace test_02_multithreading
{
    [TestClass]
    public sealed class Test_2_1_WorkingQueue
    {
        [TestMethod(DisplayName = "T2.1.1 TestBasicAdding")]
        [Timeout(1000, CooperativeCancellation = true)]
        public void TestBasicAdding()
        {
            var queue = new WorkQueue<int>();
            for (int i = 0; i < 10; ++i)
            {
                queue.Enqueue(i);
            }
            for (int i = 0; i < 10; ++i)
            {
                Assert.IsTrue(queue.TryDequeue(out var item), $"Failed to dequeue item {i}");
                Assert.AreEqual(i, item, $"Expected {i} but got {item}");
            }
        }

        [TestMethod(DisplayName = "T2.1.2 TestCompleteAdding")]
        [Timeout(1000, CooperativeCancellation = true)]
        public void TestCompleteAdding()
        {
            var queue = new WorkQueue<int>();
            for (int i = 0; i < 10; ++i)
            {
                queue.Enqueue(i);
            }
            queue.CompleteAdding();
            Assert.IsTrue(queue.IsCompleted, "Queue should be marked as completed after calling CompleteAdding");

            for (int i = 0; i < 10; ++i)
            {
                Assert.IsTrue(queue.TryDequeue(out var item), $"Failed to dequeue item {i}");
                Assert.AreEqual(i, item, $"Expected {i} but got {item}");
            }
        }

        [TestMethod(DisplayName = "T2.1.3 TestConcurrentAdding")]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestConcurrentAdding()
        {
            const int nThread = 8;
            const int nItem = 50000;

            var queue = new WorkQueue<(int, int)>();
            var threads = new Thread[nThread];
            for (int i = 0; i < nThread; ++i)
            {
                int threadId = i; // Capture the loop variable
                threads[i] = new Thread(() =>
                {
                    for (int j = 0; j < nItem; ++j)
                    {
                        queue.Enqueue((threadId, j));
                    }
                })
                {
                    IsBackground = true,
                    Name = $"TestConcurrentAdding-Thread-{threadId}"
                };
                threads[i].Start();
            }

            new Thread(() =>
            {
                foreach (var thread in threads)
                {
                    thread.Join();
                }
                queue.CompleteAdding();
            })
            {
                IsBackground = true,
                Name = $"TestConcurrentAdding-Thread-Join"
            }.Start();

            var lastDeque = new int[nThread];
            for (int i = 0; i < nThread; ++i)
            {
                lastDeque[i] = -1;
            }

            while (queue.TryDequeue(out var item))
            {
                Assert.AreEqual(lastDeque[item.Item1] + 1, item.Item2,
                    $"Expected {lastDeque[item.Item1] + 1} but got {item.Item2} for thread {item.Item1}");
                ++lastDeque[item.Item1];
            }

            for (int i = 0; i < nThread; ++i)
            {
                Assert.AreEqual(nItem - 1, lastDeque[i],
                    $"Expected last dequeued item for thread {i} to be {nItem - 1} but got {lastDeque[i]}");
            }
        }
    }
}
