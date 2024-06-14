namespace SteamTCPProxy
{
    internal interface ISteamProxyRunner
    {
        public bool IsAlive { get; }
        public Task RunAsync();
        public void Update();
    }
}
