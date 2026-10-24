using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace NestZoneDiscord
{
    internal sealed class NativeHost
    {
        private readonly object _outGate = new object();
        private Stream _stdin;
        private Stream _stdout;
        private readonly JavaScriptSerializer _js = new JavaScriptSerializer();
        private DiscordIpc _ipc;
        private object _lastActivity;

        internal void Run()
        {
            Log.Write("Native host start v" + Program.Version);
            _js.MaxJsonLength = 2 * 1024 * 1024;
            _stdin = Console.OpenStandardInput();
            _stdout = Console.OpenStandardOutput();
            _ipc = new DiscordIpc();
            _ipc.OnStatus = IpcStatusChanged;
            _ipc.OnError = IpcErrored;
            try
            {
                while (true)
                {
                    string json = ReadMessage();
                    if (json == null) break; // stdin closed
                    HandleMessage(json);
                }
            }
            catch (Exception ex) { Log.Write("Host loop err: " + ex.Message); }
            try { if (_ipc != null) _ipc.ClosePipe(); } catch { }
            Log.Write("Native host exit");
        }

        private void HandleMessage(string json)
        {
            string type = null;
            Dictionary<string, object> msg = null;
            try
            {
                msg = _js.Deserialize<Dictionary<string, object>>(json);
                object t; if (msg.TryGetValue("type", out t)) type = t as string;
            }
            catch (Exception ex) { Log.Write("Bad msg: " + ex.GetType().Name); return; }

            string shown = SafeForLog(type);
            Log.Write("recv type=" + shown);
            if (type == "ping") { DoPing(); }
            else if (type == "set") { DoSet(msg); }
            else if (type == "clear") { DoClear(); }
            else { Log.Write("Unknown type: " + shown); }
            Log.Write("done type=" + shown);
        }

        // Short, printable ASCII only, so input cannot fake extra log lines.
        private static string SafeForLog(string s)
        {
            if (s == null) return "null";
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < s.Length && b.Length < 32; i++)
            {
                char c = s[i];
                b.Append(c >= 0x20 && c < 0x7F ? c : '?');
            }
            return b.ToString();
        }

        private void DoPing()
        {
            bool ok = false;
            try { ok = _ipc.EnsureConnected(); } catch (Exception ex) { Log.Write("ping connect err: " + ex.Message); }
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["type"] = "status";
            r["version"] = Program.Version;
            r["discord"] = ok ? "connected" : "not-running";
            r["user"] = ok ? _ipc.User : null;
            SendReply(r);
        }

        private void DoSet(Dictionary<string, object> msg)
        {
            object act; msg.TryGetValue("activity", out act);
            _lastActivity = act;
            bool ok;
            try { ok = _ipc.EnsureConnected(); }
            catch (Exception ex)
            {
                SendError("Discord connection failed: " + ex.Message);
                return;
            }
            if (!ok)
            {
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["type"] = "error";
                r["message"] = "Discord is not running";
                r["discord"] = "not-running";
                SendReply(r);
                return;
            }
            try
            {
                _ipc.SetActivity(act);
                Dictionary<string, object> ok2 = new Dictionary<string, object>();
                ok2["type"] = "ok";
                SendReply(ok2);
            }
            catch (Exception ex)
            {
                Log.Write("set err: " + ex.Message);
                _ipc.ClosePipe();
                SendError("Could not send to Discord: " + ex.Message);
            }
        }

        private void DoClear()
        {
            _lastActivity = null;
            try
            {
                if (_ipc.Connected) _ipc.SetActivity(null);
            }
            catch (Exception ex) { Log.Write("clear err: " + ex.Message); }
            Dictionary<string, object> ok = new Dictionary<string, object>();
            ok["type"] = "ok";
            SendReply(ok);
        }

        private void IpcStatusChanged(string state, string user, string error)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["type"] = "status";
            r["version"] = Program.Version;
            r["discord"] = state;
            r["user"] = user;
            if (!string.IsNullOrEmpty(error)) r["error"] = error;
            SendReply(r);
        }

        private void IpcErrored(string message)
        {
            Log.Write("Discord error: " + message);
            SendError(message);
        }

        private void SendError(string message)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["type"] = "error";
            r["message"] = message;
            SendReply(r);
        }

        private string ReadMessage()
        {
            byte[] lenBuf = ReadExact(4);
            if (lenBuf == null) return null;
            int len = (lenBuf[0]) | (lenBuf[1] << 8) | (lenBuf[2] << 16) | (lenBuf[3] << 24);
            if (len < 0 || len > 1024 * 1024) { Log.Write("bad len " + len); return null; }
            if (len == 0) return "";
            byte[] body = ReadExact(len);
            if (body == null) return null;
            return Encoding.UTF8.GetString(body);
        }

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = _stdin.Read(buf, got, count - got);
                if (n <= 0) return null;
                got += n;
            }
            return buf;
        }

        private void SendReply(object obj)
        {
            string json = _js.Serialize(obj);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[4];
            int len = payload.Length;
            header[0] = (byte)(len & 0xFF);
            header[1] = (byte)((len >> 8) & 0xFF);
            header[2] = (byte)((len >> 16) & 0xFF);
            header[3] = (byte)((len >> 24) & 0xFF);
            lock (_outGate)
            {
                try
                {
                    _stdout.Write(header, 0, 4);
                    _stdout.Write(payload, 0, payload.Length);
                    _stdout.Flush();
                }
                catch (Exception ex) { Log.Write("out err: " + ex.Message); }
            }
        }
    }
}

