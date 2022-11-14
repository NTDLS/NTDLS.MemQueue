using System.Net.Sockets;

namespace NTDLS.MemQueue.Library
{
    internal class Peer
    {
        public Peer(Socket socket)
        {
            Socket = socket;
        }

        public Socket Socket;
        public Packet Packet { get; set; } = new Packet();
    }
}
