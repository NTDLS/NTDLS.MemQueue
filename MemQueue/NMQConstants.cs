namespace MemQueue
{
    internal enum PayloadCommandType
    {
        Unspecified,
        Hello,
        CommandAck,

        //Commands sent to a SERVER:
        Enqueue,
        Subscribe,
        UnSubscribe,
        Clear,
        ProcessedAck,

        //Commands sent to a CLIENT:
        ProcessMessage
    }

    internal static class NMQPacketizer
    {
        public const int PACKET_DELIMITER = 122455788;
        public const int PACKET_HEADER_SIZE = 10;
        public const int PACKET_MAX_SIZE = 1024 * 1024 * 128; //128MB, resize all you want - its just a sanity check - not a hard limit.
    }

    internal static class NMQConstants
    {
        public const int DEFAULT_BUFFER_SIZE = 1024;
        public const int DEFAULT_TCPIP_LISTEN_SIZE = 16;
        public const int DEFAULT_STALE_EXPIRATION_SECONDS = 0;
        public const int DEFAULT_PORT = 16117;
        public const int DEFAULT_ACK_TIMEOUT_MS = 5000;
        public const int DEFAULT_PROCESS_TIMEOUT_MS = 10000;
    }

    public enum ClientConnectAction
    {
        Accept,
        Reject
    }

    public enum PayloadInterceptAction
    {
        Process,
        Discard
    }

    public enum BrodcastScheme
    {
        /// <summary>
        /// The scheme has not been set, the default (Uniform) will be used when started.
        /// </summary>
        NotSet,
        /// <summary>
        /// Send each message to each subscriber. Ensure that each message has been received and processed by each subscriber before moving to the next message.
        /// </summary>
        Uniform,
        /// <summary>
        /// Send each message to each subscriber as fast as possible with no regard for acknowledgement or receipt or processing.
        /// </summary>
        FireAndForget
    }
}
