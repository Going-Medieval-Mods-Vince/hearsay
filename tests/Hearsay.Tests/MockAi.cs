// A local stand-in for an OpenAI-compatible server: records every request and answers from a queue, so tests cost no tokens.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

sealed class MockAi : IDisposable
{
    internal sealed class Request { public string Path; public JObject Body; public string Authorization; }

    readonly HttpListener listener = new HttpListener();
    readonly Queue<(int status, string body)> answers = new Queue<(int, string)>();
    internal readonly List<Request> Requests = new List<Request>();
    internal readonly string Url;

    internal MockAi()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}/v1";
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        new Thread(Serve) { IsBackground = true }.Start();
    }

    internal void Reset() { lock (answers) { answers.Clear(); Requests.Clear(); } }

    /// <summary>Queue an answer; unqueued requests get a plain "Hello." reply.</summary>
    internal MockAi Answer(int status, string body) { lock (answers) answers.Enqueue((status, body)); return this; }

    internal MockAi Reply(string text, JObject usage = null) =>
        Answer(200, new JObject { ["choices"] = new JArray { new JObject { ["message"] = new JObject { ["role"] = "assistant", ["content"] = text } } }, ["usage"] = usage ?? new JObject() }.ToString());

    internal MockAi Refuse(string param) => Answer(400, new JObject { ["error"] = new JObject { ["message"] = $"Unsupported parameter: '{param}'" } }.ToString());

    void Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); } catch { return; }
            string text = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
            (int status, string body) a;
            lock (answers)
            {
                Requests.Add(new Request { Path = ctx.Request.Url.AbsolutePath, Body = TryParse(text), Authorization = ctx.Request.Headers["Authorization"] });
                a = answers.Count > 0 ? answers.Dequeue() : (200, "{\"choices\":[{\"message\":{\"content\":\"Hello.\"}}]}");
            }
            var bytes = Encoding.UTF8.GetBytes(a.body);
            ctx.Response.StatusCode = a.status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }

    static JObject TryParse(string s) { try { return JObject.Parse(s); } catch { return null; } }

    public void Dispose() { try { listener.Stop(); } catch { } }
}
