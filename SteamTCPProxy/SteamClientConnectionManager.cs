using Steamworks;
using Steamworks.Data;
using System.Net;
using System.Net.Sockets;

namespace SteamTCPProxy
{
    internal class SteamClientConnectionManager : ConnectionManager
    {
        static private IPEndPoint tcpEndPoint = IPEndPoint.Parse("0.0.0.0");
        static public int TcpPort
        {
            private get => tcpEndPoint != null ? tcpEndPoint.Port : 0;
            set
            {
                tcpEndPoint = IPEndPoint.Parse("0.0.0.0:" + value);
            }
        }
        public event Action? closeEvent;
        readonly System.Net.Sockets.Socket listener = new(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        bool canAccept = false;
        readonly Dictionary<int, System.Net.Sockets.Socket> proxiedSockets = [];
        readonly Dictionary<int, System.Net.Sockets.Socket> pendingSockets = [];

        public override void OnConnected(ConnectionInfo info)
        {
            listener.Bind(tcpEndPoint);
            listener.Blocking = false;
            listener.Listen();
            Console.WriteLine("Connected to server. Starting local TCP server");
            canAccept = true;
        }

        public override void OnConnecting(ConnectionInfo info)
        {
            Console.WriteLine("Connecting to server...");
        }

        public override void OnDisconnected(ConnectionInfo info)
        {
            Console.WriteLine("Disconnected from server.");
            Console.WriteLine(info.EndReason);
            foreach (var socket in proxiedSockets.Values)
            {
                socket.Close();
            }
            closeEvent?.Invoke();
        }

        public override void OnMessage(IntPtr inMessagePtr, int size, long messageNum, long recvTime, int channel)
        {
            var inMessage = SteamProxyMessage.FromPtr(inMessagePtr);
            switch (inMessage.Type)
            {
                case SteamProxyMessageType.NEW_SESSION:
                    int pendingId = BitConverter.ToInt32(inMessage.Data, 0);
                    var socket = pendingSockets[pendingId];
                    if(socket == null) break;

                    proxiedSockets.Add(inMessage.SessionId, socket);
                    break;

                case SteamProxyMessageType.MESSAGE:
                    try
                    {
                        proxiedSockets[inMessage.SessionId].Send(inMessage.Data);
                    }
                    catch (SocketException) 
                    {
                        proxiedSockets[inMessage.SessionId].Close();

                        SteamProxyMessage outMessage = new()
                        {
                            Type = SteamProxyMessageType.CLOSE_SESSION,
                            SessionId = inMessage.SessionId
                        };
                        Connection.SendMessage(outMessage.ToArray());

                        proxiedSockets.Remove(inMessage.SessionId);
                    }
                    break;

                case SteamProxyMessageType.CLOSE_SESSION:
                    proxiedSockets[inMessage.SessionId].Close();
                    proxiedSockets.Remove(inMessage.SessionId);
                    break;
            }
        }

        public void ReceiveTCP()
        {
            if (!canAccept) return;
            foreach(var socket in proxiedSockets)
            {
                if (!socket.Value.Connected) {
                    socket.Value.Close();

                    SteamProxyMessage outMessage = new()
                    {
                        Type = SteamProxyMessageType.CLOSE_SESSION,
                        SessionId = socket.Key
                    };
                    Connection.SendMessage(outMessage.ToArray());

                    continue;
                }
                int count;
                if ((count = socket.Value.Available) > 0)
                {
                    var data = new byte[count];
                    socket.Value.Receive(data, 0, count, SocketFlags.None);

                    SteamProxyMessage outMessage = new()
                    {
                        Type = SteamProxyMessageType.MESSAGE,
                        SessionId = socket.Key,
                        Data = data
                    };
                    Connection.SendMessage(outMessage.ToArray());
                }
            }
        }

        public void TryAcceptTCP() { 
            if (!canAccept) return;
            try
            {
                var pendingSocket = listener.Accept();

                int pendingId = pendingSocket.GetHashCode();
                pendingSockets.Add(pendingId, pendingSocket);

                SteamProxyMessage handshakeMessage = new()
                {
                    Type = SteamProxyMessageType.NEW_SESSION,
                    SessionId = 0,
                    Data = BitConverter.GetBytes(pendingId)
                };
                Connection.SendMessage(handshakeMessage.ToArray());
            }
            catch (SocketException)
            {
                // No socket to accept
            }
        }
    }
}
