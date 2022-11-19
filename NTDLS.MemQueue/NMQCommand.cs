using ProtoBuf;
using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Internal command which allows for lowelevel communication betweeen server and client.
    /// </summary>
    [Serializable]
    [ProtoContract]
    internal class NMQCommand
    {
        /// <summary>
        /// The enclosed message.
        /// </summary>
        [ProtoMember(1)]
        public NMQMessageBase Message { get; set; }

        /// <summary>
        /// The type of command. Tells the engine how to interpret the enclosed message.
        /// </summary>
        [ProtoMember(2)]
        public PayloadCommandType CommandType { get; set; }
    }
}
