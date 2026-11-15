using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace vShell.Core
{
    /// <summary>
    /// A second start with "--plugin &lt;id&gt; &lt;verb&gt; args…" (Explorer's "Add to shelf", a shortcut) hands those words to the running vShell
    /// over a named pipe and ends. The pipe takes only this Windows account (CurrentUserOnly) and this settings folder; the running
    /// vShell passes the words to the plugin that registered the verb (PluginHost.AddCommandLine).
    /// </summary>
    public static class HandOff
    {
        const int MaxBytes = 1 << 20;     // 1 MB of words is far more than any Explorer selection

        /// <summary>The pipe for this settings folder (a test with its own VSHELL_DATA_DIR has another).</summary>
        static string PipeName { get { return "vShell.HandOff." + vSuite.Instance.Id.ToString("x8"); } }

        /// <summary>The words after --plugin (id, verb, args…), or null when the command line isn't one.</summary>
        public static string[] PluginWords(string[] args)
        {
            if (args == null || args.Length < 3 || !string.Equals(args[0], "--plugin", StringComparison.OrdinalIgnoreCase)) return null;
            string[] w = new string[args.Length - 1];
            Array.Copy(args, 1, w, 0, w.Length);
            return w;
        }

        /// <summary>Give the words to the running vShell. true = it took them; false = none answered within the wait (starting or closing).</summary>
        public static bool Send(string[] words, int waitMs)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(words));
            if (data.Length > MaxBytes) { Log.Write("Hand-off: too much to hand over (" + data.Length + " bytes)"); return false; }
            DateTime until = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (true)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
                    {
                        int left = (int)Math.Max(100, (until - DateTime.UtcNow).TotalMilliseconds);
                        pipe.Connect(left);
                        pipe.Write(BitConverter.GetBytes(data.Length), 0, 4);
                        pipe.Write(data, 0, data.Length);
                        pipe.Flush();
                        return pipe.ReadByte() == 1;
                    }
                }
                catch (TimeoutException) { return false; }
                catch (IOException)
                {
                    // the server was busy with another start (one at a time): try again until the wait is over
                    if (DateTime.UtcNow >= until) return false;
                    Thread.Sleep(50);
                }
                catch (Exception ex) { Log.Write("Hand-off: " + ex.Message); return false; }
            }
        }

        static Thread _thread;

        /// <summary>The running vShell: listen for hand-offs; received gets each one's words (on the listening thread - switch to the UI yourself).</summary>
        public static void Listen(Action<string[]> received)
        {
            if (_thread != null) return;
            _thread = new Thread(delegate () { Serve(received); }) { IsBackground = true, Name = "vShell hand-off" };
            _thread.Start();
        }

        static void Serve(Action<string[]> received)
        {
            int failures = 0;
            while (true)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly))
                    {
                        pipe.WaitForConnection();
                        failures = 0;
                        string[] words = Read(pipe);
                        bool ok = words != null && words.Length >= 2;
                        try { pipe.WriteByte(ok ? (byte)1 : (byte)0); pipe.Flush(); } catch { }
                        if (ok)
                        {
                            try { received(words); }
                            catch (Exception ex) { Log.Write("Hand-off: " + ex.Message); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // e.g. a second vShell with the same pipe during an update hand-over: wait and try again, a few times
                    Log.Write("Hand-off: listening failed - " + ex.Message);
                    if (++failures >= 20) { Log.Write("Hand-off: stopped listening"); return; }
                    Thread.Sleep(3000);
                }
            }
        }

        static string[] Read(Stream pipe)
        {
            byte[] len = ReadExactly(pipe, 4);
            if (len == null) return null;
            int n = BitConverter.ToInt32(len, 0);
            if (n <= 0 || n > MaxBytes) return null;
            byte[] data = ReadExactly(pipe, n);
            if (data == null) return null;
            try { return JsonSerializer.Deserialize<string[]>(data); }
            catch (JsonException) { return null; }
        }

        static byte[] ReadExactly(Stream s, int n)
        {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return b;
        }
    }
}
