using Steamworks;
using Steamworks.Data;
using System.Net;
using System.Net.Sockets;

namespace SteamTCPProxy
{
    internal class SteamServerSocketManager : SocketManager
    {
        static private IPEndPoint tcpEndPoint = IPEndPoint.Parse("127.0.0.1");
        static public int TcpPort
        {
            private get => tcpEndPoint != null ? tcpEndPoint.Port : 0;
            set {
                tcpEndPoint = IPEndPoint.Parse("127.0.0.1:" + value);
            }
        }
        public event Action? closeEvent;
        readonly Dictionary<SteamId, Dictionary<int, System.Net.Sockets.Socket>> proxiedSockets = [];
        readonly Dictionary<SteamId, Connection> connections = [];

        public override void OnConnecting(Connection connection, ConnectionInfo data)
        {
            if (!data.Identity.IsSteamId)
            {
                connection.Close(false, 0, "Non relay connection");
                return;
            }
            connection.Accept();
        }

        public override void OnConnected(Connection connection, ConnectionInfo data)
        {
            Console.WriteLine($"{data.Identity} has connected");
            proxiedSockets.Add(data.Identity.SteamId, []);
            connections.Add(data.Identity.SteamId, connection);
            base.OnConnected(connection, data); // Adds connection to poll group
        }

        public override void OnDisconnected(Connection connection, ConnectionInfo data)
        {
            Console.WriteLine($"{data.Identity} disconnected");
            var status = proxiedSockets.TryGetValue(data.Identity.SteamId, out var sockets);
            if (status && sockets != null && sockets.Count != 0)
            {
                foreach(var socket in sockets.Values)
                { 
                    socket.Close();
                }
            }
            proxiedSockets.Remove(data.Identity.SteamId);
            connections.Remove(data.Identity.SteamId);
        }

        public override void OnMessage(Connection connection, NetIdentity identity, IntPtr inMessagePtr, int size, long messageNum, long recvTime, int channel)
        {
            var inMessage = SteamProxyMessage.FromPtr(inMessagePtr);
            switch(inMessage.Type)
            {
                case SteamProxyMessageType.NEW_SESSION:
                    try
                    {
                        var socket = new System.Net.Sockets.Socket(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        socket.Connect(tcpEndPoint);

                        if (socket.LocalEndPoint == null)
                        {
                            Console.WriteLine("Could not create new session for " + identity.SteamId);
                            break;
                        }

                        var id = ((IPEndPoint)socket.LocalEndPoint).Port;
                        proxiedSockets[identity.SteamId].Add(id, socket);

                        Console.WriteLine($"New session {id} for {identity.SteamId}");

                        SteamProxyMessage outMessage = new()
                        {
                            Type = SteamProxyMessageType.NEW_SESSION,
                            SessionId = id,
                            Data = inMessage.Data
                        };
                        connection.SendMessage(outMessage.ToArray());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception while creating new session: " + e.Message);
                        closeEvent?.Invoke();
                    }
                    break;

                case SteamProxyMessageType.MESSAGE:
                    proxiedSockets[identity.SteamId][inMessage.SessionId].Send(inMessage.Data);
                    break;

                case SteamProxyMessageType.CLOSE_SESSION:
                    proxiedSockets[identity.SteamId][inMessage.SessionId].Close();
                    proxiedSockets[identity.SteamId].Remove(inMessage.SessionId);
                    break;
            }
        }

        public void RecieveTCP()
        {
            foreach (var sockets in proxiedSockets)
            {
                var hasConnection = connections.TryGetValue(sockets.Key, out var connection);
                if (hasConnection)
                {
                    foreach (var socket in sockets.Value)
                    {
                        int count;
                        if ((count = socket.Value.Available) > 0) {
                            var data = new byte[count];
                            socket.Value.Receive(data, 0, count, SocketFlags.None);

                            SteamProxyMessage outMessage = new()
                            {
                                Type = SteamProxyMessageType.MESSAGE,
                                SessionId = socket.Key,
                                Data = data
                            };
                            connection.SendMessage(outMessage.ToArray());
                        }
                    }
                }
            }
        }
    }
}
