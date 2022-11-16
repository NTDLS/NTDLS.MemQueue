using System;
using System.Threading;

namespace MemQueue
{
    internal class NMQWaitEvent
    {
        public Guid MessageId { get; set; }
        public AutoResetEvent Event { get; set; }
        public DateTime CreatedDate { get; set; }

        public NMQWaitEvent(Guid messageId)
        {
            Event = new AutoResetEvent(false);
            CreatedDate = DateTime.UtcNow;
            MessageId = messageId;
        }


        public NMQWaitEvent()
        {
            Event = new AutoResetEvent(false);
            CreatedDate = DateTime.UtcNow;
        }

        public void Reset()
        {
            Event.Reset();
        }

        public void Set()
        {
            Event.Set();
        }

        public void WaitOne()
        {
            Event.WaitOne();
        }

        public bool WaitOne(int millisecondsTimeout)
        {
            return Event.WaitOne(millisecondsTimeout);
        }
    }
}

