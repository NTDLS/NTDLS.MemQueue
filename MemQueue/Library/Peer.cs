using System;
using System.Net.Sockets;

namespace MemQueue.Library
{
    internal class Peer
    {
        public NMQMessageEnvelope CurrentMessage { get; set; }

        public Guid PeerId { get; set; } = Guid.NewGuid();

        public Peer(Socket socket)
        {
            Socket = socket;
        }

        public Socket Socket;
        public Packet Packet { get; set; } = new Packet();
    }
}
