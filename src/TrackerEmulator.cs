/*
    SomaticVR Code is placed under the MIT license
    Copyright (c) 2025 Somatic VR, LLC

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SomaticVR.TrackerEmulator
{
    public class TrackerEmulator : IAsyncDisposable
    {
        private readonly uint _trackerIndex;
        // private readonly byte _trackerType = 0; // TRACKER_TYPE_SVR_ROTATION
        private readonly uint _numSensors;
        private readonly List<SensorEmulator> _sensors = new();
        private readonly UdpClient _udpClient;
        private IPEndPoint? _serverEndpoint;
        private readonly byte[] _macAddress;
        public readonly string macAddressString;
        public bool SensorInfoAckReceived { get; private set; } = false;

        private Int64 _packetNumber = 0; // Incremented for each packet sent
        private const int BroadcastPort = 6969; // Default SlimeVR UDP port


        private readonly CancellationTokenSource _cts = new();
        private Task _receiveTask = Task.CompletedTask;

        public TrackerEmulator(uint trackerIndex, uint numSensors, byte[] macAddress)
        {
            _trackerIndex = trackerIndex;
            _numSensors = numSensors;
            _udpClient = new UdpClient(0) { EnableBroadcast = true }; // Bind to any available local port
            _macAddress = macAddress;
            macAddressString = BitConverter.ToString(macAddress);
            for (byte i = 0; i < _numSensors; i++)
            {
                var sensor = new SensorEmulator(i);
                _sensors.Add(sensor);
            }

            Console.WriteLine(_udpClient.GetType().FullName);
        }

        public async Task StartAsync()
        {
            // Start background listener
            _receiveTask = ListenForServerAsync(_cts.Token);
        }

        public async Task SendInfoPacketAsync()
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (!await TryFindServerAsync(timeoutCts.Token))
            {
                throw new Exception($"Tracker {_trackerIndex}: Failed to find server after 10 seconds.");                
            }

            foreach (var sensor in _sensors)
            {
                var packet = PacketBuilder.BuildSensorInfoPacket(_packetNumber++, sensor);
                if (_serverEndpoint != null)
                {
                    await _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);                    
                }
            }
        }

        public async Task SendDataPacketAsync(byte[] packetData)
        {
            if (_serverEndpoint == null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (!await TryFindServerAsync(timeoutCts.Token))
                {
                    throw new Exception($"Tracker {_trackerIndex}: Failed to find server after 10 seconds.");                    
                }
            }

            if (_serverEndpoint != null)
            {
                await _udpClient.SendAsync(packetData, packetData.Length, _serverEndpoint);                
            }
        }

        // Attempts to find the server, retrying every second for up to 30 seconds
        private async Task<bool> TryFindServerAsync(CancellationToken ct)
        {
            for (int attempt = 1; attempt <= 30; attempt++)
            {
                await SendHandshakeAsync();
                if (await WaitForServerAsync(1000, ct))
                {
                    return true;                    
                }
            }
            return false;
        }


        // Waits for a server response for a given timeout
        private async Task<bool> WaitForServerAsync(int timeoutMs, CancellationToken ct)
        {
            var receiveTask = _udpClient.ReceiveAsync(ct).AsTask();
            var timeoutTask = Task.Delay(timeoutMs, ct);

            var completed = await Task.WhenAny(receiveTask, timeoutTask);

            if (completed == receiveTask && !ct.IsCancellationRequested)
            {
                var result = await receiveTask;

                if (_serverEndpoint == null)
                {
                    _serverEndpoint = result.RemoteEndPoint;
                    ProcessIncomingPacket(result.Buffer, _serverEndpoint);
                }

                return true;
            }
            return false;
        }

        private async Task SendHandshakeAsync()
        {
            var packet = PacketBuilder.BuildHandshakePacket(_macAddress);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
            await _udpClient.SendAsync(packet, packet.Length, broadcastEndpoint);
        }

        private async Task ListenForServerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(ct);

                    if (_serverEndpoint == null)
                    {
                        _serverEndpoint = result.RemoteEndPoint;                        
                    }

                    ProcessIncomingPacket(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (ObjectDisposedException)
            {
                // Socket disposed during shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tracker {_trackerIndex}: Receive loop error: {ex.Message}");
            }
        }

        private void ProcessIncomingPacket(byte[] data, IPEndPoint remote)
        {
            if (data.Length < 4)
            {
                return;
            }

            uint packetId =
                data[0] == 0x03
                ? 0x03
                : (uint)BitConverter.ToInt32(data.Take(4).Reverse().ToArray(), 0);

            byte[]? response = packetId switch
            {
                1 => PacketBuilder.BuildHeartbeatPacket(),
                10 => PacketBuilder.BuildPingPongPacket(data),
                15 => HandleSensorInfoAck(remote),
                _ => null
            };

            if (response != null && _serverEndpoint != null)
            {
                _udpClient.SendAsync(response, response.Length, _serverEndpoint);                
            }
        }

        private byte[]? HandleSensorInfoAck(IPEndPoint remote)
        {
            Console.WriteLine($"Received Sensor Info ACK from {remote}");
            SensorInfoAckReceived = true;
            return null;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            try { await _receiveTask.ConfigureAwait(false); }
            catch { }

            _udpClient.Dispose();
            _cts.Dispose();
        }
    }
}
