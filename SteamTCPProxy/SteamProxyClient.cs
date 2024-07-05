using Steamworks;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;

namespace SteamTCPProxy
{
    internal class SteamProxyClient : ISteamProxyRunner
    {
        public bool IsAlive { get; private set; } = true;

        private readonly Callback<GameLobbyJoinRequested_t> m_GameLobbyJoinRequested;
        private readonly CallResult<LobbyEnter_t> m_LobbyEnter;
        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> m_SteamNetConnectionStatusChangedCallback;

        readonly private IPEndPoint tcpEndPoint;
        readonly Socket listener;
        readonly Dictionary<int, Socket> proxiedSockets = [];
        readonly Dictionary<int, Socket> pendingSockets = [];
        HSteamNetConnection? connection;
        bool canAccept = false;

        public SteamProxyClient(int port) {
            tcpEndPoint = IPEndPoint.Parse("0.0.0.0:" + port);
            listener = new Socket(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            m_LobbyEnter = CallResult<LobbyEnter_t>.Create((result, ioErr) =>
            {
                Console.WriteLine("Joined Lobby");

                var status = SteamMatchmaking.GetLobbyGameServer(new CSteamID(result.m_ulSteamIDLobby), out _, out _, out var serverId);

                if (!status)
                {
                    Console.WriteLine("Failed to get lobby game server");
                    IsAlive = false;
                    return;
                }

                var identity = new SteamNetworkingIdentity();
                identity.SetSteamID(serverId);

                connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            });

            m_GameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create((result) =>
            {
                m_LobbyEnter.Set(SteamMatchmaking.JoinLobby(result.m_steamIDLobby));
            });

            m_SteamNetConnectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChange);
        }

        public void Update()
        {
            Recieve();
            ReceiveTCP();
            TryAcceptTCP();
        }

        void OnConnectionStatusChange(SteamNetConnectionStatusChangedCallback_t result)
        {
            ESteamNetworkingConnectionState state = result.m_info.m_eState;
            ESteamNetworkingConnectionState oldState = result.m_eOldState;
            Console.WriteLine(oldState + " -> " + state);

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected) {
                OnConnected(result.m_info);
            }

            if (
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally
            ) {
                OnDisconnected(result.m_info);
            }
        }

        void OnConnected(SteamNetConnectionInfo_t _)
        {
            listener.Bind(tcpEndPoint);
            listener.Blocking = false;
            listener.Listen();
            Console.WriteLine("Connected to server. Starting local TCP server");
            canAccept = true;
        }

        void OnDisconnected(SteamNetConnectionInfo_t info)
        {
            Console.WriteLine("Disconnected from server.");
            Console.WriteLine(info.m_eEndReason);
            foreach (var socket in proxiedSockets.Values)
            {
                socket.Close();
            }
            IsAlive = false;
        }

        void OnMessage(IntPtr inMessagePtr)
        {
            var inMessage = SteamProxyMessage.FromPtr(inMessagePtr);
            switch (inMessage.Type)
            {
                case SteamProxyMessageType.NEW_SESSION:
                    int pendingId = BitConverter.ToInt32(inMessage.Data, 0);
                    var socket = pendingSockets[pendingId];
                    if (socket == null) break;

                    proxiedSockets.Add(inMessage.SessionId, socket);
                    break;

                case SteamProxyMessageType.MESSAGE:
                    try
                    {
                        if (proxiedSockets.TryGetValue(inMessage.SessionId, out var msgSocket))
                        {
                            msgSocket.Send(inMessage.Data);
                        }
                    }
                    catch (SocketException)
                    {
                        proxiedSockets[inMessage.SessionId].Close();

                        SteamProxyMessage outMessage = new()
                        {
                            Type = SteamProxyMessageType.CLOSE_SESSION,
                            SessionId = inMessage.SessionId
                        };

                        if (connection.HasValue) { 
                            outMessage.SendMessage(connection.Value);
                        }

                        proxiedSockets.Remove(inMessage.SessionId);
                    }
                    break;

                case SteamProxyMessageType.CLOSE_SESSION:
                    if (proxiedSockets.TryGetValue(inMessage.SessionId, out var closeSocket))
                    {
                        closeSocket.Close();
                        proxiedSockets.Remove(inMessage.SessionId);
                    }
                    break;
            }
        }

        void Recieve()
        {
            if (!connection.HasValue) return;
            IntPtr[] pointers = new IntPtr[100];
            var count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.Value, pointers, pointers.Length);
            if (count == -1)
            {
                IsAlive = false;
                return;
            }

            if (count == 0) return;
            for (var i = 0; i < count; i++) {
                var steamMessage = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointers[i]);
                OnMessage(steamMessage.m_pData);
                SteamNetworkingMessage_t.Release(pointers[i]);
            }
        }

        void ReceiveTCP()
        {
            if (!canAccept) return;
            if (!connection.HasValue) return;
            foreach (var socket in proxiedSockets)
            {
                if (!socket.Value.Connected)
                {
                    socket.Value.Close();

                    SteamProxyMessage outMessage = new()
                    {
                        Type = SteamProxyMessageType.CLOSE_SESSION,
                        SessionId = socket.Key
                    };
                    outMessage.SendMessage(connection.Value);

                    continue;
                }
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
                    outMessage.SendMessage(connection.Value);
                }
            }
        }

        void TryAcceptTCP()
        {
            if (!canAccept) return;
            if (!connection.HasValue) return;
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
                handshakeMessage.SendMessage(connection.Value);
            }
            catch (SocketException)
            {
                // No socket to accept
            }
        }
    }
}

