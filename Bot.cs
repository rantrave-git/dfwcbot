using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
        public string PrivatePrefix { get; set; } = "/w ";
        public string Command { get; set; } = "vote";
        public bool UseArchive { get; set; } = false;
        public string ChannelName { get; set; }
        public string ExtractDirectory { get; set; }
        public string[] Superusers { get; set; }
        public SelectionConfiguration Vq3 { get; set; }
        public SelectionConfiguration Cpm { get; set; }
        public Dictionary<string, int> UserWeights { get; set; }
        public Credentials Credentials { get; set; }
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
        private async Task DoExtract(int round, Physics physics)
        {
            var required = _config.GetPhysicsConfig(physics).RequiredPlayers
                .Select(x => _players.FindNickname(x, out var y, out _) ? y : null)
                .Where(x => x != null).Union(_places.Top(physics, _config.GetPhysicsConfig(physics).RequiredTop)).ToList();
            var voted = _voteCounter.Summup(physics, _config.UserWeights);
            await _places.Extract(round, physics, required.ToList(),
                voted.Select(x => x.Item1).ToList(), _config.ExtractDirectory,
                _config.GetPhysicsConfig(physics).TotalDemos);
        }
        private async Task PerformExtraction(string initiator, int round)
        {
            try
            {
                await Task.WhenAll(DoExtract(round, Physics.Vq3), DoExtract(round, Physics.Cpm));
                if (File.Exists(RunningFilename)) File.Delete(RunningFilename);
            }
            catch (Exception e)
            {
                await _chat?.SendMessage($"/w {initiator} sorry but unable to download yet. Error log's saved.");
                await File.WriteAllTextAsync(".log", e.ToString());

                _round = round;
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
                            await Task.WhenAll(
                                _voteCounter.Load(Physics.Vq3, ".none"),
                                _voteCounter.Load(Physics.Cpm, ".none")
                            );
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
                            _places = await _players.LoadPlaces(_round, _config.UseArchive);
                            if (_places == null)
                            {
                                await _chat?.SendMessage($"{privatePrefix}{message.Sender} results are not ready yet");
                                return;
                            }
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
        private async Task SaveStates()
        {
            while (!_stop.Token.IsCancellationRequested)
            {
                await Task.Delay(4000);
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _connection = _connectionFactory();
                    var connectionTask = _connection.Connect(_config.Credentials.Nickname, _config.Credentials.Password);
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
                    await _connection.Disconnect();
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
