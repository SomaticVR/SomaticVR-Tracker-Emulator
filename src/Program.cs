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
using System.Threading;
using System.Threading.Tasks;

namespace SomaticVR.TrackerEmulator
{
    class Program
    {

        static async Task Main(string[] args)
        {
            Console.WriteLine("SomaticVR Full Body Emulator");
            Console.WriteLine("Make sure the SomaticVR server is running before proceeding.\n");

            await using var emulator = new FullBodyEmulator();

            var currentStage = EmulationStage.BridgeConnection;
            CancellationTokenSource? statusLoopCts = null;
            Task? statusLoopTask = null;

            while (true)
            {
                switch (currentStage)
                {
                    case EmulationStage.BridgeConnection:
                        await RunBridgeConnectionStage(emulator);
                        currentStage = EmulationStage.BridgeDeviceHandshake;
                        break;

                    case EmulationStage.BridgeDeviceHandshake:
                        await RunHandshakeStage(emulator);
                        statusLoopCts = new CancellationTokenSource();
                        statusLoopTask = emulator.SendBridgeDevicesPositionsLoopAsync(statusLoopCts.Token);
                        currentStage = EmulationStage.DeviceInfoPacket;
                        break;

                    case EmulationStage.DeviceInfoPacket:
                        Console.WriteLine("[Stage] Sending device info packet...");
                        await RunInfoPacketStage(emulator);
                        currentStage = EmulationStage.FullReset;
                        break;

                    case EmulationStage.FullReset:
                        statusLoopCts?.Cancel();
                        try { if (statusLoopTask != null) await statusLoopTask; }
                        catch (OperationCanceledException) { }
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.FullReset,
                            "Please start the FULL RESET in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.ResetMounting;
                        break;

                    case EmulationStage.ResetMounting:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.ResetMounting,
                            "Please start the RESET MOUNTING in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.FootMounting;
                        break;
                        
                    case EmulationStage.FootMounting:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.FootMounting,
                            "Please start the FOOT MOUNTING in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.StandingStayAligned;
                        break;

                    case EmulationStage.StandingStayAligned:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.StandingStayAligned,
                            "Please start the STANDING STAY ALIGNED in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.ChairStayAligned;
                        break;

                    case EmulationStage.ChairStayAligned:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.ChairStayAligned,
                            "Please start the CHAIR STAY ALIGNED in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.FloorStayAligned;
                        break;

                    case EmulationStage.FloorStayAligned:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.FloorStayAligned,
                            "Please start the FLOOR STAY ALIGNED in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.SendData;
                        break;

                    case EmulationStage.SendData:
                        await RunUserTriggeredStage(
                            emulator,
                            EmulationStage.SendData,
                            "Sending Packet data to the SomaticVR GUI, press SPACE when complete."
                        );
                        return; // Finished all stages
                }
            }
        }

        // --- STAGE HANDLERS ----------------------------------------------------
        static async Task RunBridgeConnectionStage(FullBodyEmulator emulator)
        {
            Console.WriteLine("[Stage] Connecting to server bridge...");
            await emulator.ConnectBridgeAsync();
        }

        static async Task RunHandshakeStage(FullBodyEmulator emulator)
        {
            Console.WriteLine("[Stage] Starting handshake...");
            await emulator.StartBridgeHandshakeAsync();
            // Give the server a moment to process the tracker registrations
            await Task.Delay(300);
        }

        static async Task RunInfoPacketStage(FullBodyEmulator emulator)
        {
            try
            {
                Console.WriteLine("Sending SensorInfo packets...");
                await emulator.SendDeviceInfoPacketsAsync();
                Console.WriteLine("All trackers acknowledged SensorInfo.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending info packets: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static async Task RunUserTriggeredStage(
            FullBodyEmulator emulator,
            EmulationStage stage,
            string userPrompt)
        {
            Console.WriteLine();
            Console.WriteLine(userPrompt);
            Console.WriteLine();

            var cts = new CancellationTokenSource();
            var task = emulator.SendDataPacketsAsync(stage, cts.Token);

            while (!task.IsCompleted)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Spacebar)
                    {
                        Console.WriteLine($"{stage} packets cancelled by user.");
                        cts.Cancel();
                        break;
                    }
                }

                await Task.Delay(50);
            }

            // Flush input buffer so next stage doesn't auto-cancel
            while (Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);                
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"{stage} stage cancelled.");
            }
        }
    }
}
