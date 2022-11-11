using System.Net.Sockets;

namespace NTDLS.MemQueue.Library
{
    internal class Peer
    {
        public Peer(Socket socket)
        {
            Socket = socket;
        }

        public bool Disconnected = false;
        public int BytesReceived;
        public Socket Socket;
        public byte[] Buffer = new byte[NMQConstants.DEFAULT_BUFFER_SIZE];
        public byte[] PayloadBuilder = new byte[NMQConstants.DEFAULT_BUFFER_SIZE];
        public int PayloadBuilderLength = 0;
    }
}
