using System;

namespace MemQueue
{
    /// <summary>
    /// Represents a query which expects a reply.
    /// </summary>
    public class NMQQuery: NMQMessageBase
    {
        public NMQQuery(string queueName, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
        }

        public NMQQuery(string queueName, string label, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
            this.Label = label;
        }

        public NMQQuery()
        {
        }
        protected NMQQuery(Guid peerId, string queueName, Guid messageId, int expireSeconds)
            : base(peerId, queueName, messageId, expireSeconds)
        {
        }

    }
}
