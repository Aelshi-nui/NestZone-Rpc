using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace NestZoneDiscord
{
    // State values: "connected", "not-running", "error"
    internal delegate void IpcStatusHandler(string state, string user, string error);
    internal delegate void IpcErrorHandler(string message);

    internal sealed class DiscordIpc
    {
        private readonly object _writeGate = new object();
        private NamedPipeClientStream _pipe;
        private Thread _reader;
        private volatile bool _ready;
        private volatile bool _connected;
        private string _user;
        private readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        internal IpcStatusHandler OnStatus;
        internal IpcErrorHandler OnError;

        internal bool Connected { get { return _connected && _ready; } }
        internal string User { get { return _user; } }

        // Returns true if connected and READY received.
        internal bool EnsureConnected()
        {
            if (_connected && _ready) return true;
            ClosePipe();
            for (int i = 0; i <= 9; i++)
            {
                string name = "discord-ipc-" + i;
                try
                {
                    if (!File.Exists(@"\\.\pipe\" + name)) continue;
                    NamedPipeClientStream p = new NamedPipeClientStream(".", name,
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(2000);
                    _pipe = p;
                    _connected = true;
                    Log.Write("IPC connected to pipe " + name);
                    SendHandshake();
                    StartReader();
                    if (WaitForReady(8000)) return true;
                    Log.Write("IPC no READY on " + name);
                    ClosePipe();
                }
                catch (TimeoutException) { }
                catch (Exception ex) { Log.Write("IPC connect err " + name + ": " + ex.Message); }
            }
            _connected = false;
            return false;
        }

        private bool WaitForReady(int ms)
        {
            int waited = 0;
            while (waited < ms)
            {
                if (_ready) return true;
                if (!_connected) return false;
                Thread.Sleep(50);
                waited += 50;
            }
            return _ready;
        }

        private void SendHandshake()
        {
            Dictionary<string, object> h = new Dictionary<string, object>();
            h["v"] = 1;
            h["client_id"] = Program.ClientId;
            WriteFrame(0, _js.Serialize(h));
        }

        private void WriteFrame(int opcode, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] all = new byte[8 + payload.Length];
            WriteInt32LE(all, 0, opcode);
            WriteInt32LE(all, 4, payload.Length);
            Array.Copy(payload, 0, all, 8, payload.Length);
            lock (_writeGate)
            {
                Stream s = _pipe;
                if (s == null) throw new IOException("pipe closed");
                s.Write(all, 0, all.Length);
                s.Flush();
            }
        }

        private static void WriteInt32LE(byte[] b, int off, int v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }

        // activity may be null to clear.
        internal void SetActivity(object activity)
        {
            Dictionary<string, object> args = new Dictionary<string, object>();
            args["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            args["activity"] = activity;
            Dictionary<string, object> frame = new Dictionary<string, object>();
            frame["cmd"] = "SET_ACTIVITY";
            frame["args"] = args;
            frame["nonce"] = Guid.NewGuid().ToString();
            WriteFrame(1, _js.Serialize(frame));
        }

        private void StartReader()
        {
            _reader = new Thread(ReadLoop);
            _reader.IsBackground = true;
            _reader.Start();
        }

        private void ReadLoop()
        {
            try
            {
                while (true)
                {
                    NamedPipeClientStream s = _pipe;
                    if (s == null) break;
                    byte[] header = ReadExact(s, 8);
                    if (header == null) break;
                    int opcode = ReadInt32LE(header, 0);
                    int len = ReadInt32LE(header, 4);
                    if (len < 0 || len > 1024 * 1024) break;
                    byte[] body = len > 0 ? ReadExact(s, len) : new byte[0];
                    if (body == null) break;
                    string json = Encoding.UTF8.GetString(body);
                    HandleFrame(opcode, json);
                }
            }
            catch (Exception ex) { Log.Write("IPC read ended: " + ex.Message); }
            bool was = _connected && _ready;
            _connected = false;
            _ready = false;
            if (was && OnStatus != null) OnStatus("not-running", null, null);
        }

        private void HandleFrame(int opcode, string json)
        {
            if (opcode == 2) // CLOSE
            {
                Log.Write("IPC close frame: " + json);
                _connected = false; _ready = false;
                if (OnStatus != null) OnStatus("error", null, "Discord closed the connection");
                ClosePipe();
                return;
            }
            if (opcode == 3) // PING -> PONG
            {
                try { WriteFrame(4, json); } catch { }
                return;
            }
            if (opcode != 1) return;
            try
            {
                Dictionary<string, object> m = _js.Deserialize<Dictionary<string, object>>(json);
                object evtObj; m.TryGetValue("evt", out evtObj);
                string evt = evtObj as string;
                object dataObj; m.TryGetValue("data", out dataObj);
                Dictionary<string, object> data = dataObj as Dictionary<string, object>;
                if (evt == "READY")
                {
                    _user = ExtractUser(data);
                    _ready = true;
                    Log.Write("IPC READY user=" + (_user == null ? "?" : _user));
                    if (OnStatus != null) OnStatus("connected", _user, null);
                }
                else if (evt == "ERROR")
                {
                    string msg = ExtractErrorMessage(data);
                    Log.Write("IPC ERROR: " + msg);
                    if (OnError != null) OnError(msg);
                }
            }
            catch (Exception ex) { Log.Write("IPC parse err: " + ex.Message); }
        }

        private static string ExtractUser(Dictionary<string, object> data)
        {
            if (data == null) return null;
            object userObj; data.TryGetValue("user", out userObj);
            Dictionary<string, object> user = userObj as Dictionary<string, object>;
            if (user == null) return null;
            object gn; user.TryGetValue("global_name", out gn);
            string g = gn as string;
            if (!string.IsNullOrEmpty(g)) return g;
            object un; user.TryGetValue("username", out un);
            return un as string;
        }

        private static string ExtractErrorMessage(Dictionary<string, object> data)
        {
            if (data == null) return "Discord reported an error";
            object msg; data.TryGetValue("message", out msg);
            string s = msg as string;
            if (!string.IsNullOrEmpty(s)) return s;
            return "Discord reported an error";
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            byte[] buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) return null;
                got += n;
            }
            return buf;
        }

        private static int ReadInt32LE(byte[] b, int off)
        {
            return (b[off]) | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);
        }

        internal void ClosePipe()
        {
            NamedPipeClientStream p = _pipe;
            _pipe = null;
            _ready = false;
            _connected = false;
            if (p != null)
            {
                try { p.Close(); } catch { }
                try { p.Dispose(); } catch { }
            }
        }
    }
}

