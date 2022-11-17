using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Internal command which allows for lowelevel communication betweeen server and client.
    /// </summary>
    [Serializable]
    internal class NMQCommand
    {
        /// <summary>
        /// The enclosed message.
        /// </summary>
        public NMQMessageBase Message { get; set; }

        /// <summary>
        /// The type of command. Tells the engine how to interpret the enclosed message.
        /// </summary>
        public PayloadCommandType CommandType { get; set; }
    }
}
