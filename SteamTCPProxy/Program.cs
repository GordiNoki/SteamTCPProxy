using Steamworks;

namespace SteamTCPProxy
{
    internal class Program
    {
        static void Main()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 3 || (args[1] != "server" && args[1] != "client") || !int.TryParse(args[2], out int port))
            {
                Console.WriteLine($"Usage: {args[0]} <server|client> [port]");
                return;
            }

            if (!SteamAPI.Init()) {
                Console.Error.WriteLine("Failed to initialize Steam");
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            Console.WriteLine($"Hello, {SteamFriends.GetPersonaName()}! ({SteamUser.GetSteamID()})");

            ISteamProxyRunner runner;
            if (args[1] == "client")
            {
                Console.WriteLine("Using client on port: " + port);
                runner = new SteamProxyClient(port);
            }
            else
            {
                Console.WriteLine("Using server on port: " + port);
                runner = new SteamProxyServer(port);
            }

            while (runner.IsAlive)
            {
                SteamAPI.RunCallbacks();
                runner.Update();
            }

            SteamAPI.Shutdown();
        }
    }
}
