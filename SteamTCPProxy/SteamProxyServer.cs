using Steamworks;

namespace SteamTCPProxy
{
    internal class SteamProxyServer(int port) : ISteamProxyRunner
    {
        public bool IsAlive { get; private set; } = true;
        private readonly int port = port;
        private Steamworks.Data.Lobby lobby;
        private SteamServerSocketManager? manager;

        public async Task RunAsync()
        {
            Console.WriteLine("Using server on port: " + port);
            var lobbyResult = await SteamMatchmaking.CreateLobbyAsync();
            if (!lobbyResult.HasValue) { throw new Exception("Failed to create Steam lobby."); }
            lobby = lobbyResult.Value;
            lobby.SetFriendsOnly();
            lobby.SetPublic();

            Console.WriteLine(lobby.Id);

            SteamServerSocketManager.TcpPort = port;

            manager = SteamNetworkingSockets.CreateRelaySocket<SteamServerSocketManager>();

            lobby.SetGameServer(SteamClient.SteamId);

            manager.closeEvent += () =>
            {
                IsAlive = false;
            };
        }

        public void Update()
        {
            manager?.Receive();
            manager?.RecieveTCP();
        }
    }
}
