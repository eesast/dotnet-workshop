using System.Diagnostics.CodeAnalysis;

namespace LogAnalyzer
{
    public class WorkQueue<T>
    {
        private readonly Queue<T> _items = new();
        private bool _isCompleted = false;
        private readonly object _lock = new object();

        public bool IsCompleted
        {
            get
            {
                lock (_lock)
                {
                    return _isCompleted;
                }
            }
        }

        public void Enqueue(T item)
        {
            lock (_lock)                   
            {                              
                _items.Enqueue(item);      
                Monitor.Pulse(_lock);      
            }        
        }

        public bool TryDequeue([NotNullWhen(true)] out T? item)
        {
             lock (_lock)                   
            {                              
                while (_items.Count == 0 && !_isCompleted) 
                {                          
                    Monitor.Wait(_lock);   
                }                          

                if (_items.Count > 0)     
                {                          
                    item = _items.Dequeue(); 
                    return true;           
                }                         
                else                       
                {                          
                    item = default(T);     
                    return false;          
                }                          
            }
        }

        public void CompleteAdding()
        {
            lock (_lock)                   
            {                              
                _isCompleted = true;      
                Monitor.PulseAll(_lock);  
            }
        }
    }
}
