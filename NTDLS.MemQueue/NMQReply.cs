using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Represents a reply to a query.
    /// </summary>
    public class NMQReply: NMQMessageBase
    {
        public NMQReply(string message)
        {
            this.Message = message;
        }

        public NMQReply(string label, string message)
        {
            this.Message = message;
            this.Label = label;
        }

        public NMQReply()
        {
        }

        protected NMQReply(Guid clientId, string queueName, Guid messageId, int expireSeconds)
            : base(clientId, queueName, messageId, expireSeconds)
        {
        }
    }
}
