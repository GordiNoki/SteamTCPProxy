using Steamworks;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamTCPProxy
{
    internal class SteamProxyServer : ISteamProxyRunner
    {
        public bool IsAlive { get; private set; } = true;

        private readonly CallResult<LobbyCreated_t> m_LobbyCreated;
        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> m_SteamNetConnectionStatusChangedCallback;

        private IPEndPoint tcpEndPoint = IPEndPoint.Parse("127.0.0.1");
        private CSteamID? lobbyId;
        private readonly HSteamNetPollGroup pollGroup;
        readonly Dictionary<CSteamID, Dictionary<int, Socket>> proxiedSockets = [];
        readonly Dictionary<CSteamID, HSteamNetConnection> connections = [];

        public SteamProxyServer(int port) {
            tcpEndPoint = IPEndPoint.Parse("127.0.0.1:" + port);

            pollGroup = SteamNetworkingSockets.CreatePollGroup();

            m_LobbyCreated = CallResult<LobbyCreated_t>.Create((result, ioErr) => {
                if(ioErr) {
                    Console.WriteLine("Failed to create lobby");
                    IsAlive = false;
                    return;
                }

                lobbyId = new CSteamID(result.m_ulSteamIDLobby);

                var configVal = new SteamNetworkingConfigValue_t
                {
                    m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
                    m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    m_val = { 
                        m_int32 = 0,
                    }
                };

                var listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, [configVal]);
                SteamMatchmaking.SetLobbyGameServer(lobbyId.Value, 0, 0, SteamUser.GetSteamID());
            });

            m_LobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 100));

            m_SteamNetConnectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChange);
        }

        public void Update()
        {
            Receive();
            ReceiveTCP();
        }

        void OnConnectionStatusChange(SteamNetConnectionStatusChangedCallback_t result)
        {
            ESteamNetworkingConnectionState state = result.m_info.m_eState;
            ESteamNetworkingConnectionState oldState = result.m_eOldState;
            Console.WriteLine(oldState + " -> " + state);

            if (
                oldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None &&
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
            ) {
                OnConnecting(result.m_hConn);
            }

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                OnConnected(result.m_hConn, result.m_info.m_identityRemote);
            }

            if (
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally
            )
            {
                OnDisconnected(result.m_hConn, result.m_info.m_identityRemote);
            }
        }

        void OnConnecting(HSteamNetConnection connection)
        {
            SteamNetworkingSockets.AcceptConnection(connection);
        }

        void OnConnected(HSteamNetConnection connection, SteamNetworkingIdentity identity)
        {
            proxiedSockets.Add(identity.GetSteamID(), []);
            connections.Add(identity.GetSteamID(), connection);

            

            SteamNetworkingSockets.SetConnectionPollGroup(connection, pollGroup);
        }

        void OnDisconnected(HSteamNetConnection _connection, SteamNetworkingIdentity identity)
        {
            var steamId = identity.GetSteamID();
            Console.WriteLine($"{steamId} disconnected");
            var status = proxiedSockets.TryGetValue(steamId, out var sockets);
            if (status && sockets != null && sockets.Count != 0)
            {
                foreach (var socket in sockets.Values)
                {
                    socket.Close();
                }
            }
            proxiedSockets.Remove(steamId);
            connections.Remove(steamId);
        }

        void OnMessage(HSteamNetConnection connection, SteamNetworkingIdentity identity, IntPtr inMessagePtr)
        {
            var steamId = identity.GetSteamID();
            if (!connections.ContainsKey(steamId))
            {
                connections.Add(steamId, connection);
                proxiedSockets.Add(steamId, []);
            }

            var inMessage = SteamProxyMessage.FromPtr(inMessagePtr);
            switch (inMessage.Type)
            {
                case SteamProxyMessageType.NEW_SESSION:
                    try
                    {
                        var socket = new Socket(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        socket.Connect(tcpEndPoint);

                        if (socket.LocalEndPoint == null)
                        {
                            Console.WriteLine("Could not create new session for " + steamId);
                            break;
                        }

                        var id = ((IPEndPoint)socket.LocalEndPoint).Port;
                        proxiedSockets[steamId].Add(id, socket);

                        Console.WriteLine($"New session {id} for {steamId}");

                        SteamProxyMessage outMessage = new()
                        {
                            Type = SteamProxyMessageType.NEW_SESSION,
                            SessionId = id,
                            Data = inMessage.Data
                        };
                        outMessage.SendMessage(connection);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception while creating new session: " + e.Message);
                        IsAlive = false;
                    }
                    break;

                case SteamProxyMessageType.MESSAGE:
                    if (proxiedSockets[steamId].TryGetValue(inMessage.SessionId, out var outSocket))
                    {
                        try
                        {
                            outSocket.Send(inMessage.Data);
                        }
                        catch
                        {
                            outSocket.Close();
                            proxiedSockets[steamId].Remove(inMessage.SessionId);

                            SteamProxyMessage outMessage = new()
                            {
                                Type = SteamProxyMessageType.CLOSE_SESSION,
                                SessionId = inMessage.SessionId,
                            };
                            outMessage.SendMessage(connection);
                        }
                    }
                    break;

                case SteamProxyMessageType.CLOSE_SESSION:
                    if (proxiedSockets[steamId].TryGetValue(inMessage.SessionId, out var oldSocket))
                    {
                        oldSocket.Close();
                        proxiedSockets[steamId].Remove(inMessage.SessionId);
                    }
                    break;
            }
        }

        void Receive()
        {
            IntPtr[] pointers = new IntPtr[100];
            var count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, pointers, pointers.Length);
            if (count == -1)
            {
                IsAlive = false;
                return;
            }

            if (count == 0) return;
            for (var i = 0; i < count; i++)
            {
                var steamMessage = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointers[i]);
                OnMessage(steamMessage.m_conn, steamMessage.m_identityPeer, steamMessage.m_pData);
                SteamNetworkingMessage_t.Release(pointers[i]);
            }
        }

        void ReceiveTCP()
        {
            foreach (var sockets in proxiedSockets)
            {
                var hasConnection = connections.TryGetValue(sockets.Key, out var connection);
                if (hasConnection)
                {
                    foreach (var socket in sockets.Value)
                    {
                        int count;
                        if ((count = socket.Value.Available) > 0)
                        {
                            if (count > 524272) count = 524272;
                            
                            var data = new byte[count];
                            socket.Value.Receive(data, 0, count, SocketFlags.None);

                            SteamProxyMessage outMessage = new()
                            {
                                Type = SteamProxyMessageType.MESSAGE,
                                SessionId = socket.Key,
                                Data = data
                            };
                            outMessage.SendMessage(connection);
                        }
                    }
                }
            }
        }
    }
}
