using ProtoBuf;
using System.IO;

namespace NTDLS.MemQueue.Library
{
    public static class Serialization
    {
        public static byte[] ToByteArray(object obj)
        {
            if (obj == null) return null;

            using var stream = new MemoryStream();
            Serializer.Serialize(stream, obj);
            return stream.ToArray();
        }

        public static T ToObject<T>(byte[] arrBytes)
        {
            using var stream = new MemoryStream();
            stream.Write(arrBytes, 0, arrBytes.Length);
            stream.Seek(0, SeekOrigin.Begin);
            return Serializer.Deserialize<T>(stream);
        }
    }
}
