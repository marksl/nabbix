using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Common.Logging;

namespace Nabbix
{
    internal class QueryHandler
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(QueryHandler));

        private static byte[] GetResponseLength(string response)
        {
            var responseLength = new byte[8];
            long length = response.Length;
            for (int i = 0; i < 8; i++)
            {
                responseLength[i] = (byte)(int)(length & 0xFF);
                length >>= 8;
            }
            return responseLength;
        }

        private static string GetRequest(NetworkStream stream)
        {
            const int lengthSize = 4;
            const int reservedSize = 4;

            var dataLength = 0;

            var oldProtocol = false;
            var bytes = new List<byte>();
            int oneByte;
            do
            {
                oneByte = stream.ReadByte();
                bytes.Add((byte) oneByte);

                if(oldProtocol && (bytes.Count >= 32 || oneByte == 10))
                    return Encoding.ASCII.GetString(bytes.ToArray());

                if (bytes.Count == Header.Length)
                {
                    if (!bytes.Take(Header.Length).SequenceEqual(Header))
                        oldProtocol = true;
                }
                else if (bytes.Count == Header.Length + lengthSize)
                {
                    var lengthBytes = bytes.Skip(Header.Length).Take(lengthSize).ToArray();
                    dataLength = GetLittleEndianIntegerFromByteArray(lengthBytes, 0);
                }
                else if (bytes.Count == Header.Length + lengthSize + reservedSize + dataLength)
                {
                    var dataBytes = bytes.Skip(Header.Length + lengthSize + reservedSize).Take(dataLength).ToArray();
                    var dataString = Encoding.ASCII.GetString(dataBytes);

                    return dataString;
                }
            } while (oneByte != -1);

            return "ZBX_INVALID_DATA_ERR"; // never occurs i think
        }

        private static readonly byte[] Header = {(byte)'Z', (byte)'B', (byte)'X', (byte)'D', 1};
        private static void SendResponse(NetworkStream stream, string response)
        {
            byte[] responseLength = GetResponseLength(response);
            byte[] responseBytes = Encoding.ASCII.GetBytes(response);

            byte[] buffer = new byte[Header.Length + responseLength.Length + responseBytes.Length];

            // https://www.zabbix.com/documentation/1.8/protocols
            Array.Copy(Header, 0, buffer, 0, Header.Length);
            Array.Copy(responseLength, 0, buffer, Header.Length, responseLength.Length);
            Array.Copy(responseBytes, 0, buffer, Header.Length + responseLength.Length, responseBytes.Length);

            Log.Debug("Response sending...");
            stream.Write(buffer, 0, buffer.Length);
            stream.Flush();
            Log.Debug("Response sent.");
        }

        internal static void Run(TcpClient client, ItemRegistry registry)
        {
            Log.Debug("Run... .");

            var stream = client.GetStream();
            do
            {
                Log.Debug("Request recieving...");
                var request = GetRequest(stream);
                Log.DebugFormat("Request received: {0}", request);

                var response = registry.GetItemValue(request);
                Log.DebugFormat("Response: {0}", response);
                SendResponse(stream, response);
            } while (stream.DataAvailable);

            Log.Debug("Run. Ended.");
        }

        static int GetLittleEndianIntegerFromByteArray(byte[] data, int startIndex)
        {
            return (data[startIndex + 3] << 24)
                   | (data[startIndex + 2] << 16)
                   | (data[startIndex + 1] << 8)
                   | data[startIndex];
        }
    }
}
