/*
 * Happy Time Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.JRNet;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HappyTime;

public sealed class UdpTimeService(
    ILogger<UdpTimeService> logger,
    IOptions<HappyTimeOptions> options)
    : BackgroundService
{
    private static readonly TimeSpan TelemetryPublishTimeout = TimeSpan.FromSeconds(2);
    private UdpClient? _udp;

    public override async Task StartAsync(
        CancellationToken cancellationToken)
    {
        HappyTimeOptions value = options.Value;

        if (value.UdpEnabled)
        {
            IPAddress listenAddress =
                IPAddressUtils.ParseListenAddress(
                    string.IsNullOrWhiteSpace(
                        value.UdpListenAddress)
                        ? value.ListenAddress
                        : value.UdpListenAddress);

            int port = value.UdpPort ?? value.Port;
            _udp = CreateUdpClient(listenAddress, port, value.DualMode);

            logger.LogInformation(
                "HappyTime UDP listener bound to {Endpoint} (dual mode: {DualMode}).",
                _udp.Client.LocalEndPoint,
                value.DualMode);
        }

        try
        {
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            _udp?.Dispose();
            _udp = null;
            throw;
        }
    }

    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            _udp?.Dispose();
            _udp = null;
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        HappyTimeOptions value = options.Value;

        if (!value.UdpEnabled)
        {
            logger.LogInformation("HappyTime UDP listener disabled.");

            return;
        }

        UdpClient udp = _udp
            ?? throw new InvalidOperationException("UDP Time listener was not initialized.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult received;

                try
                {
                    received = await udp.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Socket error while receiving UDP Daytime datagram.");

                    continue;
                }
                catch (ObjectDisposedException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RespondAsync(
                    udp,
                    received.RemoteEndPoint,
                    stoppingToken);
            }
        }
        finally
        {
            udp.Dispose();
            _udp = null;

            logger.LogInformation("HappyTime UDP listener stopped.");
        }
    }

    private async Task RespondAsync(
        UdpClient udp,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        string correlationId = Guid.NewGuid().ToString("N");

        Stopwatch stopwatch = Stopwatch.StartNew();

        //string response = DaytimeResponseFormatter.Format(occurredAt);

        string outcome = "failed";
        bool succeeded = false;

        try
        {
            byte[] responseBytes = TimeHelper.GetTimeBigEdianBytes();

            await udp.SendAsync(
                responseBytes,
                remoteEndPoint,
                cancellationToken);

            succeeded = true;
            outcome = "success";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException exception)
        {
            outcome = "socket-error";

            logger.LogDebug(
                exception,
                "UDP Daytime send failed for {Remote}.",
                remoteEndPoint);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unhandled UDP Daytime error for {Remote}.",
                remoteEndPoint);
        }
        finally
        {
            stopwatch.Stop();
        }

        if (TimeConnectionHandler.IsIgnoredTelemetrySource(
                remoteEndPoint,
                options.Value.TelemetryIgnoredRemoteAddresses))
        {
            return;
        }

        _ = PublishTelemetryAsync(
            remoteEndPoint.ToString(),
            "TODO",
            stopwatch.ElapsedMilliseconds,
            outcome,
            succeeded,
            occurredAt,
            correlationId,
            cancellationToken);
    }

    private async Task PublishTelemetryAsync(
        string remote,
        string response,
        long durationMilliseconds,
        string outcome,
        bool succeeded,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            // TODO: Mission Control
            //bool published =
            //    await missionControlClient.TryPublishAsync(
            //        eventType: DaytimeRequestCompletedEvent.EventName,
            //        payload: new DaytimeRequestCompletedEvent(
            //            Remote: remote,
            //            Response: response,
            //            DurationMilliseconds: durationMilliseconds,
            //            Outcome: outcome,
            //            Succeeded: succeeded,
            //            Protocol: DaytimeRequestCompletedEvent.UdpProtocol),
            //        payloadTypeInfo:
            //            HappyDaytimeJsonContext.Default
            //                .DaytimeRequestCompletedEvent,
            //        occurredAt: occurredAt,
            //        correlationId: correlationId,
            //        cancellationToken: timeout.Token);

            //if (!published)
            //{
            //    logger.LogWarning(
            //        "Mission Control did not accept UDP Daytime telemetry for {Remote}.",
            //        remote);
            //}
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "UDP Time telemetry publishing stopped for {Remote}.",
                remote);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "UDP Time telemetry publishing timed out for {Remote}.",
                remote);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish UDP Time telemetry for {Remote}.",
                remote);
        }
    }

    private static UdpClient CreateUdpClient(
        IPAddress address,
        int port,
        bool dualMode)
    {
        if (dualMode &&
            !address.Equals(IPAddress.IPv6Any))
        {
            throw new InvalidOperationException("UDP dual mode requires the UDP listen address to be the IPv6 wildcard address '::'.");
        }

        var udp = new UdpClient(address.AddressFamily);

        if (address.AddressFamily ==
            AddressFamily.InterNetworkV6)
        {
            udp.Client.DualMode = dualMode;
        }

        udp.Client.Bind(new IPEndPoint(address, port));

        return udp;
    }
}
