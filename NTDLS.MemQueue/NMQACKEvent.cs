using System;
using System.Threading;

namespace NTDLS.MemQueue
{
    internal class NMQACKEvent
    {
        public Guid MessageId { get; set; }
        public DateTime CreatedDate { get; set; }

        public NMQACKEvent(Guid messageId)
        {
            CreatedDate = DateTime.UtcNow;
            MessageId = messageId;
        }

        public NMQACKEvent()
        {
            CreatedDate = DateTime.UtcNow;
        }
    }
}
