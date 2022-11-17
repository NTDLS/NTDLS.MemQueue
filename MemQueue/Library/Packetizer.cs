using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MemQueue.Library
{
    internal static class Packetizer
    {
        public delegate void ProcessPayloadCallback(Peer peer, NMQCommand payload);

        public static byte[] AssembleMessagePacket(NMQBase q, NMQCommand payload)
        {
            try
            {
                var payloadBody = Serialization.ObjectToByteArray(payload);
                var payloadBytes = Compress(payloadBody);
                var grossPacketSize = payloadBytes.Length + NMQPacketizer.PACKET_HEADER_SIZE;
                var packetBytes = new byte[grossPacketSize];
                var payloadCrc = CRC16.ComputeChecksum(payloadBytes);

                Buffer.BlockCopy(BitConverter.GetBytes(NMQPacketizer.PACKET_DELIMITER), 0, packetBytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(grossPacketSize), 0, packetBytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(payloadCrc), 0, packetBytes, 8, 2);
                Buffer.BlockCopy(payloadBytes, 0, packetBytes, NMQPacketizer.PACKET_HEADER_SIZE, payloadBytes.Length);

                return packetBytes;
            }
            catch (Exception ex)
            {
                q.LogException(ex);
            }

            return null;
        }

        private static void SkipPacket(NMQBase q, ref Packet packet)
        {
            try
            {
                var payloadDelimiterBytes = new byte[4];

                for (int offset = 1; offset < packet.PayloadBuilderLength - payloadDelimiterBytes.Length; offset++)
                {
                    Buffer.BlockCopy(packet.PayloadBuilder, offset, payloadDelimiterBytes, 0, payloadDelimiterBytes.Length);

                    var value = BitConverter.ToInt32(payloadDelimiterBytes, 0);

                    if (value == NMQPacketizer.PACKET_DELIMITER)
                    {
                        Buffer.BlockCopy(packet.PayloadBuilder, offset, packet.PayloadBuilder, 0, packet.PayloadBuilderLength - offset);
                        packet.PayloadBuilderLength -= offset;
                        return;
                    }
                }
                Array.Clear(packet.PayloadBuilder, 0, packet.PayloadBuilder.Length);
                packet.PayloadBuilderLength = 0;
            }
            catch (Exception ex)
            {
                q.LogException(ex);
            }
        }

        public static void DissasemblePacketData(NMQBase q, Peer peer, Packet packet, ProcessPayloadCallback processPayload)
        {
            try
            {
                if (packet.PayloadBuilderLength + packet.BufferLength >= packet.PayloadBuilder.Length)
                {
                    Array.Resize(ref packet.PayloadBuilder, packet.PayloadBuilderLength + packet.BufferLength);
                }

                Buffer.BlockCopy(packet.Buffer, 0, packet.PayloadBuilder, packet.PayloadBuilderLength, packet.BufferLength);

                packet.PayloadBuilderLength = packet.PayloadBuilderLength + packet.BufferLength;

                while (packet.PayloadBuilderLength > NMQPacketizer.PACKET_HEADER_SIZE) //[PayloadSize] and [CRC16]
                {
                    var payloadDelimiterBytes = new byte[4];
                    var payloadSizeBytes = new byte[4];
                    var expectedCRC16Bytes = new byte[2];

                    Buffer.BlockCopy(packet.PayloadBuilder, 0, payloadDelimiterBytes, 0, payloadDelimiterBytes.Length);
                    Buffer.BlockCopy(packet.PayloadBuilder, 4, payloadSizeBytes, 0, payloadSizeBytes.Length);
                    Buffer.BlockCopy(packet.PayloadBuilder, 8, expectedCRC16Bytes, 0, expectedCRC16Bytes.Length);

                    var payloadDelimiter = BitConverter.ToInt32(payloadDelimiterBytes, 0);
                    var grossPayloadSize = BitConverter.ToInt32(payloadSizeBytes, 0);
                    var expectedCRC16 = BitConverter.ToUInt16(expectedCRC16Bytes, 0);

                    if (payloadDelimiter != NMQPacketizer.PACKET_DELIMITER)
                    {
                        q.LogException(new Exception("Malformed payload packet, invalid delimiter."));
                        SkipPacket(q, ref packet);
                        continue;
                    }

                    if (grossPayloadSize < 0 || grossPayloadSize > NMQPacketizer.PACKET_MAX_SIZE)
                    {
                        q.LogException(new Exception("Malformed payload packet, invalid length."));
                        SkipPacket(q, ref packet);
                        continue;
                    }

                    if (packet.PayloadBuilderLength < grossPayloadSize)
                    {
                        //We have data in the buffer, but it's not enough to make up
                        //  the entire message so we will break and wait on more data.
                        break;
                    }

                    var actualCRC16 = CRC16.ComputeChecksum(packet.PayloadBuilder, NMQPacketizer.PACKET_HEADER_SIZE, grossPayloadSize - NMQPacketizer.PACKET_HEADER_SIZE);

                    if (actualCRC16 != expectedCRC16)
                    {
                        q.LogException(new Exception("Malformed payload packet, invalid CRC."));
                        SkipPacket(q, ref packet);
                        continue;
                    }

                    var netPayloadSize = grossPayloadSize - NMQPacketizer.PACKET_HEADER_SIZE;
                    var payloadBytes = new byte[netPayloadSize];

                    Buffer.BlockCopy(packet.PayloadBuilder, NMQPacketizer.PACKET_HEADER_SIZE, payloadBytes, 0, netPayloadSize);

                    var payloadBody = Decompress(payloadBytes);

                    var payload = (NMQCommand)Serialization.ByteArrayToObject(payloadBody);

                    processPayload(peer, payload);

                    //Zero out the consumed portion of the payload buffer - more for fun than anything else.
                    Array.Clear(packet.PayloadBuilder, 0, grossPayloadSize);

                    Buffer.BlockCopy(packet.PayloadBuilder, grossPayloadSize, packet.PayloadBuilder, 0, packet.PayloadBuilderLength - grossPayloadSize);
                    packet.PayloadBuilderLength -= grossPayloadSize;
                }
            }
            catch (Exception ex)
            {
                q.LogException(ex);
            }
        }

        public static byte[] Compress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }
                return mso.ToArray();
            }
        }

        public static byte[] Decompress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }
                return mso.ToArray();
            }
        }
    }
}
