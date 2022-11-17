using System;
using System.Threading;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Maps queries to replies so they can be routed accordingly.
    /// </summary>
    internal class NMQQuerySubscription
    {
        public AutoResetEvent PayloadReceivedEvent { get; private set; }
        public AutoResetEvent PayloadProcessedEvent { get; private set; }
        public Guid OriginalMessageId { get; private set; }
        public NMQMessageBase ReplyQueueItem { get; private set; }

        public void SetReply(NMQMessageBase replyQueueItem)
        {
            ReplyQueueItem = replyQueueItem;
            PayloadReceivedEvent.Set();
        }

        public NMQQuerySubscription(Guid originalMessageId)
        {
            PayloadReceivedEvent = new AutoResetEvent(false);
            PayloadProcessedEvent = new AutoResetEvent(false);
            OriginalMessageId = originalMessageId;
            ReplyQueueItem = null;
        }
    }
}
