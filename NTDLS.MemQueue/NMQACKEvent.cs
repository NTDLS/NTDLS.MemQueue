using NTDLS.MemQueue.Library;
using System;

namespace NTDLS.MemQueue
{
    internal class NMQACKEvent
    {
        public Peer Peer { get; set; }
        public NMQCommand Command { get; set; }
        public DateTime CreatedDate { get; set; }

        public NMQACKEvent(Peer peer, NMQCommand command)
        {
            CreatedDate = DateTime.UtcNow;
            Peer = peer;
            Command = command;
        }

        public string Key
        {
            get
            {
                return $"{Peer.PeerId}-{Command.Message.MessageId}";
            }
        }
    }
}

