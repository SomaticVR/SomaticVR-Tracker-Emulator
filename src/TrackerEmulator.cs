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
    public class TrackerEmulator
    {
        private DateTime _lastPacket = DateTime.MinValue;
        private CancellationTokenSource? _timeoutMonitorCts;
        private CancellationTokenSource? _listenCts;
        private readonly uint _trackerIndex;
        private readonly byte _trackerType = 0; // TRACKER_TYPE_SVR_ROTATION
        private readonly uint _numSensors;
        private readonly List<SensorEmulator> _sensors = new();
        private readonly UdpClient _udpClient;
        private IPEndPoint? _serverEndpoint;
        private readonly byte[] _macAddress;
        private Int64 _packetNumber = 0; // Incremented for each packet sent
        private const int BroadcastPort = 6969; // Default SlimeVR UDP port
        private bool _receivedServerFeatureFlags = false;


        public TrackerEmulator(uint trackerIndex, uint numSensors)
        {
            _trackerIndex = trackerIndex;
            _numSensors = numSensors;
            _udpClient = new UdpClient(0) { EnableBroadcast = true }; // Bind to any available local port
            _macAddress = GenerateMacAddress(trackerIndex);
            for (byte i = 0; i < _numSensors; i++)
            {
                var sensor = new SensorEmulator(i);
                _sensors.Add(sensor);
            }
        }

        public async Task StartAsync()
        {
            var cts = new CancellationTokenSource();
            while (true)
            {
                // Ensure any previous background tasks are cancelled and state is reset
                _timeoutMonitorCts?.Cancel();
                _timeoutMonitorCts?.Dispose();
                _timeoutMonitorCts = null;
                _listenCts?.Cancel();
                _listenCts?.Dispose();
                _listenCts = null;
                _serverEndpoint = null;
                _receivedServerFeatureFlags = false;

                bool foundServer = await TryFindServerAsync(cts.Token);
                if (!foundServer)
                {
                    Console.WriteLine($"Tracker {_trackerIndex}: Failed to find server after 30 seconds. Exiting.");
                    Environment.Exit(1);
                }

                // Send Sensor Info packet
                foreach (var sensor in _sensors)
                {
                    var sensorInfoPacket = PacketBuilder.BuildSensorInfoPacket(_packetNumber++, sensor);
                    if (_serverEndpoint != null)
                    {
                        await _udpClient.SendAsync(sensorInfoPacket, sensorInfoPacket.Length, _serverEndpoint);
                    }
                }

                // Start listening for server packets with a new CTS
                _listenCts = new CancellationTokenSource();
                _ = ListenForServerAsync(_listenCts.Token);

                // Start heartbeat monitor
                _timeoutMonitorCts = new CancellationTokenSource();
                var monitorTask = MonitorPacketTimeAsync(_timeoutMonitorCts.Token);

                // Start sending rotation data
                while (_serverEndpoint != null)
                {
                    await SendRotationAsync();
                    await Task.Delay(10); // 100Hz
                }

                // If we get here, heartbeat timed out, so restart server discovery
                Console.WriteLine($"Tracker {_trackerIndex}: Heartbeat timeout. Restarting server discovery.");
            }
        }

        // Attempts to find the server, retrying every second for up to 30 seconds
        async Task<bool> TryFindServerAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= 30; attempt++)
            {
                Console.WriteLine($"Tracker {_trackerIndex}: Attempt {attempt}/30 to find server...");
                await SendHandshakeAsync();
                var found = await WaitForServerAsync(1000, cancellationToken);
                if (found)
                {
                    return true;
                }
            }
            return false;
        }

        // Waits for a server response for a given timeout
        async Task<bool> WaitForServerAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            var task = _udpClient.ReceiveAsync();
            var delayTask = Task.Delay(timeoutMs, cancellationToken);
            var completed = await Task.WhenAny(task, delayTask);
            if (completed == task && !cancellationToken.IsCancellationRequested)
            {
                var result = task.Result;
                if (_serverEndpoint == null)
                {
                    _serverEndpoint = result.RemoteEndPoint;
                    Console.WriteLine($"Tracker {_trackerIndex}: Server discovered at {_serverEndpoint}");
                    ProcessIncomingPacket(result.Buffer, _serverEndpoint);
                }
                return true;
            }
            return false;
        }

        async Task SendHandshakeAsync()
        {
            var packet = PacketBuilder.BuildHandshakePacket(_macAddress);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
            // Handshake is always broadcast
            await _udpClient.SendAsync(packet, packet.Length, broadcastEndpoint);
        }

        async Task ListenForServerAsync(CancellationToken token)
        {
            _lastPacket = DateTime.UtcNow;
            while (!token.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync();
                if (_serverEndpoint == null)
                {
                    _serverEndpoint = result.RemoteEndPoint;
                    Console.WriteLine($"Tracker {_trackerIndex}: Server discovered at {_serverEndpoint}");
                }
                // Process all incoming packets
                ProcessIncomingPacket(result.Buffer, result.RemoteEndPoint);

                await Task.Delay(100, token);
                if ((DateTime.UtcNow - _lastPacket).TotalSeconds > 1)
                {
                    // Timeout: clear server endpoint to break out of rotation loop
                    _serverEndpoint = null;
                    break;
                }
            }
        }

        async Task SendFeatureFlagsPacketAsync()
        {
            int attempts = 0;
            while (!_receivedServerFeatureFlags && attempts < 15)
            {
                if (_serverEndpoint != null)
                {
                    var packet = PacketBuilder.BuildFeatureFlagsPacket();
                    await _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);
                }
                attempts++;
                await Task.Delay(100);
            }
        }

        // Template for processing incoming UDP packets
        void ProcessIncomingPacket(byte[] data, IPEndPoint remote)
        {
            if (data == null || data.Length < 4)
                return;
            // Packet ID is first 4 bytes (big-endian) or first byte is 0x03 for handshake response
            _lastPacket = DateTime.UtcNow;
            uint packetId = 0;
            if (data[0] == 0x03) // Handshake response
                packetId = 0x03;
            else 
                packetId = (uint) BitConverter.ToInt32(data.Take(4).Reverse().ToArray(), 0);
            // Example switch for packet types
            byte[]? packet = null;
            switch (packetId)
            {
                case 1: // Heartbeat
                    // Console.WriteLine($"Received Heartbeat from {remote}");
                    packet = PacketBuilder.BuildHeartbeatPacket();
                    break;
                case 3: // Handshake response
                    Console.WriteLine($"Received Handshake response from {remote}");
                    // Start sending feature flags as a background task
                    _ = SendFeatureFlagsPacketAsync();
                    break;
                case 10: // Ping/Pong
                    // Console.WriteLine($"Received Ping/Pong from {remote}");
                    packet = PacketBuilder.BuildPingPongPacket(data);
                    break;
                case 15: // Sensor Info ACK
                    Console.WriteLine($"Received Sensor Info ACK from {remote}");
                    // SensorInfoPacket sensorInfoPacket;
                    // memcpy(&sensorInfoPacket, m_Packet + 4, sizeof(sensorInfoPacket));

                    // for (int i = 0; i < (int)sensors.size(); i++) {
                    //     if (sensorInfoPacket.sensorId == sensors[i]->getSensorId()) {
                    //         m_AckedSensorState[i] = sensorInfoPacket.sensorState;
                    //         if (len < 12) {
                    //             m_AckedSensorCalibration[i]
                    //                 = sensors[i]->hasCompletedRestCalibration();
                    //             m_AckedSensorConfigData[i] = sensors[i]->getSensorConfigData();
                    //             break;
                    //         }
                    //         m_AckedSensorCalibration[i]
                    //             = sensorInfoPacket.hasCompletedRestCalibration;
                    //         break;
                    //     }
                    break;
                case 22:
                    // Server feature flags
                    _receivedServerFeatureFlags = true; // End SendFeatureFlagsPacketAsync early
                    break;
                case 24: // Ack Config Change
                    Console.WriteLine($"Received Ack Config Change from {remote}");
                    // TODO: Handle config change ack
                    break;
                // Add more cases for other packet types as needed
                default:
                    Console.WriteLine($"Received unknown packet ID {packetId} from {remote}");
                    Console.WriteLine($"Received packet {BitConverter.ToString(data)}");
                    break;
            }
            // If we have a packet to send back, send it to the server endpoint
            if (packet != null && _serverEndpoint != null)
            {
                _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);
            }
        }

        async Task SendRotationAsync()
        {
            foreach (var sensor in _sensors)
            {
                sensor.UpdateRotationAsync(); // Update each sensor's rotation
                // Console.WriteLine($"Tracker {_trackerIndex}: Sending rotation packet... {sensor.rotation}");
                var packet = PacketBuilder.BuildRotationPacket(sensor.index, _packetNumber++, sensor.rotation);
                // All non-handshake packets are unicast to the discovered server endpoint
                if (_serverEndpoint != null)
                {
                    await _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);
                }
                packet = PacketBuilder.BuildAccelerationPacket(sensor.index, _packetNumber++, sensor.acceleration);
                // All non-handshake packets are unicast to the discovered server endpoint
                if (_serverEndpoint != null)
                {
                    await _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);
                }                
            }
        }

        static byte[] GenerateMacAddress(uint index)
        {
            // 02:00:00:00:00:XX (locally administered)
            return new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, (byte)index };
        }

        // Monitors for packet timeout and triggers rediscovery if no packets are received for 10 seconds
        private async Task MonitorPacketTimeAsync(CancellationToken token)
        {
            _lastPacket = DateTime.UtcNow;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
                if ((DateTime.UtcNow - _lastPacket).TotalSeconds > 1)
                {
                    // Timeout: clear server endpoint to break out of rotation loop
                    _serverEndpoint = null;
                    break;
                }
            }
        }
    }
}
