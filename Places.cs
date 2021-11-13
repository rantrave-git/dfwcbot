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
        // private (int, string) GetDemoName(Physics physics, string nickname)
        // {
        //     var target = _records[physics].Select((x, i) => (i, x)).FirstOrDefault(x => x.x.Nickname == nickname);
        //     if (target.x.Demo == null)
        //     {
        //         Console.WriteLine($"Player '{nickname}' has no demo in '{physics}'");
        //         return (0, null);
        //     }

        //     return (target.i, $"{physics.ToString().ToLowerInvariant()}/{target.x.Demo}");
        // }
        // private (int, Uri) GetDemoUrl(Physics physics, string nickname)
        // {
        //     var target = _records[physics].Select((x, i) => (i, x)).FirstOrDefault(x => x.x.Nickname == nickname);
        //     if (target.x.Ref == null)
        //     {
        //         Console.WriteLine($"Player '{nickname}' has no demo in '{physics}'");
        //         return (0, null);
        //     }

        //     return (target.i, new Uri(new Uri("https://dfwc.q3df.org"), $"/comp/dfwc2021/round/3/demo/{Uri.EscapeDataString(target.x.Demo)}"));
        // }
        private (int, string) GetPlaceAndDemo(Physics physics, string nickname)
        {
            var target = _records[physics].Select((x, i) => (i, x)).FirstOrDefault(x => x.x.Nickname == nickname);
            if (target.x.Demo == null)
            {
                Console.WriteLine($"Player '{nickname}' has no demo in '{physics}'");
                return (0, null);
            }
            return (target.i, target.x.Demo);
        }
        private string GetArchivePath(Physics physics, string demoname) => $"{physics.ToString().ToLowerInvariant()}/{demoname}";
        private Uri GetUri(int round, string demoname) => new Uri(new Uri("https://dfwc.q3df.org"), $"/comp/dfwc2021/round/{round}/demo/{Uri.EscapeDataString(demoname)}");
        public List<string> Top(Physics physics, int top) => _records[physics].Select(x => x.Nickname).Take(top).ToList();
        private async Task<bool> TryDownloadAndSave(HttpClient client, Uri uri, string targetName)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(1024 * 1024);

            try
            {
                for (int i = 0; i < 20; ++i)
                {
                    int total = 0;
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
                                total += len;
                                await fstream.WriteAsync(array, 0, len);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        File.Delete(targetName);
                        if (i == 19)
                        {
                            return false;
                        }
                        await Task.Delay(i * i * 10 + 10);
                        continue;
                    }
                    break;
                }
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        public async Task<List<(int, string)>> TryExtractAndSave(int round, Physics physics, List<(int, string)> demos, string dirpath)
        {
            var p = await _archivePath;
            if (p != null)
            {
                try
                {
                    using (ZipArchive z = ZipFile.OpenRead(p))
                    {
                        return demos.Select(x =>
                        {
                            var entry = z.GetEntry(GetArchivePath(physics, x.Item2));
                            if (entry == null) return x;
                            try
                            {
                                entry.ExtractToFile(Path.Combine(dirpath, $"{x.Item1 + 1:000}.dm_68"));
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                return x;
                            }
                            return (0, null);
                        }).Where(x => x.Item2 != null).ToList();
                    }
                }
                finally
                {
                    File.Delete(p);
                }
            }
            Console.WriteLine("No archive found");
            return demos;
        }
        public List<(int, string)> ListDemos(Physics physics, List<string> requiredNicks, List<string> votedNicks, int maxNumber)
        {
            var reqs = requiredNicks.Select(x => GetPlaceAndDemo(physics, x)).Where(x => x.Item2 != null).ToList();
            var vots = votedNicks.Where(x => !requiredNicks.Contains(x)).Select((x, i) =>
                {
                    var s = GetPlaceAndDemo(physics, x);
                    return (i, s.Item1, s.Item2);
                }).Where(x => x.Item3 != null)
                .OrderBy(x => (x.Item1, x.Item2))
                .Take(maxNumber - reqs.Count)
                .Select(x => (x.Item2, x.Item3));

            return reqs.Concat(vots).ToList();
        }
        public async Task<List<(int, string)>> TryDownload(HttpClient client, int round, Physics physics, List<(int, string)> demos, string dirpath)
        {
            var failed = await Task.WhenAll(demos.Select(async (x, i) =>
            {
                await Task.Delay(i * 500);
                if (await TryDownloadAndSave(client, GetUri(round, x.Item2), Path.Combine(dirpath, $"{x.Item1 + 1:000}.dm_68")))
                {
                    return (0, null);
                }
                return x;
            }));
            return failed.Where(x => x.Item2 != null).ToList();
        }
    }
}
