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
            Console.WriteLine("Starting SomaticVR Tracker Emulator...");

            var manager = new TrackerManager();

            // Initial state
            EmulationStage currentStage = EmulationStage.InfoPacket;

            while (true)
            {
                switch (currentStage)
                {
                    case EmulationStage.InfoPacket:
                        await RunInfoPacketStage(manager);
                        currentStage = EmulationStage.FullReset;
                        break;

                    case EmulationStage.FullReset:
                        await RunUserTriggeredStage(
                            manager,
                            EmulationStage.FullReset,
                            "Please start the FULL RESET in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.ResetMounting;
                        break;

                    case EmulationStage.ResetMounting:
                        await RunUserTriggeredStage(
                            manager,
                            EmulationStage.ResetMounting,
                            "Please start the RESET MOUNTING in the SomaticVR GUI then press SPACE when complete."
                        );
                        currentStage = EmulationStage.SendData;
                        break;

                    case EmulationStage.SendData:
                        await RunUserTriggeredStage(
                            manager,
                            EmulationStage.SendData,
                            "Sending Packet data to the SomaticVR GUI, press SPACE when complete."
                        );
                        return; // Finished all stages
                }
            }
        }

        // --- STAGE HANDLERS ----------------------------------------------------

        static async Task RunInfoPacketStage(TrackerManager manager)
        {
            try
            {
                Console.WriteLine("Sending SensorInfo packets...");
                await manager.SendInfoPacketsAsync();
                Console.WriteLine("All trackers acknowledged SensorInfo.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending info packets: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static async Task RunUserTriggeredStage(
            TrackerManager manager,
            EmulationStage stage,
            string userPrompt)
        {
            Console.WriteLine();
            Console.WriteLine(userPrompt);
            Console.WriteLine();

            var cts = new CancellationTokenSource();
            var task = manager.SendDataPacketsAsync(stage, cts.Token);

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
                Console.ReadKey(intercept: true);

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
