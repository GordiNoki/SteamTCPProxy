using Steamworks;

namespace SteamTCPProxy
{
    internal class Program
    {
        static void Main()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || !int.TryParse(args[1], out _))
            {
                Console.WriteLine($"Usage: {args[0]} [port] [--client]");
                return;
            }
            if (!SteamAPI.Init())
            {
                throw new Exception("Failed to initialize Steam.");
            }
            SteamNetworkingUtils.InitRelayNetworkAccess();

            string name = SteamFriends.GetPersonaName();
            CSteamID id = SteamUser.GetSteamID();
            Console.WriteLine($"Hello, {name}! ({id})");
            Console.WriteLine($"Running as " + (args.Contains("--client") ? "client" : "host"));

            if (args.Contains("--client"))
            {
                _ = new SteamProxyClient(args[1]);
            }
            else
            {
                _ = new SteamProxyServer(args[1]);
            }

            // Run Steam callbacks
            var task = Task.Run(() =>
            {
                while (true)
                {
                    SteamAPI.RunCallbacks();
                }
            });
            task.Wait();
        }
    }
}