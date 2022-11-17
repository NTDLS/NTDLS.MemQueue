using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace NTDLS.MemQueue.Library
{
    public static class Serialization
    {
        public static byte[] ToByteArray(Object obj)
        {
            using var ms = new MemoryStream();
            (new BinaryFormatter()).Serialize(ms, obj);
            return ms.ToArray();
        }

        public static T ToObject<T>(byte[] arrBytes)
        {
            using var memStream = new MemoryStream();
            memStream.Write(arrBytes, 0, arrBytes.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            return (T)(new BinaryFormatter()).Deserialize(memStream);
        }
    }
}
