using Steamworks;

namespace SteamTCPProxy
{
    internal class SteamProxyClient(int port) : ISteamProxyRunner
    {
        public bool IsAlive { get; private set; } = true;
        private readonly int port = port;
        private SteamClientConnectionManager? manager;

        public Task RunAsync()
        {
            Console.WriteLine("Using client on port: " + port);
            SteamFriends.OnGameLobbyJoinRequested += async (lobby, steamId) =>
            {
                await SteamMatchmaking.JoinLobbyAsync(lobby.Id);
            };

            SteamMatchmaking.OnLobbyEntered += (lobby) =>
            {
                Console.WriteLine("Joined Lobby");
                uint _ip = 0;
                ushort _port = 0;
                SteamId serverId = new();

                lobby.GetGameServer(ref _ip, ref _port, ref serverId);

                SteamClientConnectionManager.TcpPort = port;

                manager = SteamNetworkingSockets.ConnectRelay<SteamClientConnectionManager>(serverId);

                manager.closeEvent += () =>
                {
                    IsAlive = false;
                };
            };

            return Task.CompletedTask;
        }

        public void Update()
        {
            manager?.Receive();
            manager?.ReceiveTCP();
            manager?.TryAcceptTCP();
        }
    }
}
