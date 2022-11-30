using System.Collections.Generic;

namespace NTDLS.MemQueue.Library
{
    public class ListQueue<T> : List<T>
    {
        public void Enqueue(T item)
        {
            lock(this) base.Add(item);
        }

        public T Dequeue()
        {
            lock (this)
            {
                var t = base[0];
                base.RemoveAt(0);
                return t;
            }
        }

        public T Peek()
        {
            lock (this) return base[0];
        }
    }
}
