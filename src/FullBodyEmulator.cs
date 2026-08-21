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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SomaticVR.TrackerEmulator
{

    public sealed class FullBodyEmulator : IAsyncDisposable
    {
        private readonly BridgeManager BridgeManager;
        private readonly TrackerManager TrackerManager;

        public FullBodyEmulator()
        {
            BridgeManager = new BridgeManager();
            TrackerManager = new TrackerManager();
        }

        public async Task ConnectBridgeAsync(int timeoutMs = 10_000)
        {
            await BridgeManager.ConnectAsync(timeoutMs);
        }

        public async Task StartBridgeHandshakeAsync()
        {
            await BridgeManager.StartHandshakeAsync();
        }

        public async Task SendDeviceInfoPacketsAsync()
        {
            await TrackerManager.SendInfoPacketsAsync();
        }

        public async Task SendBridgeDevicesPositionsLoopAsync(CancellationToken cancellationToken = default)
        {
            await BridgeManager.SendBridgeDevicesPositionsLoopAsync(cancellationToken);
        }
    
        public async Task SendDataPacketsAsync(
            EmulationStage stage,
            CancellationToken cancellationToken = default)
        {
            string fileName = stage switch
            {
                EmulationStage.FullReset           => "data\\FullResetStance.jsonl",
                EmulationStage.ResetMounting       => "data\\MountingResetStance.jsonl",
                EmulationStage.FootMounting        => "data\\FootMountingResetStance.jsonl",
                EmulationStage.StandingStayAligned => "data\\FullResetStance.jsonl",
                EmulationStage.ChairStayAligned    => "data\\StayAlignedChair.jsonl",
                EmulationStage.FloorStayAligned    => "data\\StayAlignedFloor.jsonl",
                EmulationStage.SendData            => "data\\RotationAccelerationData.jsonl",
                _ => throw new ArgumentException("Invalid setup stage", nameof(stage))
            };

            // Your measured frequency: 13200 writes / 10 seconds = 1320 Hz
            double hz = 13200.0 / 10.0;
            long ticksPerPacket = (long)(Stopwatch.Frequency / hz);

            var stopwatch = Stopwatch.StartNew();
            long nextSendTicks = stopwatch.ElapsedTicks;

            await foreach (var line in ReadLinesLoopingAsync(fileName, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;                    
                }

                var obj = JsonSerializer.Deserialize<PacketLine>(line);
                if (obj.device is null || obj.data is null)
                {
                    continue;                    
                }

                // Wait until nextSendTicks
                if (stopwatch.ElapsedTicks < nextSendTicks)
                {
                    double remainingMs =
                        (nextSendTicks - stopwatch.ElapsedTicks) * 1000.0 / Stopwatch.Frequency;

                    if (remainingMs > 2.0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 1.0), cancellationToken);                        
                    }

                    while (stopwatch.ElapsedTicks < nextSendTicks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Thread.SpinWait(10);
                    }
                }

                // Send packet
                byte[] bytesData = Convert.FromBase64String(obj.data);

                if (TrackerManager._trackerLookup.TryGetValue(obj.device, out int trackerIndex))
                {
                    await TrackerManager.SendAsync(trackerIndex, bytesData);                    
                }
                else
                {
                    await BridgeManager.SendAsync(bytesData);                    
                }

                // Schedule next send time
                nextSendTicks += ticksPerPacket;
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

        public async ValueTask DisposeAsync()
        {
            // Dispose managers in reverse dependency order
            if (TrackerManager is IAsyncDisposable asyncTrackers)
            {
                await asyncTrackers.DisposeAsync();                
            }

            if (BridgeManager is IAsyncDisposable asyncBridge)
            {
                await asyncBridge.DisposeAsync();                
            }
        }
    }
}    

