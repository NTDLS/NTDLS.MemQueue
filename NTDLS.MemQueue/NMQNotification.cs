using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Represents a notification message that does not expect a reply.
    /// </summary>
    public class NMQNotification : NMQMessageBase
    {
        public NMQNotification(string queueName, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
        }

        public NMQNotification(string queueName, string label, string message)
        {
            this.QueueName = queueName;
            this.Message = message;
            this.Label = label;
        }

        public NMQNotification()
        {
        }

        protected NMQNotification(Guid peerId, string queueName, Guid messageId, int expireSeconds)
            : base(peerId, queueName, messageId, expireSeconds)
        {
        }
    }
}
