# Steam TCP Proxy

Small utility to proxy TCP connections through Steam relay network.

## Usage
Launch excutable with port as argument:
```
SteamTCPProxy.exe 12345
```
To start hosting Steam socket listener to TCP port 12345

To start client launch exeutable with `--client` attribute:
```
SteamTCPProxy.exe 54321 --client
```
It will start waiting for joining Steam lobby, and connect to owners socket. Then it will start TCP listener on specified port 54321 and proxy one connection through socket.
