namespace NTDLS.MemQueue
{
    internal enum PayloadCommandType
    {
        Unspecified,

        //Commands sent to a SERVER:
        Enqueue,
        Subscribe,
        UnSubscribe,
        Clear,

        //Commands sent to a CLIENT:
        ProcessMessage
    }

    internal static class NMQConstants
    {
        public const int PAYLOAD_DELIMITER = 122455788;
        public const int DEFAULT_BUFFER_SIZE = 1024 * 64;
        public const int PAYLOAD_HEADEER_SIZE = 10;
        public const int DEFAULT_MIN_MSG_SIZE = 0;
        public const int DEFAULT_MAX_MSG_SIZE = 1024 * 1024;
        public const int DEFAULT_TCPIP_LISTEN_SIZE = 4;
        public const int DEFAULT_STALE_EXPIRATION_SECONDS = 0;
        public const int DEFAULT_PORT = 16117;
    }

    public enum ClientConnectAction
    {
        Accept,
        Reject
    }

    public enum PayloadSendAction
    {
        Process,
        Discard,
        Skip
    }

    public enum PayloadReceiveAction
    {
        Process,
        Discard
    }
}
