using System.Runtime.InteropServices;

namespace SteamTCPProxy
{
    internal class SteamProxyMessage
    {
        public SteamProxyMessageType Type;
        public int SessionId;
        public byte[] Data = [];

        public byte[] ToArray() => ToArray(this);
        public static byte[] ToArray(SteamProxyMessage message) {
            var array = new byte[sizeof(int) * 3 + sizeof(byte) * message.Data.Length];

            BitConverter.GetBytes((int)message.Type).CopyTo(array, 0);
            BitConverter.GetBytes(message.SessionId).CopyTo(array, sizeof(int));
            BitConverter.GetBytes(message.Data.Length).CopyTo(array, sizeof(int) * 2);
            message.Data.CopyTo(array, sizeof(int) * 3);

            return array;
        }

        public static SteamProxyMessage FromPtr(IntPtr ptr)
        {
            var message = new SteamProxyMessage
            {
                Type = (SteamProxyMessageType)Marshal.ReadInt32(ptr),
                SessionId = Marshal.ReadInt32(ptr + Marshal.SizeOf<int>()),
            };

            var length = Marshal.ReadInt32(ptr + Marshal.SizeOf<int>() * 2);
            message.Data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                message.Data[i] = Marshal.PtrToStructure<byte>(ptr + Marshal.SizeOf<int>() * 3 + Marshal.SizeOf<byte>() * i);
            }

            return message;
        }
    }

    internal enum SteamProxyMessageType
    {
        NEW_SESSION = 0,
        MESSAGE = 1,
        CLOSE_SESSION = 2
    }
}

