using System;
using System.Collections.Generic;
using System.Text;

namespace MemQueue.Library
{
    internal class Packet
    {

        /// <summary>
        /// The number of bytes in the current receive buffer.
        /// </summary>
        public int BufferLength;
        /// <summary>
        /// The current receive buffer. May be more than one packet or even a partial packet.
        /// </summary>
        public byte[] Buffer = new byte[NMQConstants.DEFAULT_BUFFER_SIZE];

        /// <summary>
        /// The buffer used to build a full message from the packet. This will be automatically resized if its too small.
        /// </summary>
        public byte[] PayloadBuilder = new byte[NMQConstants.DEFAULT_BUFFER_SIZE];

        /// <summary>
        /// The length of the data currently contained in the PayloadBuilder.
        /// </summary>
        public int PayloadBuilderLength = 0;
    }
}
