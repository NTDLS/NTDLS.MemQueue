﻿using ProtoBuf;
using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Base for all messages, queries and replies.
    /// </summary>
    [Serializable]
    [ProtoContract]

    public class NMQMessageBase
    {
        [ProtoMember(1)]
        internal DateTime CreatedTime { get; set; }
        /// <summary>
        /// If set, the time at which the message will be removed from the server if not dequeued by a client. Set via ExpireSeconds.
        /// </summary>
        [ProtoMember(2)]
        public DateTime? ExpireTime { get; private set; }
        /// <summary>
        /// The name of the queue to broadcast to. Each subscriber of the queue will receive the message.
        /// </summary>
        [ProtoMember(3)]
        public string QueueName { get; internal set; }
        /// <summary>
        /// In additon to the message, the label is additional data that can be broadcast to subscribers.
        /// </summary>
        [ProtoMember(4)]
        public string Label { get; set; }
        /// <summary>
        /// The message to be broadcast to subscribers.
        /// </summary>
        [ProtoMember(5)]
        public string Message { get; set; }
        /// <summary>
        /// The unique identifier of the message. Each message will have a unique id..
        /// </summary>
        [ProtoMember(6)]
        internal Guid MessageId { get; set; }
        /// <summary>
        /// Unique id of the client that sent the message.
        /// </summary>
        [ProtoMember(7)]
        public Guid PeerId { get; internal set; }
        /// <summary>
        /// For replies, this is the MessageId of the message for which the reply is to.
        /// </summary>
        [ProtoMember(8)]
        internal Guid? InReplyToMessageId { get; set; }
        /// <summary>
        /// Denotes whether this message is a query. If neither query or reply, it is a message.
        /// </summary>
        [ProtoMember(9)]
        public bool IsQuery { get; internal set; }
        /// <summary>
        /// Denotes whether this message is a reply. If neither query or reply, it is a message.
        /// </summary>
        public bool IsReply
        {
            get
            {
                return InReplyToMessageId != null;
            }
        }

        [ProtoMember(10)]
        private int _expireSeconds = 0;
        /// <summary>
        /// If set, the time at which the message will be removed from the server if not dequeued by a client.
        /// </summary>
        public int ExpireSeconds
        {
            get
            {
                return _expireSeconds;
            }
            set
            {
                _expireSeconds = value;
                if (value > 0)
                {
                    ExpireTime = DateTime.UtcNow.AddSeconds(value);
                }
                else
                {
                    ExpireTime = null;
                }
            }
        }

        public NMQMessageBase(Guid peerId, string queueName, Guid messageId, int expireSeconds)
        {
            PeerId = peerId;
            MessageId = messageId;
            QueueName = queueName;
            CreatedTime = DateTime.UtcNow;
            ExpireSeconds = expireSeconds;
        }

        public NMQMessageBase(Guid peerId, string queueName, Guid messageId)
        {
            PeerId = peerId;
            MessageId = messageId;
            QueueName = queueName;
            CreatedTime = DateTime.UtcNow;
        }

        public NMQMessageBase(Guid peerId, Guid messageId)
        {
            PeerId = peerId;
            MessageId = messageId;
            CreatedTime = DateTime.UtcNow;
        }

        public NMQMessageBase()
        {
        }

        public T As<T>() where T : NMQMessageBase, new()
        {
            var derived = new T()
            {
                CreatedTime = this.CreatedTime,
                ExpireTime = this.ExpireTime,
                QueueName = this.QueueName,
                Label = this.Label,
                Message = this.Message,
                MessageId = this.MessageId,
                PeerId = this.PeerId,
                InReplyToMessageId = this.InReplyToMessageId,
                IsQuery = this.IsQuery
            };

            return derived;
        }
    }
}
