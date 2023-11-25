using Steamworks;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamTCPProxy
{
    internal class ProxiedConnection
    {
        private Task runTask;
        private bool isActive = true;
        private readonly HSteamNetConnection steamConnection;
        private readonly Socket tcpSocket;

        public ProxiedConnection(HSteamNetConnection steamConnection, Socket tcpSocket)
        {
            this.steamConnection = steamConnection;
            this.tcpSocket = tcpSocket;

            runTask = Task.Run(Start);
        }

        public void WaitTillDie()
        {
            runTask.Wait();
        }

        private async void Start()
        {
            await Task.WhenAll([Task.Run(PollSteamConnection), Task.Run(PollTCPConnection)]);
            SteamNetworkingSockets.CloseConnection(steamConnection, 0, "Closed as useless", true);
            tcpSocket.Disconnect(false);
        }

        private void PollSteamConnection()
        {
            while (true)
            {
                if (!RecieveMessagesOnSteam() || !isActive)
                {
                    isActive = false;
                    break;
                }
            }
        }

        private bool RecieveMessagesOnSteam()
        {
            nint[] pMessages = new nint[100];
            var status = SteamNetworkingSockets.ReceiveMessagesOnConnection(steamConnection, pMessages, 100);
            if (status == -1) return false;
            for (var i = 0; i < status; i++)
            {
                var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pMessages[i]);
                var buffer = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, buffer, 0, message.m_cbSize);
                tcpSocket.Send(buffer);
                SteamNetworkingMessage_t.Release(pMessages[i]);
            }
            return true;
        }

        private void PollTCPConnection()
        {
            while (true)
            {
                try
                {
                    if (!RecieveMessagesOnTCP() || !isActive)
                    {
                        isActive = false;
                        break;
                    }
                }
                catch
                {
                    isActive = false;
                    break;
                }
            }
        }

        private bool RecieveMessagesOnTCP()
        {
            if (!tcpSocket.Connected) return false;
            var buffer = new byte[1024];
            var bufLen = tcpSocket.Receive(buffer, SocketFlags.None);
            if (bufLen > 0)
            {
                nint messagePointer = Marshal.AllocCoTaskMem(65536);
                Marshal.Copy(buffer, 0, messagePointer, bufLen);
                SteamNetworkingSockets.SendMessageToConnection(steamConnection, messagePointer, (uint)bufLen, Constants.k_nSteamNetworkingSend_Reliable, out _);
                Marshal.FreeCoTaskMem(messagePointer);
            }
            return true;
        }
    }
}
