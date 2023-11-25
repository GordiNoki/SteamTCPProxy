using Steamworks;
using System.Net.Sockets;
using System.Net;

namespace SteamTCPProxy
{
    internal class SteamProxyServer
    {
        private readonly CallResult<LobbyCreated_t> lobbyCreatedEvent;
        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> socketStatusChangeEvent;
        private readonly EndPoint tcpEndPoint;

        private readonly ProxiedConnection[] connections = [];

        public SteamProxyServer(string tcpPort)
        {
            lobbyCreatedEvent = CallResult<LobbyCreated_t>.Create(OnLobbyCreate);
            socketStatusChangeEvent = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSocketStatusChange);

            var endpointString = "127.0.0.1:" + tcpPort;
            Console.WriteLine("TCP will connect to " + endpointString);
            tcpEndPoint = IPEndPoint.Parse(endpointString);

            var handle = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 250);
            lobbyCreatedEvent.Set(handle);
            Console.WriteLine("Created lobby");

            SteamNetworkingSockets.CreateListenSocketP2P(0, 0, []);
            Console.WriteLine("Created socket");
        }

        private void OnLobbyCreate(LobbyCreated_t callbackData, bool IOFailed)
        {
            if (IOFailed || callbackData.m_eResult != EResult.k_EResultOK)
            {
                Console.WriteLine("Failed to create lobby");
            }
        }

        private void OnSocketStatusChange(SteamNetConnectionStatusChangedCallback_t callbackData)
        {
            var oldState = callbackData.m_eOldState;
            var newState = callbackData.m_info.m_eState;
            if (oldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None && newState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                Console.WriteLine("New connection");
                SteamNetworkingSockets.AcceptConnection(callbackData.m_hConn);

                var tcpSocket = new Socket(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                tcpSocket.Connect(tcpEndPoint);
                var connection = new ProxiedConnection(callbackData.m_hConn, tcpSocket);
                connections.Append(connection);
            }
        }
    }
}
