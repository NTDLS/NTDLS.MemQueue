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

    internal static class NMQConstants
    {
        public const int PAYLOAD_DELIMITER = 122455788;
        public const int DEFAULT_BUFFER_SIZE = 1024;
        public const int PAYLOAD_HEADER_SIZE = 10;
        public const int DEFAULT_MIN_MSG_SIZE = 0;
        public const int DEFAULT_MAX_MSG_SIZE = 1024 * 1024;
        public const int DEFAULT_TCPIP_LISTEN_SIZE = 4;
        public const int DEFAULT_STALE_EXPIRATION_SECONDS = 0;
        public const int DEFAULT_PORT = 16117;
        public const int ACK_TIMEOUT_MS = 5000;
        public const int PROCESS_TIMEOUT_MS = 10000;
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
