using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DfwcResultsBot
{
    class TestConnection : IConnection
    {
        class ChatApi : IChatApi
        {
            ConcurrentQueue<ChatMessage> _queue = new ConcurrentQueue<ChatMessage>();
            private CancellationToken _token;

            public ChatApi(CancellationToken token)
            {
                _token = token;
            }
            public async IAsyncEnumerable<ChatMessage> RecvMessages()
            {
                while (!_token.IsCancellationRequested)
                {
                    while (_queue.TryDequeue(out var s))
                    {
                        await Task.Yield();
                        yield return s;
                    }
                    await Task.Delay(100);
                }
            }

            public Task SendMessage(string message)
            {
                Console.WriteLine($">: {message}");
                return Task.CompletedTask;
            }
            public void Add(ChatMessage message)
            {
                _queue.Enqueue(message);
            }
        }
        CancellationTokenSource _stop = new CancellationTokenSource();
        ChatApi _channel = null;
        private void Run(string nickname, string auth)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var line = Console.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                    if (_channel == null)
                    {
                        Console.WriteLine("!! TRYING TO WRITE TO CHANNEL WHICH IS NOT JOINED !!");
                        continue;
                    }
                    var s = line.Split(' ', 2);
                    if (s.Length < 2)
                    {
                        continue;
                    }
                    _channel.Add(new ChatMessage() { Sender = s[0], Message = s[1] });
                }
            }
            finally
            {
                _stop.Cancel();
            }
        }
        public async Task Connect(string nickname, string auth)
        {
            await Task.Run(() => Run(nickname, auth));
        }

        public void Disconnect()
        {
            Console.WriteLine("Disconnect called");
            _stop.Cancel();
        }

        public void Dispose()
        {
            Console.WriteLine("Dispose called");
            _stop.Dispose();
        }

        public async Task<IChatApi> JoinChannel(string channel)
        {
            Console.WriteLine($">> Joined channel {channel} <<");
            if (_channel != null)
            {
                Console.WriteLine($"!!! MULTIPLE JOINS !!!");
                return _channel;
            }
            _channel = new ChatApi(_stop.Token);
            await Task.Yield();
            return _channel;
        }

        public async ValueTask LeaveChannel(string channel)
        {
            Console.WriteLine($"<< Left channel {channel} >>");
            _channel = null;
            await Task.Yield();
        }

        public void Terminate()
        {
            Console.WriteLine("Terminate called");
        }
    }
}