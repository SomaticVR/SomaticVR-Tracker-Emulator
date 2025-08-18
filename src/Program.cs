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
using System.Threading.Tasks;

namespace SomaticVR.TrackerEmulator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting SomaticVR Tracker Emulator...");
            uint trackerCount = 10;
            uint sensorsPerTracker = 1;
            if (args.Length > 0)
            {
                if (!uint.TryParse(args[0], out trackerCount))
                {
                    Console.WriteLine($"Invalid tracker count '{args[0]}', using default: 10");
                    trackerCount = 10;
                }
            }
            if (args.Length > 1)
            {
                if (!uint.TryParse(args[1], out sensorsPerTracker))
                {
                    Console.WriteLine($"Invalid sensors per tracker '{args[1]}', using default: 1");
                    sensorsPerTracker = 1;
                }
            }
            Console.WriteLine($"Tracker count: {trackerCount}, Sensors per tracker: {sensorsPerTracker}");
            var manager = new TrackerManager(trackerCount, sensorsPerTracker);
            await manager.StartAsync();
        }
    }
}
