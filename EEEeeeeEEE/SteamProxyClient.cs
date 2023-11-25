using Steamworks;
using System.Net.Sockets;
using System.Net;

namespace SteamTCPProxy
{
    internal class SteamProxyClient
    {
        private readonly CallResult<LobbyEnter_t> lobbyEnteredEvent;
        private readonly Callback<GameLobbyJoinRequested_t> gameLobbyJoinEvent;
        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> socketStatusChangeEvent;

        private Socket? currentTcpSocket;
        private SteamNetworkingIdentity lobbyIdentity;

        private readonly Socket tcpServer;
        public SteamProxyClient(string tcpPort)
        {
            lobbyEnteredEvent = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);
            gameLobbyJoinEvent = Callback<GameLobbyJoinRequested_t>.Create(OnGameJoinLobbyRequest);
            socketStatusChangeEvent = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSocketStatusChange);

            var endpointString = "0.0.0.0:" + tcpPort;
            Console.WriteLine("TCP will listen on " + endpointString);
            var tcpEndPoint = IPEndPoint.Parse(endpointString);

            tcpServer = new Socket(tcpEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            tcpServer.Bind(tcpEndPoint);
            tcpServer.Listen();
        }


        private void OnLobbyEnter(LobbyEnter_t callbackData, bool IOFailed)
        {
            if (IOFailed)
            {
                Console.WriteLine("Failed to enter lobby");
                return;
            }

            lobbyIdentity = new SteamNetworkingIdentity();
            var hostSteamId = SteamMatchmaking.GetLobbyOwner((CSteamID)callbackData.m_ulSteamIDLobby);
            lobbyIdentity.SetSteamID(hostSteamId);
            Task.Run(WaitForNewTCPSocket);
        }

        private void OnSocketStatusChange(SteamNetConnectionStatusChangedCallback_t callbackData)
        {
            var oldState = callbackData.m_eOldState;
            var newState = callbackData.m_info.m_eState;
            if ((oldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting || oldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute) && newState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                Console.WriteLine("Connected to socket");
                Task.Run(() =>
                {
                    if (currentTcpSocket != null)
                    {
                        var connection = new ProxiedConnection(callbackData.m_hConn, currentTcpSocket);
                        connection.WaitTillDie();
                    }
                    Task.Run(WaitForNewTCPSocket);
                });
            }

            if (newState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                Console.WriteLine(callbackData.m_info.m_szEndDebug);
            }
        }

        private void OnGameJoinLobbyRequest(GameLobbyJoinRequested_t callbackData)
        {
            Console.WriteLine("Joining lobby");
            var handle = SteamMatchmaking.JoinLobby(callbackData.m_steamIDLobby);
            lobbyEnteredEvent.Set(handle);
        }

        private void WaitForNewTCPSocket()
        {
            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for TCP");
                    var tcpConnection = tcpServer.Accept();
                    Console.WriteLine("TCP Connection!");
                    currentTcpSocket = tcpConnection;
                    Console.WriteLine("Trying to connect to socket");
                    SteamNetworkingSockets.ConnectP2P(ref lobbyIdentity, 0, 0, []);
                    break;
                }
            }
            catch { }
        }
    }
}
