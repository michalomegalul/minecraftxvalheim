using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Valcraft
{
    /// <summary>
    /// Localhost TCP server the Minecraft mod connects to. One JSON object per line.
    /// Runs on a background thread; the game thread drains <see cref="Inbox"/>.
    /// </summary>
    internal sealed class Link
    {
        public const int Port = 25610;

        public readonly ConcurrentQueue<JObject> Inbox = new ConcurrentQueue<JObject>();
        private volatile StreamWriter _writer;
        private TcpListener _listener;

        public bool Connected => _writer != null;

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Seconds on a precise clock shared by the network thread and the game.</summary>
        public static double Now => Clock.Elapsed.TotalSeconds;

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "Valcraft link" }.Start();
            Plugin.Log.LogInfo($"listening for Minecraft on 127.0.0.1:{Port}");
        }

        public void Send(JObject msg)
        {
            var w = _writer;
            if (w == null) return;
            try
            {
                lock (w) w.Write(msg.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            }
            catch (Exception)
            {
                _writer = null;
            }
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"link accept failed: {e.Message}");
                    return;
                }
                // Only one Minecraft at a time; a new connection replaces the old one.
                using (client)
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    Inbox.Enqueue(Status("connected"));
                    try
                    {
                        var reader = new StreamReader(stream, Encoding.UTF8);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            try
                            {
                                var msg = JObject.Parse(line);
                                msg["_rx"] = Now; // arrival time, stamped before the game thread sees it
                                Inbox.Enqueue(msg);
                            }
                            catch (Exception)
                            {
                                // Malformed line; skip.
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Connection dropped.
                    }
                    _writer = null;
                    Inbox.Enqueue(Status("disconnected"));
                }
            }
        }

        private static JObject Status(string state) => new JObject { ["t"] = "_link", ["state"] = state };
    }
}
