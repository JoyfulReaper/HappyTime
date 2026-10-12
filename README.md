# HappyTime

HappyTime is a small .NET implementation of the classic **RFC 868 Time Protocol**.

It listens for Time Protocol requests over **TCP and UDP** and returns the current time as a 32-bit unsigned integer representing the number of seconds since **1900-01-01 00:00:00 UTC**, encoded in **network byte order (big-endian)**.

## Protocol

RFC 868 uses port **37**.

### TCP

A client connects to TCP port 37.

The server immediately sends exactly four bytes containing the current Time Protocol timestamp, then closes the connection.

### UDP

A client sends a datagram to UDP port 37.

The server replies with a four-byte Time Protocol timestamp.

The contents of the request datagram are not significant.

## Timestamp Format

The wire value is:

```text
uint32 seconds since 1900-01-01 00:00:00 UTC
```

The value is transmitted in big-endian byte order.

Example:

```text
EE 77 AC 73
```

decodes to:

```text
4000820339 seconds since 1900-01-01
2026-10-12 18:58:59 UTC
```

Because RFC 868 uses an unsigned 32-bit counter, the timestamp wraps in **February 2036**.

## Requirements

- .NET 10 SDK
- Windows or Linux
- Permission to bind the configured TCP and UDP ports

HappyTime uses `JoyfulReaperLib` for its shared TCP server infrastructure.

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run
```

By default, HappyTime is intended to use TCP and UDP port 37. If you are developing without elevated privileges, configure a higher port such as `1037`.

## Configuration

HappyTime uses the standard .NET configuration system.

Typical options include:

```json
{
  "HappyTime": {
    "ListenAddress": "0.0.0.0",
    "Port": 37,
    "MaxConcurrentConnections": 100,
    "RequestTimeoutSeconds": 5,
    "UdpEnabled": true,
    "UdpPort": 37
  }
}
```

Exact option names should match `HappyTimeOptions`.

## Testing from PowerShell

### TCP

```powershell
$client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", 37)

try {
    $stream = $client.GetStream()
    $buffer = [byte[]]::new(4)
    $read = 0

    while ($read -lt 4) {
        $n = $stream.Read($buffer, $read, 4 - $read)
        if ($n -eq 0) { break }
        $read += $n
    }

    if ($read -ne 4) {
        throw "Expected 4 bytes, received $read"
    }

    $seconds = [System.Buffers.Binary.BinaryPrimitives]::ReadUInt32BigEndian($buffer)
    $epoch = [DateTimeOffset]::new(1900, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

    "Raw bytes: $([Convert]::ToHexString($buffer))"
    "Seconds since 1900: $seconds"
    "Server time (UTC): $($epoch.AddSeconds($seconds).ToString('u'))"
}
finally {
    if ($null -ne $client) {
        $client.Dispose()
    }
}
```

### UDP

```powershell
$udp = [System.Net.Sockets.UdpClient]::new()
$udp.Client.ReceiveTimeout = 3000

try {
    $udp.Connect("127.0.0.1", 37)

    [void]$udp.Send([byte[]]@(0), 1)

    $remote = [System.Net.IPEndPoint]::new(
        [System.Net.IPAddress]::Any, 0)

    $buffer = $udp.Receive([ref]$remote)

    if ($buffer.Length -ne 4) {
        throw "Expected 4 bytes, received $($buffer.Length)"
    }

    $seconds = [System.Buffers.Binary.BinaryPrimitives]::ReadUInt32BigEndian($buffer)
    $epoch = [DateTimeOffset]::new(1900, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

    "Raw bytes: $([Convert]::ToHexString($buffer))"
    "Seconds since 1900: $seconds"
    "Server time (UTC): $($epoch.AddSeconds($seconds).ToString('u'))"
}
finally {
    $udp.Dispose()
}
```

If HappyTime is configured on a development port such as `1037`, replace `37` in the examples above.

## Status

The core RFC 868 implementation has been verified locally over both:

- TCP
- UDP

Planned follow-up work includes:

- Mission Control telemetry
- Production deployment
- Remote TCP and UDP verification

## License

Copyright (c) 2026 Kyle Givler

Licensed under the MIT License.

## References

- RFC 868 — Time Protocol
