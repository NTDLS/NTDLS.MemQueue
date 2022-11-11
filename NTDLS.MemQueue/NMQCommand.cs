using NTDLS.MemQueue.Library;
using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Internal command which allows for lowelevel communication betweeen server and client.
    /// </summary>
    [Serializable]
    internal class NMQCommand
    {
        public NMQMessageBase Message { get; set; }
        public PayloadCommandType CommandType { get; set; }
    }
}
