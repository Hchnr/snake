using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace SnakeClub
{
    // One WebSocket connection per room. In WebGL the browser owns the socket;
    // desktop/editor builds use ClientWebSocket and marshal events to Unity's thread.
    public sealed class RoomConnection : MonoBehaviour
    {
        [Serializable] private sealed class SocketEvent { public string type; public string data; }
        public Action<string, string> OnEvent;
        private readonly Queue<SocketEvent> events = new Queue<SocketEvent>();
        private bool closing;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void SnakeSocketOpen(string objectName, string url);
        [DllImport("__Internal")] private static extern void SnakeSocketSend(string message);
        [DllImport("__Internal")] private static extern void SnakeSocketClose();
#else
        private ClientWebSocket socket;
        private CancellationTokenSource cancel;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
#endif

        public void Open(string url)
        {
            Close();
            closing = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            SnakeSocketOpen(gameObject.name, url);
#else
            cancel = new CancellationTokenSource();
            socket = new ClientWebSocket();
            _ = Receive(url, socket, cancel.Token);
#endif
        }

        public void Send(string message)
        {
            if (closing) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SnakeSocketSend(message);
#else
            var current = socket;
            var source = cancel;
            if (current != null && source != null && current.State == WebSocketState.Open)
                _ = SendNative(current, message, source.Token);
#endif
        }

        public void Close()
        {
            closing = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            SnakeSocketClose();
#else
            if (cancel != null) { cancel.Cancel(); cancel.Dispose(); cancel = null; }
            if (socket != null) { socket.Abort(); socket.Dispose(); socket = null; }
#endif
            lock (events) events.Clear();
        }

        public void OnSocketEvent(string json)
        {
            try
            {
                var item = JsonUtility.FromJson<SocketEvent>(json);
                if (item != null) Enqueue(item.type, item.data);
            }
            catch (Exception) { Enqueue("error", "无法读取网络消息"); }
        }

        private void Enqueue(string type, string data)
        {
            lock (events) events.Enqueue(new SocketEvent { type = type, data = data });
        }

        private void Update()
        {
            while (true)
            {
                SocketEvent item;
                lock (events)
                {
                    if (events.Count == 0) break;
                    item = events.Dequeue();
                }
                if (!closing) OnEvent?.Invoke(item.type, item.data);
            }
        }

        private void OnDestroy() => Close();

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task Receive(string url, ClientWebSocket current, CancellationToken token)
        {
            try
            {
                await current.ConnectAsync(new Uri(url), token);
                Enqueue("open", "");
                var buffer = new byte[8192];
                while (current.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await current.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close) { Enqueue("close", "连接已关闭"); return; }
                            message.Write(buffer, 0, result.Count);
                            if (message.Length > 1024 * 1024) throw new InvalidOperationException("网络消息过大");
                        } while (!result.EndOfMessage);
                        Enqueue("message", Encoding.UTF8.GetString(message.ToArray()));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Enqueue("error", e.Message); }
        }

        private async Task SendNative(ClientWebSocket current, string message, CancellationToken token)
        {
            try
            {
                await sendLock.WaitAsync(token);
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    if (current.State == WebSocketState.Open)
                        await current.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, token);
                }
                finally { sendLock.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Enqueue("error", e.Message); }
        }
#endif
    }
}
