namespace NTDLS.MemQueue
{
    internal class NMQMessageEnvelope
    {
        public NMQMessageBase Message { get; set; }
        public bool Sent { get; set; }
        public bool Acknowledged { get; set; }
        public bool Errored { get; set; }

        public bool IsComplete
        {
            get
            {
                return (Sent && Acknowledged) || Errored;
            }
        }

    }
}
