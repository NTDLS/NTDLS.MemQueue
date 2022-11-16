using System;

namespace MemQueue
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

        protected NMQReply(Guid peerId, string queueName, Guid messageId, int expireSeconds)
            : base(peerId, queueName, messageId, expireSeconds)
        {
        }
    }
}
