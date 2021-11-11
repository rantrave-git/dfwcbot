using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DfwcResultsBot
{
#if NO_NITO
    public static class WaitHandleExtensions
    {
        public static Task AsTask(this WaitHandle handle) => AsTask(handle, Timeout.InfiniteTimeSpan);

        public static Task AsTask(this WaitHandle handle, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource();
            var registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) =>
            {
                var localTcs = (TaskCompletionSource)state;
                if (timedOut)
                    localTcs.TrySetCanceled();
                else
                    localTcs.TrySetResult();
            }, tcs, timeout, executeOnlyOnce: true);
            tcs.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration, TaskScheduler.Default);
            return tcs.Task;
        }
    }
#endif
    class TwitchConnection : IDisposable, IConnection
    {
        class TwitchChannelChatApi : IChatApi
        {
            private ConcurrentQueue<ChatMessage> _queue = new ConcurrentQueue<ChatMessage>();
#if NO_NITO
            private ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
#else
            private Nito.AsyncEx.AsyncManualResetEvent _resetEvent = new Nito.AsyncEx.AsyncManualResetEvent(false);
#endif
            private ClientWebSocket _connection;
            private CancellationToken _terminate;
            private Buffer _sendBuffer = new Buffer();

            public string Channel { get; }
            private volatile bool _isJoined = false;

            public TwitchChannelChatApi(ClientWebSocket connection, string channel, CancellationToken terminate)
            {
                _connection = connection;
                _terminate = terminate;
                Channel = channel;
            }
            public async ValueTask Join()
            {
                lock (_sendBuffer)
                {
                    if (_isJoined) return;
                    _isJoined = true;
                }
                _sendBuffer.Start().WriteNext($"JOIN ").WriteNext(Channel).End();
                await _connection.SendAsync(_sendBuffer.Value, WebSocketMessageType.Text, true, _terminate);
                Console.WriteLine($"Joined to channel '{Channel}'");
            }

            public async ValueTask Leave()
            {
                lock (_sendBuffer)
                {
                    if (!_isJoined) return;
                    _isJoined = false;
                }
                _resetEvent.Set();
                _sendBuffer.Start().WriteNext("PART ").WriteNext(Channel).End();
                await _connection.SendAsync(_sendBuffer.Value, WebSocketMessageType.Text, true, _terminate);
                _sendBuffer.Dispose();
                Console.WriteLine($"Left channel '{Channel}'");
            }
            public async IAsyncEnumerable<ChatMessage> RecvMessages()
            {
                var ts = TimeSpan.FromMilliseconds(50);
                while (true)
                {
                    while (_queue.TryDequeue(out var v)) yield return v;
                    if (_connection.State != WebSocketState.Open || _terminate.IsCancellationRequested) break;
                    _resetEvent.Reset();
#if NO_NITO
                    await _resetEvent.WaitHandle.AsTask(ts);
#else
                    await _resetEvent.WaitAsync(_terminate);
#endif
                }
                while (_queue.TryDequeue(out var v)) yield return v;
                Console.WriteLine($"Channel '{Channel}' done!");
            }

            public async Task SendMessage(string message)
            {
                _sendBuffer.Start().WriteNext("PRIVMSG ").WriteNext(Channel).WriteNext($" :{message.Replace("\r\n", "")}").End();
                await _connection.SendAsync(_sendBuffer.Value, WebSocketMessageType.Text, true, _terminate);
            }

            public void AddMessage(string sender, string message)
            {
                _queue.Enqueue(new ChatMessage() { Sender = sender, Message = message });
                _resetEvent.Set();
            }
        }
        private ClientWebSocket _connection = new ClientWebSocket();
        private CancellationTokenSource _stop = new CancellationTokenSource();
        private CancellationTokenSource _terminate = new CancellationTokenSource();
        private TaskCompletionSource _connectedState = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private object _receiveLock = new object();
        private Task<WebSocketReceiveResult> _receiveOperation = null;

        private ConcurrentDictionary<string, TwitchChannelChatApi> _channels = new ConcurrentDictionary<string, TwitchChannelChatApi>();
        private TaskCompletionSource _completed = new TaskCompletionSource();

        private async Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            var cancellation = new TaskCompletionSource();
            using (cancellationToken.Register(() => cancellation.TrySetCanceled(cancellationToken)))
            {
                Task<WebSocketReceiveResult> currentTask;
                lock (_receiveLock)
                {
                    if (_receiveOperation != null)
                    {
                        currentTask = _receiveOperation;
                    }
                    else
                    {
                        currentTask = _receiveOperation = _connection.ReceiveAsync(buffer, _terminate.Token);
                    }
                }
                if (await Task.WhenAny(currentTask, cancellation.Task) == cancellation.Task)
                {
                    await cancellation.Task;
                }
                try
                {
                    return await currentTask;
                }
                catch (OperationCanceledException) { return null; } // normal behaviour, operation cancelled
                finally
                {
                    _receiveOperation = null;
                }
            }
        }
        public delegate Task MessageSend(string command, string[] arguments, string prefix, string trailer);
        public delegate Task OnMessageEvent(string command, string[] arguments, string prefix, string trailer, MessageSend sendBack);

        private void OnMessage(string command, string[] arguments, string prefix, string trailer)
        {
            if (command.ToLowerInvariant() == "privmsg" && arguments.Length > 0)
            {
                var excl = prefix.IndexOf('!');
                if (excl >= 0)
                {
                    var user = prefix.Substring(1, excl - 1);
                    if (user != null && _channels.TryGetValue(arguments[0], out var ca))
                    {
                        ca.AddMessage(user, trailer);
                    }
                }
            }
        }
        private string Channel(string c) => c[0] != '#' ? $"#{c}" : c;
        public async Task<IChatApi> JoinChannel(string channel)
        {
            await _connectedState.Task;
            var ca = _channels.GetOrAdd(Channel(channel), new TwitchChannelChatApi(_connection, Channel(channel), _terminate.Token));
            await ca.Join();
            return ca;
        }
        public async ValueTask LeaveChannel(string channel)
        {
            if (_channels.TryRemove(Channel(channel), out var ca))
            {
                await ca.Leave();
            }
        }
        public async Task Connect(string nickname, string auth)
        {
            Console.WriteLine(new Uri("wss://irc-ws.chat.twitch.tv:443").Port);
            await _connection.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), _stop.Token);
            using var sendBuffer = new Buffer();
            byte[] recvBuffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                sendBuffer.Start().WriteNext("PASS ").WriteNext(auth).End();
                Console.WriteLine($"> {Encoding.UTF8.GetString(sendBuffer.Value.Span)}");
                await _connection.SendAsync(sendBuffer.Value, WebSocketMessageType.Text, true, _terminate.Token);
                sendBuffer.Start().WriteNext("NICK ").WriteNext(nickname).End();
                Console.WriteLine($"> {Encoding.UTF8.GetString(sendBuffer.Value.Span)}");
                await _connection.SendAsync(sendBuffer.Value, WebSocketMessageType.Text, true, _terminate.Token);

                while (!_stop.Token.IsCancellationRequested)
                {
                    var recv = await ReceiveAsync(recvBuffer, _stop.Token);
                    if (recv == null) { break; }
                    if (recv.CloseStatus != null)
                    {
                        Console.WriteLine($"Connection closed =( {recv.CloseStatus}");
                        break;
                    }
                    if (!_connectedState.Task.IsCompleted)
                    {
                        _connectedState.TrySetResult();
                    }
                    var message = Encoding.UTF8.GetString(recvBuffer.AsSpan(0, recv.Count)).TrimEnd();
                    Console.WriteLine($"< {message}");
                    int commandStart = 0;
                    string prefix = null;
                    if (message[0] == ':')
                    {
                        // has prefix
                        commandStart = message.IndexOf(' ') + 1;
                        prefix = message.Substring(0, commandStart - 1);
                    }
                    int commandEnd = message.IndexOf(' ', commandStart);
                    var command = message.Substring(commandStart, commandEnd - commandStart);
                    var trailerStart = message.IndexOf(':', commandEnd);
                    string trailer = null;
                    if (trailerStart != -1)
                    {
                        trailer = message.Substring(trailerStart + 1, message.Length - trailerStart - 1);
                    }
                    else
                    {
                        trailerStart = message.Length;
                    }
                    var args = message.Substring(commandEnd, trailerStart - commandEnd).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (command.ToLowerInvariant() == "ping")
                    {
                        sendBuffer.Start().WriteNext("PONG ").WriteNext(message.Substring(5, message.Length - 5));
                        Console.WriteLine($"> {Encoding.UTF8.GetString(sendBuffer.Value.Span)}");
                        await _connection.SendAsync(sendBuffer.Value, WebSocketMessageType.Text, true, _terminate.Token);
                    }
                    else
                    {
                        OnMessage(command, args, prefix, trailer);
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                _connectedState.TrySetException(e);
                Console.WriteLine(e);
            }
            finally
            {
                _connectedState.TrySetCanceled();
                sendBuffer.Start().WriteNext("QUIT :chatbot").End();
                Console.WriteLine($"> {Encoding.UTF8.GetString(sendBuffer.Value.Span)}");
                await _connection.SendAsync(sendBuffer.Value, WebSocketMessageType.Text, true, _terminate.Token);
                ArrayPool<byte>.Shared.Return(recvBuffer);
                _completed.TrySetResult();
                Console.WriteLine("Execution done!");
            }
        }
        public async Task Disconnect()
        {
            Console.WriteLine("Disconnect called");
            if (_stop == null) return;
            _stop?.Cancel();
            await _completed.Task;
        }
        public void Terminate()
        {
            Console.WriteLine("Terminate called");
            _terminate.Cancel();
        }

        public void Dispose()
        {
            Console.WriteLine("Dispose called");
            if (_stop != null)
            {
                _stop.Dispose();
                _stop = null;
            }
            if (_terminate != null)
            {
                _terminate.Dispose();
                _terminate = null;
            }
            _connection.Dispose();
        }
    }
}
