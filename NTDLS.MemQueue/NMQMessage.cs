using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Represents a message that does not expect a reply.
    /// </summary>
    public class NMQMessage : NMQMessageBase
    {
        public NMQMessage(string queueName, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
        }

        public NMQMessage(string queueName, string label, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
            this.Label = label;
        }

        public NMQMessage()
        {
        }

        protected NMQMessage(Guid peerId, string queueName, Guid messageId, int expireSeconds)
            : base(peerId, queueName, messageId, expireSeconds)
        {
        }
    }
}
