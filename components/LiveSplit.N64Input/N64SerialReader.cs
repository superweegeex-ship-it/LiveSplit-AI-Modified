using RetroSpy.Readers;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace LiveSplit.UI.Components
{
    /// <summary>Reads the newline-delimited N64 packets used by sm64Spy/RetroSpy serial adapters.</summary>
    internal sealed class N64SerialReader : IDisposable
    {
        private readonly SerialPort port;
        private readonly Thread worker;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly List<byte> packetBuffer = new List<byte>(256);
        private volatile bool disposed;
        private bool hasSeenFrameBoundary;

        public event Action<ControllerStateEventArgs> StateChanged;
        public event Action<string> Disconnected;

        public N64SerialReader(string portName)
        {
            port = new SerialPort(portName, 115200)
            {
                Handshake = Handshake.RequestToSend,
                DtrEnable = true,
                ReadTimeout = 100,
                WriteTimeout = 100
            };
            port.Open();
            worker = new Thread(ReadLoop) { IsBackground = true, Name = "LiveSplit N64 serial input" };
            worker.Start();
        }

        private void ReadLoop()
        {
            var bytes = new byte[256];
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    int count = port.BytesToRead;
                    if (count <= 0) { Thread.Sleep(1); continue; }
                    count = Math.Min(count, bytes.Length);
                    int read = port.Read(bytes, 0, count);
                    for (int i = 0; i < read; i++)
                    {
                        byte value = bytes[i];
                        if (value == 0x0A)
                        {
                            if (hasSeenFrameBoundary) ParsePacket();
                            else hasSeenFrameBoundary = true;
                            packetBuffer.Clear();
                        }
                        else if (packetBuffer.Count < 4096)
                        {
                            packetBuffer.Add(value);
                        }
                        else
                        {
                            packetBuffer.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!disposed && !cancel.IsCancellationRequested)
                    Disconnected?.Invoke("N64 adapter disconnected: " + ex.Message);
            }
            finally
            {
                try { if (port.IsOpen) port.Close(); } catch { }
                try { port.Dispose(); } catch { }
            }
        }

        private void ParsePacket()
        {
            int length = packetBuffer.Count;
            if (length > 0 && packetBuffer[length - 1] == 0x0D) length--;
            if (length == 0) return;
            var packet = packetBuffer.GetRange(0, length).ToArray();
            try
            {
                var state = Nintendo64.ReadFromPacket(packet);
                if (state != null) StateChanged?.Invoke(state);
            }
            catch { /* Ignore incomplete or malformed serial frames and wait for the next delimiter. */ }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancel.Cancel();
            try { if (port.IsOpen) port.Close(); } catch { }
            try { worker.Join(500); } catch (ThreadStateException) { }
            port.Dispose();
            cancel.Dispose();
        }
    }
}
