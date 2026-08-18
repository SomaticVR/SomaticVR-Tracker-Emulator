/*
    SomaticVR Code is placed under the MIT license
    Copyright (c) 2026 Somatic VR, LLC
*/

using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Messages;
using ProtobufVersion = Messages.Version;

namespace SomaticVR.TrackerEmulator
{
    /// <summary>
    /// Emulates the SteamVR driver side of the NamedPipeBridge: registers an HMD and two
    /// controllers then streams position updates so the server can exercise its full pipeline.
    /// </summary>
    public sealed class BridgeManager : IAsyncDisposable
    {
        // TrackerRole int values from solarxr_protocol.datatypes.TrackerRole
        private const int RoleHmd            = 19;
        private const int RoleLeftController  = 13;
        private const int RoleRightController = 14;

        public const int TrackerIdHmd             = 0; // server detects HMD by id == 0
        public const int TrackerIdLeftController  = 9; // Assigned by SteamVR
        public const int TrackerIdRightController = 10; // Assigned by SteamVR

        public readonly List<string> RegisteredTrackerSerials = new()
        {
            "VRLINKHMDQUEST2",
            "VRLINKQ2_Controller_Left",
            "VRLINKQ2_Controller_Right"
        };

        private readonly BridgeClient _client = new();

        public bool IsConnected => _client.IsConnected;

        public event Action<ProtobufMessage>? MessageReceived;

        public BridgeManager()
        {
            _client.MessageReceived += OnMessageReceived;
        }

        public async Task ConnectAsync(int timeoutMs = 10_000) =>
            await _client.ConnectAsync(timeoutMs).ConfigureAwait(false);

        public async Task StartHandshakeAsync()
        {
            Console.WriteLine("[Emulator] Sending handshake...");

            await _client.SendAsync(new ProtobufMessage
            {
                Version = new ProtobufVersion { ProtocolVersion = 1 }
            });

            await _client.SendAsync(new ProtobufMessage
            {
                TrackerAdded = new TrackerAdded
                {
                    TrackerId    = TrackerIdHmd,
                    TrackerName  = "HMD",
                    TrackerSerial = "VRLINKHMDQUEST2",
                    TrackerRole  = RoleHmd,
                    Manufacturer = "SomaticVR Emulator"
                }
            });

            await _client.SendAsync(new ProtobufMessage
            {
                TrackerAdded = new TrackerAdded
                {
                    TrackerId    = TrackerIdLeftController,
                    TrackerName  = "Left Controller",
                    TrackerSerial = "VRLINKQ2_Controller_Left",
                    TrackerRole  = RoleLeftController,
                    Manufacturer = "SomaticVR Emulator"
                }
            });

            await _client.SendAsync(new ProtobufMessage
            {
                TrackerAdded = new TrackerAdded
                {
                    TrackerId    = TrackerIdRightController,
                    TrackerName  = "Right Controller",
                    TrackerSerial = "VRLINKQ2_Controller_Right",
                    TrackerRole  = RoleRightController,
                    Manufacturer = "SomaticVR Emulator"
                }
            });

            foreach (var id in new[] { TrackerIdHmd, TrackerIdLeftController, TrackerIdRightController })
            {
                await _client.SendAsync(new ProtobufMessage
                {
                    TrackerStatus = new TrackerStatus
                    {
                        TrackerId = id,
                        Status    = TrackerStatus.Types.Status.Ok
                    }
                });
            }

            Console.WriteLine("[Emulator] Handshake complete — HMD + 2 controllers registered.");
        }

        public async Task SendAsync (byte[] data, CancellationToken ct = default)
        {
            await _client.SendAsync(data, ct).ConfigureAwait(false);
        }

        // asynchronous task that continuouslly sends tracker status for each of the three devices to keep them alive
        public async Task SendBridgeDevicesPositionsLoopAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                var position = new Vector3(0, 0, 0);
                var rotation = new Quaternion(0, 0, 0, 1);

                foreach (var trackerId in new[] { TrackerIdHmd, TrackerIdLeftController, TrackerIdRightController })
                {
                    await _client.SendAsync(new ProtobufMessage
                    {
                        Position = new Position
                        {
                            TrackerId = trackerId,
                            X = position.X, Y = position.Y, Z = position.Z,
                            Qx = rotation.X, Qy = rotation.Y, Qz = rotation.Z, Qw = rotation.W
                        }
                    }, ct).ConfigureAwait(false);
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        public async Task SendBridgeDevicesPositionAsync(int trackerId, Vector3 position, Quaternion rotation, CancellationToken ct = default)
        {
            await _client.SendAsync(new ProtobufMessage
            {
                Position = new Position
                {
                    TrackerId = trackerId,
                    X = position.X, Y = position.Y, Z = position.Z,
                    Qx = rotation.X, Qy = rotation.Y, Qz = rotation.Z, Qw = rotation.W
                }
            }, ct).ConfigureAwait(false);
        }

        private void OnMessageReceived(ProtobufMessage message)
        {
            MessageReceived?.Invoke(message);

            switch (message.MessageCase)
            {
                case ProtobufMessage.MessageOneofCase.TrackerAdded:
                    Console.WriteLine($"[Emulator] Server registered computed tracker: id={message.TrackerAdded.TrackerId} name='{message.TrackerAdded.TrackerName}'");
                    break;
                case ProtobufMessage.MessageOneofCase.TrackerStatus:
                    Console.WriteLine($"[Emulator] Tracker status update: id={message.TrackerStatus.TrackerId} status={message.TrackerStatus.Status}");
                    break;
                case ProtobufMessage.MessageOneofCase.Version:
                    Console.WriteLine($"[Emulator] Server protocol version: {message.Version.ProtocolVersion}");
                    break;
                case ProtobufMessage.MessageOneofCase.Position:
                    // High-frequency computed tracker updates — log only if needed
                    break;
            }
        }

        public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);
    }
}


