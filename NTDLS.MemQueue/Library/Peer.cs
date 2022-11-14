using System;
using System.Net.Sockets;

namespace NTDLS.MemQueue.Library
{
    internal class Peer
    {
        public Guid UID { get; set; } = Guid.NewGuid();

        public Peer(Socket socket)
        {
            Socket = socket;
        }

        public Socket Socket;
        public Packet Packet { get; set; } = new Packet();
    }
}
