﻿namespace SteamTCPProxy
{
    internal interface ISteamProxyRunner
    {
        public bool IsAlive { get; }
        public void Update();
    }
}
