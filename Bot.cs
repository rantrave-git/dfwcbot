using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;

namespace DfwcResultsBot
{
    class SelectionConfiguration
    {
        public string[] RequiredPlayers { get; set; }
        public int RequiredTop { get; set; }
        public int TotalDemos { get; set; }
    }
    class Credentials
    {
        public string Nickname { get; set; }
        public string Password { get; set; }
    }
    class DfwcBotConfiguration
    {
        public string PrivatePrefix { get; set; } = ""; // "/w " but not working ='(
        public double? AnounceTimeSeconds { get; set; } = 600;
        public string Command { get; set; } = "vote";
        public bool UseArchive { get; set; } = false;
        public string ChannelName { get; set; }
        public string ExtractDirectory { get; set; }
        public string[] Superusers { get; set; }
        public SelectionConfiguration Vq3 { get; set; }
        public SelectionConfiguration Cpm { get; set; }
        public Dictionary<string, int> UserWeights { get; set; }
        public Credentials TwitchTvCredentials { get; set; }
        public Credentials DfwcOrgCredentials { get; set; }
        public SelectionConfiguration GetPhysicsConfig(Physics physics)
        {
            switch (physics)
            {
                default:
                case Physics.Vq3: return Vq3;
                case Physics.Cpm: return Cpm;
            }
        }
    }
    class Bot
    {
        private DfwcBotConfiguration _config;
        private IConnection _connection;
        private IChatApi _chat;

        private PlayersDict _players = new PlayersDict();
        private Places _places;
        private VoteCounter _voteCounter = new VoteCounter();
        private CancellationTokenSource _stop = new CancellationTokenSource();
        private volatile int _round = -1;
        private Func<IConnection> _connectionFactory;
        Regex _commandRe;
        Dictionary<Physics, DateTimeOffset> _statRateLimitBorder = new Dictionary<Physics, DateTimeOffset>()
        {
            [Physics.Vq3] = DateTimeOffset.UtcNow,
            [Physics.Cpm] = DateTimeOffset.UtcNow,
        };

        public Bot(Func<IConnection> connectionFactory, DfwcBotConfiguration config)
        {
            _connectionFactory = connectionFactory;
            _config = config;
            _commandRe = new Regex(@$"^([\\/]w\s.*)*[!+]{config.Command}\s+(.*)", RegexOptions.Compiled);
        }
        private List<(int, string)> SelectDemolist(Physics physics)
        {
            var top = _places.Top(physics, _config.GetPhysicsConfig(physics).TotalDemos);
            var rq = _config.GetPhysicsConfig(physics).RequiredTop;
            var required = _config.GetPhysicsConfig(physics).RequiredPlayers
                .Select(x => _players.FindNickname(x, out var y, out _) ? y : null)
                .Where(x => x != null)
                .Union(top.Take(rq))
                .ToList();
            var voted = _voteCounter.Summup(physics, _config.UserWeights).Select(x => x.Item1).Concat(top.Skip(rq)).ToList();

            return _places.ListDemos(physics, required, voted, _config.GetPhysicsConfig(physics).TotalDemos);
        }
        private async Task<(HttpClientHandler Handler, HttpClient Client)> GetLoggedClient()
        {
            var hh = new HttpClientHandler()
            {
                CookieContainer = new CookieContainer()
            };
            var hc = new HttpClient(hh);
            using (var resp = await hc.GetAsync("https://q3df.org/index"))
            {
                resp.EnsureSuccessStatusCode();
            }
            var creds = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("username", _config.DfwcOrgCredentials.Nickname),
                new KeyValuePair<string, string>("password",_config.DfwcOrgCredentials.Password),
                new KeyValuePair<string, string>("submit","Login"),
            };
            using (var resp = await hc.PostAsync("https://q3df.org/auth/login", new FormUrlEncodedContent(creds)))
            {
                resp.EnsureSuccessStatusCode();
            }
            return (hh, hc);
        }
        private async Task PerformExtraction(string initiator, int round)
        {
            var demosVq3 = SelectDemolist(Physics.Vq3);
            var demosCpm = SelectDemolist(Physics.Cpm);
            var dirpathVq3 = Path.Combine(_config.ExtractDirectory, $"round{round}", Physics.Vq3.ToString().ToLowerInvariant());
            var dirpathCpm = Path.Combine(_config.ExtractDirectory, $"round{round}", Physics.Cpm.ToString().ToLowerInvariant());
            if (!Directory.Exists(dirpathVq3)) Directory.CreateDirectory(dirpathVq3);
            if (!Directory.Exists(dirpathCpm)) Directory.CreateDirectory(dirpathCpm);

            for (int i = 0; i < 20; ++i)
            {
                var loggedContext = await GetLoggedClient();
                try
                {
                    if (demosVq3.Count > 0)
                    {
                        if (_config.UseArchive) demosVq3 = await _places.TryExtractAndSave(round, Physics.Vq3, demosVq3, dirpathVq3);

                        demosVq3 = await _places.TryDownload(loggedContext.Client, round, Physics.Vq3, demosVq3, dirpathVq3);
                    }
                    if (demosCpm.Count > 0)
                    {
                        if (_config.UseArchive) demosCpm = await _places.TryExtractAndSave(round, Physics.Cpm, demosCpm, dirpathCpm);

                        demosCpm = await _places.TryDownload(loggedContext.Client, round, Physics.Cpm, demosCpm, dirpathCpm);
                    }
                    if (demosVq3.Count == 0 && demosCpm.Count == 0) break;
                    if (i == 19)
                    {
                        File.WriteAllText(".log", $"There was errors when downloading:\n" +
                            $"Vq3:\n{String.Join("\n", demosVq3.Select(x => $"  {x.Item1}: {x.Item2}"))}" +
                            $"Cpm:\n{String.Join("\n", demosCpm.Select(x => $"  {x.Item1}: {x.Item2}"))}"
                        );
                    }
                    await Task.Delay(i * 1000);
                }
                finally
                {
                    loggedContext.Client.Dispose();
                    loggedContext.Handler.Dispose();
                }
            }
        }
        // private const string _privatePrefix = "/w ";
        // private const string _privatePrefix = "";
        private async Task HandleMessage(ChatMessage message)
        {
            var privatePrefix = _config.PrivatePrefix;
            var match = _commandRe.Match(message.Message);
            if (match.Success)
            {
                var arguments = match.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (arguments[0].ToLowerInvariant() == "start")
                {
                    if (_config.Superusers.Contains(message.Sender))
                    {
                        if (arguments.Length > 1 && int.TryParse(arguments[1], out var round))
                        {
                            _round = round;
                            _places = null;
                            await Task.WhenAll(
                                _voteCounter.Load(Physics.Vq3, null),
                                _voteCounter.Load(Physics.Cpm, null)
                            );
                            _saveResetEvent.Set();
                            await _chat?.SendMessage($"Voting for round {_round} started!\n");
                        }
                        else
                        {
                            await _chat?.SendMessage($"{privatePrefix}{message.Sender} usage: `start <round-name>`");
                        }
                    }
                }
                else if (arguments[0].ToLowerInvariant() == "stop")
                {
                    if (_config.Superusers.Contains(message.Sender))
                    {
                        if (_round != -1)
                        {
                            var context = await GetLoggedClient();
                            _places = null;
                            try
                            {
                                _places = await _players.LoadPlaces(context.Client, _round, _config.UseArchive);
                                if (_places == null)
                                {
                                    await _chat?.SendMessage($"{privatePrefix}{message.Sender} results are not ready yet");
                                    return;
                                }
                            }
                            finally
                            {
                                context.Client.Dispose();
                                context.Handler.Dispose();
                            }
                            _saveResetEvent.Set();
                            _ = PerformExtraction(message.Sender, _round);
                            _round = -1;
                        }
                        else
                        {
                            await _chat?.SendMessage($"{privatePrefix}{message.Sender} round voting is not started");
                        }
                    }
                }
                else if (arguments[0].ToLowerInvariant() == "stats")
                {
                    if (_config.Superusers.Contains(message.Sender))
                    {
                        if (_round != -1)
                        {
                            if (arguments.Length > 1 && Enum.TryParse<Physics>(arguments[1], true, out var physics))
                            {
                                if ((DateTimeOffset.UtcNow - _statRateLimitBorder[physics]) < TimeSpan.FromSeconds(10))
                                {
                                    return;
                                }
                                _statRateLimitBorder[physics] = DateTimeOffset.UtcNow;
                                var sum = _voteCounter.Summup(physics, _config.UserWeights);

                                await _chat?.SendMessage($"Voting for round {_round} (top 10): {String.Join(" ", sum.Take(10).Select((x, i) => $"{i + 1}. {x.Item1} ({x.Item2})."))}");
                            }
                            else
                            {
                                await _chat?.SendMessage($"{privatePrefix}{message.Sender} usage: `stats <physics>`");
                            }
                        }
                        else
                        {
                            await _chat?.SendMessage($"{privatePrefix}{message.Sender} round voting is not started");
                        }
                    }
                }
                else if (arguments[0].ToLowerInvariant() == "suggest")
                {
                    if (_config.Superusers.Contains(message.Sender))
                    {
                        if (arguments.Length > 2)
                        {
                            var text = match.Groups[2].Value.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[2];
                            await _chat?.SendMessage($"{arguments[1]} {text}");
                        }
                    }
                }
                else if (arguments[0].ToLowerInvariant() == "whisper")
                {
                    if (_config.Superusers.Contains(message.Sender))
                    {
                        if (arguments.Length > 2)
                        {
                            var text = match.Groups[2].Value.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[2];
                            await _chat?.SendMessage($"{privatePrefix}{arguments[1]} {text}");
                        }
                    }
                }
                else if (Enum.TryParse<Physics>(arguments[0], true, out var physics))
                {
                    if (_round != -1)
                    {
                        if (arguments.Length > 1)
                        {
                            var nickpart = match.Groups[2].Value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1];
                            if (_players.FindNickname(nickpart, out var nick, out var msg))
                            {
                                _voteCounter.AddVote(physics, message.Sender, nick);
                            }
                            await _chat?.SendMessage($"{privatePrefix}{message.Sender} {msg}");
                        }
                    }
                    else
                    {
                        await _chat?.SendMessage($"{privatePrefix}{message.Sender} round voting is not started yet");
                    }
                }
            }
        }
        private const string RunningFilename = "running";
        private string PhysicsName(Physics physics, int round) => $"votes-{round}.{physics.ToString().ToLowerInvariant()}";
#if NO_NITO
            private ManualResetEventSlim _saveResetEvent = new ManualResetEventSlim(false);
#else
        private Nito.AsyncEx.AsyncManualResetEvent _saveResetEvent = new Nito.AsyncEx.AsyncManualResetEvent(false);
#endif
        private async Task SaveStates()
        {
            try
            {
                while (!_stop.Token.IsCancellationRequested)
                {
                    // try { await Task.Delay(4000); } catch { }
                    _saveResetEvent.Reset();
                    try
                    {
#if NO_NITO
                    await _saveResetEvent.WaitHandle.AsTask(TimeSpan.FromMilliseconds(4000));
#else
                        using (var cts = new CancellationTokenSource(4000))
                            await _saveResetEvent.WaitAsync(cts.Token);
#endif
                    }
                    catch (OperationCanceledException) { }
                    if (_round != -1)
                    {
                        await Task.WhenAll(
                            File.WriteAllTextAsync(RunningFilename, _round.ToString()),
                            _voteCounter.Save(Physics.Vq3, PhysicsName(Physics.Vq3, _round)),
                            _voteCounter.Save(Physics.Cpm, PhysicsName(Physics.Cpm, _round))
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                Console.WriteLine("== Saving exited! ==");
            }
        }
        private async Task PeriodicMessage(TimeSpan period)
        {
            try
            {
                while (!_stop.Token.IsCancellationRequested)
                {
                    await Task.Delay(period, _stop.Token);
                    if (_round != -1)
                    {
                        await _chat?.SendMessage($"Demo watch voting for round {_round} is up.\nType: `!dfwc vq3 <player-name>` or `!dfwc cpm <player-name>` to vote.\nList of all players at: https://dfwc.q3df.org/comp/dfwc2021/standings.html");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
        TaskCompletionSource _completion = new TaskCompletionSource();
        private async Task StartInternal()
        {
            var token = _stop.Token;
            await _players.LoadNicknames();
            if (File.Exists(RunningFilename) && int.TryParse(await File.ReadAllTextAsync(RunningFilename), out var round))
            {
                _round = round;
                await Task.WhenAll(
                    _voteCounter.Load(Physics.Vq3, PhysicsName(Physics.Vq3, round)),
                    _voteCounter.Load(Physics.Cpm, PhysicsName(Physics.Cpm, round))
                );
            }
            else
            {
                _round = -1;
            }
            _ = SaveStates();
            if (_config.AnounceTimeSeconds != null && _config.AnounceTimeSeconds.Value > 0)
            {
                _ = PeriodicMessage(TimeSpan.FromSeconds(_config.AnounceTimeSeconds.Value));
            }
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _connection = _connectionFactory();
                    using var _ = token.Register(() => _connection.Disconnect());
                    var connectionTask = _connection.Connect(_config.TwitchTvCredentials.Nickname, _config.TwitchTvCredentials.Password);
                    _chat = await _connection.JoinChannel(_config.ChannelName);
                    // get messages
                    await foreach (var message in _chat.RecvMessages())
                    {
                        await HandleMessage(message);
                    }
                    await connectionTask;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Console.WriteLine(e); continue; }
                finally
                {
                    try { await _connection.LeaveChannel(_config.ChannelName); }
                    catch { }
                    _connection.Terminate();
                    _connection.Dispose();
                }
            }
            _completion.TrySetResult();
        }
        public void Start()
        {
            _ = StartInternal();
        }
        public async Task Wait()
        {
            await _completion.Task;
            // _stop.Dispose();
            // _stop = null;
        }
        public void Stop()
        {
            Console.WriteLine("-- stop called --");
            _stop?.Cancel();
        }
    }
}
