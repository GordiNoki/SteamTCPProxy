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

            try
            {
                SteamClient.Init(480, true);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to initialize Steam: " + e.Message);
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            Console.WriteLine($"Hello, {SteamClient.Name}! ({SteamClient.SteamId})");

            ISteamProxyRunner runner;
            if (args[1] == "client")
            {
                runner = new SteamProxyClient(port);
            }
            else
            {
                runner = new SteamProxyServer(port);
            }
            Task.Run(() => runner.RunAsync());

            while (runner.IsAlive)
            {
                SteamClient.RunCallbacks();
                runner.Update();
            }

            SteamClient.Shutdown();
        }
    }
}
