using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DfwcResultsBot
{
    class VoteCounter
    {
        ConcurrentDictionary<Physics, ConcurrentDictionary<string, string>> _votes = new ConcurrentDictionary<Physics, ConcurrentDictionary<string, string>>();
        public VoteCounter()
        {
            _votes[Physics.Vq3] = new ConcurrentDictionary<string, string>();
            _votes[Physics.Cpm] = new ConcurrentDictionary<string, string>();
        }
        public string AddVote(Physics physics, string user, string player)
        {
            string oldValue = null;
            _votes[physics].AddOrUpdate(user, (k) => player, (k, old) => { oldValue = old; return player; });
            return oldValue;
        }

        public async Task Save(Physics physics, string filename)
        {
            using (var s = File.OpenWrite(filename))
            using (var sw = new StreamWriter(s))
            {
                foreach (var i in _votes[physics])
                {
                    await sw.WriteLineAsync($"{i.Key}\t{i.Value}");
                }
            }
        }
        public async Task Load(Physics physics, string filename)
        {
            _votes[physics].Clear();
            if (!File.Exists(filename)) return;
            using (var s = File.OpenRead(filename))
            using (var sw = new StreamReader(s))
            {
                while (true)
                {
                    var line = await sw.ReadLineAsync();
                    if (line == null) break;
                    try
                    {
                        var q = line.Split('\t', 2);
                        if (q.Length < 2) continue;
                        _votes[physics][q[0]] = q[1];
                    }
                    catch { } // ignore invalid lines
                }
            }
        }

        public List<(string, int)> Summup(Physics physics, Dictionary<string, int> userWeigths)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>();
            foreach (var (k, v) in _votes[physics])
            {
                totals[v] = totals.GetValueOrDefault(v, 0) + userWeigths.GetValueOrDefault(k, 1);
            }
            return totals.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
        }
    }
}
