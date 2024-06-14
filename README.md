# Steam TCP Proxy

Small utility to proxy TCP connections through Steam relay network.

## Usage
Launch excutable with `server` and port as arguments:
```
SteamTCPProxy.exe server 12345
```
To start hosting Steam socket listener to TCP port 12345
Every connection through proxy will go to local 12345 port

To start client launch exeutable with `client` argument:
```
SteamTCPProxy.exe client 54321
```
It will create TCP listener on specified port, where every connection will be proxied through Steam