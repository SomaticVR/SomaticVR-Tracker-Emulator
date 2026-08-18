/*
    SomaticVR Code is placed under the MIT license
    Copyright (c) 2026 Somatic VR, LLC
*/

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Messages;

namespace SomaticVR.TrackerEmulator
{
    /// <summary>
    /// Named pipe client that connects to the server's NamedPipeBridge.
    /// Uses the same 4-byte length-prefix framing as the server.
    /// </summary>
    public sealed class BridgeClient : IAsyncDisposable
    {
        public const string PipeName = "SlimeVRDriver";

        private readonly NamedPipeClientStream _pipe;
        private readonly byte[] _sendBuffer = new byte[4096];
        private readonly byte[] _receiveBuffer = new byte[4096];
        private readonly CancellationTokenSource _cts = new();
        private Task _receiveTask = Task.CompletedTask;

        public bool IsConnected => _pipe.IsConnected;

        public event Action<ProtobufMessage>? MessageReceived;

        public BridgeClient()
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        }

        public async Task ConnectAsync(int timeoutMs = 10_000)
        {
            Console.WriteLine($"[Bridge] Connecting to pipe '{PipeName}'...");
            await _pipe.ConnectAsync(timeoutMs).ConfigureAwait(false);
            Console.WriteLine("[Bridge] Connected.");
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        public async Task SendAsync(byte[] protoBytes, CancellationToken ct = default)
        {
            await _pipe.WriteAsync(protoBytes.AsMemory(0, protoBytes.Length), ct).ConfigureAwait(false);
            await _pipe.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Frames and sends one protobuf message: [4-byte totalSize][proto bytes].</summary>
        public async Task SendAsync(ProtobufMessage message, CancellationToken ct = default)
        {
            int protoSize = message.CalculateSize();
            int totalSize = protoSize + 4;

            BitConverter.TryWriteBytes(_sendBuffer.AsSpan(0, 4), totalSize);

            using (var ms = new MemoryStream(_sendBuffer, 4, protoSize, writable: true))
            using (var cos = new CodedOutputStream(ms, leaveOpen: true))
            {
                message.WriteTo(cos);
                cos.Flush();
            }

            await _pipe.WriteAsync(_sendBuffer.AsMemory(0, totalSize), ct).ConfigureAwait(false);
            await _pipe.FlushAsync(ct).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            Console.WriteLine("[Bridge] Receive loop started.");
            try
            {
                while (_pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    int headerRead = await ReadExactlyAsync(_pipe, _receiveBuffer, 0, 4, ct).ConfigureAwait(false);
                    if (headerRead != 4)
                    {
                        break;                        
                    }

                    int totalSize = BitConverter.ToInt32(_receiveBuffer, 0);
                    int protoLength = totalSize - 4;

                    if (protoLength < 0 || protoLength > _receiveBuffer.Length - 4)
                    {
                        Console.WriteLine($"[Bridge] Invalid message length: {totalSize}");
                        break;
                    }

                    if (protoLength > 0)
                    {
                        int payloadRead = await ReadExactlyAsync(_pipe, _receiveBuffer, 4, protoLength, ct).ConfigureAwait(false);
                        if (payloadRead != protoLength)
                        {
                            break;                            
                        }
                    }

                    var message = ProtobufMessage.Parser.ParseFrom(_receiveBuffer, 4, protoLength);
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) { Console.WriteLine($"[Bridge] Pipe read error: {ex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[Bridge] Receive error: {ex.Message}"); }

            Console.WriteLine("[Bridge] Disconnected.");
        }

        private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (n == 0)
                    break;
                totalRead += n;
            }
            return totalRead;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _receiveTask.ConfigureAwait(false); } catch { }
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
        }
    }
}
