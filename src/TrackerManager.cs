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

    public class TrackerManager : IAsyncDisposable
    {
        private readonly List<TrackerEmulator> _trackers = new();
        private readonly uint _trackerCount;
        private readonly uint _numSensorsPerTracker = 1; // Default to 1 sensor per tracker

        public readonly Dictionary<string, int> _trackerLookup = new();
        
        private List<string> _macAddressStrings = new List<string>();
       
        public TrackerManager()
        {
            string deviceListFileName = "EmulationData\\DeviceList.jsonl";
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
            Console.WriteLine($"Tracker Count: {_trackerCount}");
            for (uint i = 0; i < _trackerCount; i++)
            {
                Console.WriteLine($"[Emulator] Creating TrackerEmulator for Tracker {i} with MAC {_macAddressStrings[(int)i]}...");
                // byte[] macAddressBytes = Convert.FromBase64String(_macAddressStrings[(int)i]);
                byte[] macAddressBytes = _macAddressStrings[(int)i].Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();

                var tracker = new TrackerEmulator(i, _numSensorsPerTracker, macAddressBytes);

                await tracker.StartAsync();

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
                    {
                        throw new TimeoutException($"Tracker {i} did not receive SensorInfo ACK in time.");
                    }

                    await Task.Delay(100);
                }
            }
        }

        public async Task SendAsync(int trackerIndex, byte[] data)
        {
            if (trackerIndex < 0 || trackerIndex >= _trackers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(trackerIndex), "Invalid tracker index.");                
            }

            await _trackers[trackerIndex].SendDataPacketAsync(data);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var tracker in _trackers)
            {
                await tracker.DisposeAsync();
            }
        }
    }
}
