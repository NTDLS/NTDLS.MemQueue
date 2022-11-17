using NTDLS.MemQueue.Library;
using System;

namespace NTDLS.MemQueue
{
    internal class NMQACKEvent
    {
        /// <summary>
        /// The peer which the event was created for.
        /// </summary>
        public Peer Peer { get; set; }
        /// <summary>
        /// The command that we are waiting on an acknowledgement for.
        /// </summary>
        public NMQCommand Command { get; set; }
        /// <summary>
        /// The date/time that the event was created.
        /// </summary>
        public DateTime CreatedDate { get; set; }

        private string _key;

        public NMQACKEvent(Peer peer, NMQCommand command)
        {
            CreatedDate = DateTime.UtcNow;
            Peer = peer;
            Command = command;
            _key = $"[{Peer.PeerId}:{Command.Message.MessageId}]".Replace("-", "");
        }

        /// <summary>
        /// Unique key identifyting both the peer and the command associated with the event.
        /// </summary>
        public string Key
        {
            get
            {
                return _key;
            }
        }
    }
}

