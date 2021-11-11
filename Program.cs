using System;
using System.IO;

namespace DfwcResultsBot
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<DfwcBotConfiguration>(File.ReadAllText("config.json"));
            var bot = new Bot(() => new TwitchConnection(), config);
            AppDomain.CurrentDomain.ProcessExit += (s, a) => bot.Stop();
            Console.CancelKeyPress += (s, a) =>
            {
                bot.Stop();
                a.Cancel = true;
            };
            bot.Start();
            bot.Wait().Wait();
        }
    }
}
