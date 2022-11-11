using System;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Base class for NMQClient and NMQServer.
    /// </summary>
    public abstract class NMQBase
    {
        public abstract void LogException(Exception ex);
    }
}
