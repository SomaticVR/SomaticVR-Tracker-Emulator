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
using System.Collections.Generic;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Runtime.CompilerServices;
using SomaticVR.TrackerEmulator;


namespace SomaticVR.TrackerEmulator
{
    public struct PacketLine
    {
        public string device { get; set; }
        public string data { get; set; }
    }

    public class TrackerManager
    {
        private const double TargetHzPerDevice = 100.0;

        private readonly List<TrackerEmulator> _trackers = new();
        private readonly uint _trackerCount;
        private readonly uint _numSensorsPerTracker = 1; // Default to 1 sensor per tracker

        private readonly Dictionary<string, int> _trackerLookup = new();
        
        private List<string> _macAddressStrings = new List<string>();

        public TrackerManager()
        {
            string deviceListFileName = "DeviceList.jsonl";
            if (File.Exists(deviceListFileName))
            {
                foreach (var line in File.ReadLines(deviceListFileName))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var obj = JsonSerializer.Deserialize<PacketLine>(line);
                        _macAddressStrings.Add(obj.device);
                    }
                }
            }
            else
            {
                throw new FileNotFoundException($"Device list file '{deviceListFileName}' not found.");
            }

            _trackerCount = (uint)_macAddressStrings.Count;
        }

        public async Task SendInfoPacketsAsync()
        {
            for (uint i = 0; i < _trackerCount; i++)
            {
                // byte[] macAddressBytes = Convert.FromBase64String(_macAddressStrings[(int)i]);
                byte[] macAddressBytes = _macAddressStrings[(int)i].Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();

                var tracker = new TrackerEmulator(i, _numSensorsPerTracker, macAddressBytes);

                // mac address string with semicolons replacing the dashes
                // put them in the dictionary for lookup later
                _trackerLookup[_macAddressStrings[(int)i]] = (int)i;
                _trackers.Add(tracker);

                // Send SensorInfo packet(s)
                Console.WriteLine($"Sending SensorInfo packet for Tracker {i}...");
                await tracker.SendInfoPacketAsync();

                // Wait for THIS tracker’s ACK
                var sw = Stopwatch.StartNew();
                const int timeoutMs = 20000; // 20 seconds per tracker

                while (!tracker.SensorInfoAckReceived)
                {
                    if (sw.ElapsedMilliseconds > timeoutMs)
                        throw new TimeoutException($"Tracker {i} did not receive SensorInfo ACK in time.");

                    await Task.Delay(100);
                }
            }
        }

        public async Task SendDataPacketsAsync(
            EmulationStage stage,
            CancellationToken cancellationToken = default)
        {
            string fileName = stage switch
            {
                EmulationStage.FullReset      => "FullResetStance.jsonl",
                EmulationStage.ResetMounting  => "MountingResetStance.jsonl",
                EmulationStage.SendData       => "RotationAccelerationData.jsonl",
                _ => throw new ArgumentException("Invalid setup stage", nameof(stage))
            };

            long ticksPerDevicePacket = (long)Math.Round(Stopwatch.Frequency / TargetHzPerDevice);
            var scheduler = Stopwatch.StartNew();
            var nextSendTicksByTracker = new Dictionary<int, long>();

            await foreach (var line in ReadLinesLoopingAsync(fileName, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var obj = JsonSerializer.Deserialize<PacketLine>(line);
                if (obj.device is null || obj.data is null)
                    continue;

                if (!_trackerLookup.TryGetValue(obj.device, out int trackerIndex))
                    continue;

                if (!nextSendTicksByTracker.TryGetValue(trackerIndex, out long nextSendTicks))
                    nextSendTicks = scheduler.ElapsedTicks;

                // Task.Delay has ~15ms minimum resolution on Windows, so spin for short waits
                if (scheduler.ElapsedTicks < nextSendTicks)
                {
                    double remainingMs = (nextSendTicks - scheduler.ElapsedTicks) * 1000.0 / Stopwatch.Frequency;
                    if (remainingMs > 20)
                        await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 15), cancellationToken);

                    while (scheduler.ElapsedTicks < nextSendTicks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Thread.SpinWait(10);
                    }
                }

                byte[] bytesData = Convert.FromBase64String(obj.data);
                await _trackers[trackerIndex].SendDataPacketAsync(bytesData);

                long baseTicks = Math.Max(nextSendTicks, scheduler.ElapsedTicks);
                nextSendTicksByTracker[trackerIndex] = baseTicks + ticksPerDevicePacket;
            }
        }        

        async IAsyncEnumerable<string> ReadLinesLoopingAsync(
            string path,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            while (true) // infinite loop
            {
                await foreach (var line in ReadLinesAsync(path, token))
                {
                    token.ThrowIfCancellationRequested();
                    yield return line;
                }

                // When file ends, loop restarts automatically
            }
        }

        async IAsyncEnumerable<string> ReadLinesAsync(
            string path, 
            [EnumeratorCancellation] CancellationToken token = default)
        {
            using var reader = new StreamReader(path);

            while (!reader.EndOfStream)
            {
                token.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (line is not null)
                {
                    yield return line;
                }
            }
        }
    }
}
