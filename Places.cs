using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DfwcResultsBot
{
    class Places
    {
        private int _round;
        private Dictionary<Physics, List<(string Nickname, string Demo, string Ref, int Place)>> _records;
        private Task<string> _archivePath;
        public Places(int round, List<(string Nickname, string Demo, string Ref, int Place)> vq3,
            List<(string Nickname, string Demo, string Ref, int Place)> cpm, Task<string> archivePath)
        {
            _round = round;
            _records = new Dictionary<Physics, List<(string Nickname, string Demo, string Ref, int Place)>>();
            _records[Physics.Vq3] = vq3;
            _records[Physics.Cpm] = cpm;
            _archivePath = archivePath;
        }
        public void _SetArchivePath(string path) => _archivePath = Task.FromResult<string>(path);
        private (int, string) GetDemoName(Physics physics, string nickname)
        {
            var target = _records[physics].Select((x, i) => (i, x)).FirstOrDefault(x => x.x.Nickname == nickname);
            if (target.x.Demo == null)
            {
                Console.WriteLine($"Player '{nickname}' has no demo in '{physics}'");
                return (0, null);
            }

            return (target.i, $"{physics.ToString().ToLowerInvariant()}/{target.x.Demo}");
        }
        private (int, Uri) GetDemoUrl(Physics physics, string nickname)
        {
            var target = _records[physics].Select((x, i) => (i, x)).FirstOrDefault(x => x.x.Nickname == nickname);
            if (target.x.Ref == null)
            {
                Console.WriteLine($"Player '{nickname}' has no demo in '{physics}'");
                return (0, null);
            }

            return (target.i, new Uri(new Uri("https://dfwc.q3df.org"), target.x.Ref));
        }
        public List<string> Top(Physics physics, int top) => _records[physics].Select(x => x.Nickname).Take(top).ToList();
        private async Task DownloadAndSave(HttpClient client, Uri uri, string targetName)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(1024 * 1024);

            try
            {
                for (int i = 0; i < 20; ++i)
                {
                    try
                    {
                        using (var response = await client.GetAsync(uri))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fstream = File.OpenWrite(targetName))
                        {
                            response.EnsureSuccessStatusCode();
                            while (true)
                            {
                                var len = await stream.ReadAsync(array);
                                if (len == 0) break; // download successfull
                                await fstream.WriteAsync(array, 0, len);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        if (i == 19) throw;
                        await Task.Delay(i * i * 10 + 10);
                        continue;
                    }
                    break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        public async Task Extract(HttpClient client, int round, Physics physics, List<string> requiredNicks, List<string> votedNicks, string destination, int maxNumber)
        {
            var dirpath = Path.Combine(destination, $"round{round}", physics.ToString().ToLowerInvariant());
            if (!Directory.Exists(dirpath)) Directory.CreateDirectory(dirpath);
            var p = await _archivePath;
            if (p != null)
            {
                using (ZipArchive z = ZipFile.OpenRead(p))
                {
                    var reqs = requiredNicks.Select(x => GetDemoName(physics, x)).ToList();
                    var vots = votedNicks.Where(x => !requiredNicks.Contains(x)).Select((x, i) =>
                    {
                        var s = GetDemoName(physics, x);
                        return (i, s.Item1, s.Item2);
                    }).OrderBy(x => (x.Item1, x.Item2)).Take(maxNumber - reqs.Count).Select(x => (x.Item2, x.Item3)).ToList();
                    foreach (var (ind, demo) in reqs.Concat(vots).Where(x => x.Item2 != null))
                    {
                        z.GetEntry(demo).ExtractToFile(Path.Combine(dirpath, $"{ind + 1:000}.dm_68"));
                    }

                    Console.WriteLine(String.Join("\n", z.Entries.Select(x => x.Name)));
                }
                File.Delete(p);
            }
            else
            {
                var reqs = requiredNicks.Select(x => GetDemoUrl(physics, x)).ToList();
                var vots = votedNicks.Where(x => !requiredNicks.Contains(x)).Select((x, i) =>
                  {
                      var s = GetDemoUrl(physics, x);
                      return (i, s.Item1, s.Item2);
                  }).OrderBy(x => (x.Item1, x.Item2)).Take(maxNumber - reqs.Count).Select(x => (x.Item2, x.Item3)).ToList();

                await Task.WhenAll(reqs.Concat(vots).Where(x => x.Item2 != null).Select(x => DownloadAndSave(client, x.Item2, Path.Combine(dirpath, $"{x.Item1 + 1:000}.dm_68"))));
            }
        }
    }
}
