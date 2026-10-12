/*
 * Happy Time Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.TcpServer;

namespace HappyTime;

public sealed class HappyTimeOptions : ITcpServerOptions
{
    public const string SectionName = "Time";

    public string ListenAddress { get; set; } = "127.0.0.1";
    public bool DualMode { get; set; }
    public int Port { get; set; } = 37;
    public int MaxConcurrentConnections { get; set; } = 64;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public string[] TelemetryIgnoredRemoteAddresses { get; set; } = [];

    public bool UdpEnabled { get; set; }
    public string? UdpListenAddress { get; set; }
    public int? UdpPort { get; set; }

    ConnectionLimitBehavior ITcpServerOptions.ConnectionLimitBehavior =>
        ConnectionLimitBehavior.Wait;
}
