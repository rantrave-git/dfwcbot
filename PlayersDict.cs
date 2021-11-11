using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;

namespace DfwcResultsBot
{
    class PlayersDict
    {
        public List<string> _nicknames;

        public bool FindNickname(string nickname, out string actualname, out string message)
        {
            var nicknames = FindNickname(nickname, out var imconfidence);
            actualname = null;
            if (nicknames.Count == 0)
            {
                message = $"Player {nickname} is not found.";
            }
            else if (nicknames.Count == 1)
            {
                if (imconfidence < 3)
                {
                    actualname = _nicknames[nicknames.First()];
                    message = $"Voted for {actualname}.";
                    return true;
                }
                else
                {
                    message = $"Player {nickname} is not found. Maybe it's {_nicknames[nicknames.First()]}?";
                }
            }
            else if (nicknames.Count < 5)
            {
                message = $"Player '{nickname}' is not found. Probably you mean one of these: {String.Join(", ", nicknames.Select(x => _nicknames[x]))}";
            }
            else
            {
                message = $"Player '{nickname}' is not found. Try to be more precise. List of all players at: https://dfwc.q3df.org/comp/dfwc2021/standings.html";
            }
            return false;
        }
        private int Levenstein(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;
            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
        private string RemoveWhitespace(string src) => String.Join("", src.Where(y => y != ' ' && y != '\t' && y != '\n'));
        private string RemovePunctuation(string src) => String.Join("", src.Where(y => y != '.' && y != '!' && y != '?' && y != ';' && y != ':'));
        private string ReplaceDigits(string src) => src.Replace('0', 'o').Replace('3', 'e').Replace('!', 'i').Replace('1', 'i').Replace('4', 'a').Replace('$', 's');
        private List<int> FindNickname(string part, out int imconfidence)
        {
            var p = part.Trim().Replace("'", "").Replace("\"", "");
            var pp = RemoveWhitespace(p.ToLowerInvariant());
            var selection = _nicknames.Select((x, i) =>
            {
                var xx = x.Trim().Replace("'", "").Replace("\"", "");
                if (xx == p) return (i, 0);
                if (xx.ToLowerInvariant() == p.ToLowerInvariant()) return (i, 1);
                if (RemovePunctuation(xx) == RemovePunctuation(p)) return (i, 1);
                var rest = RemoveWhitespace(xx.ToLowerInvariant());
                if (rest == pp) return (i, 2);
                if (ReplaceDigits(rest) == pp) return (i, 2);
                if (ReplaceDigits(rest).Contains(pp)) return (i, 2);
                return (i, 2 + Math.Min(Levenstein(rest, pp), Levenstein(ReplaceDigits(rest), pp)));
            }).OrderBy(x => x.Item2).ToList();

            if (selection.Count == 0)
            {
                imconfidence = 100;
                return new List<int>();
            }
            var imc = imconfidence = selection.FirstOrDefault().Item2;
            return selection.Where(x => x.Item2 == imc).Select(x => x.i).ToList();
        }
        private Dictionary<string, char> DecodeTable = new Dictionary<string, char>()
        {
            ["rbrack"] = '\x005D',
            ["lbrack"] = '\x005B',
            ["excl"] = '\x0021',
            ["num"] = '\x0023',
            ["dollar"] = '\x0024',
            ["percnt"] = '\x0025',
            ["amp"] = '\x0026',
            ["quot"] = '\x0027',
            ["lpar"] = '\x0028',
            ["rpar"] = '\x0029',
            ["midast"] = '\x002A',
            ["plus"] = '\x002B',
            ["comma"] = '\x002C',
            ["hyphen"] = '\x002D',
            ["period"] = '\x002E',
            ["sol"] = '\x002F',
            ["colon"] = '\x003A',
            ["semi"] = '\x003B',
            ["lt"] = '\x003C',
            ["equals"] = '\x003D',
            ["gt"] = '\x003E',
            ["quest"] = '\x003F',
            ["commat"] = '\x0040',
            ["lsqb"] = '\x005B',
            ["bsol"] = '\x005C',
            ["rsqb"] = '\x005D',
            ["circ"] = '\x005E',
            ["lowbar"] = '\x005F',
            ["grave"] = '\x0060',
            ["lcub"] = '\x007B',
            ["verbar"] = '\x007C',
            ["rcub"] = '\x007D',
            ["tilde"] = '\x007E',
            ["nbsp"] = '\x00A0',
            ["iexcl"] = '\x00A1',
            ["cent"] = '\x00A2',
            ["pound"] = '\x00A3',
            ["curren"] = '\x00A4',
            ["yen"] = '\x00A5',
            ["brvbar"] = '\x00A6',
            ["sect"] = '\x00A7',
            ["Dot"] = '\x00A8',
            ["copy"] = '\x00A9',
            ["ordf"] = '\x00AA',
            ["laquo"] = '\x00AB',
            ["not"] = '\x00AC',
            ["shy"] = '\x00AD',
            ["reg"] = '\x00AE',
            ["macr"] = '\x00AF',
            ["deg"] = '\x00B0',
            ["plusmn"] = '\x00B1',
            ["sup2"] = '\x00B2',
            ["sup3"] = '\x00B3',
            ["acute"] = '\x00B4',
            ["micro"] = '\x00B5',
            ["para"] = '\x00B6',
            ["middot"] = '\x00B7',
            ["cedil"] = '\x00B8',
            ["sup1"] = '\x00B9',
            ["ordm"] = '\x00BA',
            ["raquo"] = '\x00BB',
            ["frac14"] = '\x00BC',
            ["frac12"] = '\x00BD',
            ["frac34"] = '\x00BE',
            ["iquest"] = '\x00BF',
            ["Agrave"] = '\x00C0',
            ["Aacute"] = '\x00C1',
            ["Acirc"] = '\x00C2',
            ["Atilde"] = '\x00C3',
            ["Auml"] = '\x00C4',
            ["Aring"] = '\x00C5',
            ["AElig"] = '\x00C6',
            ["Ccedil"] = '\x00C7',
            ["Egrave"] = '\x00C8',
            ["Eacute"] = '\x00C9',
            ["Ecirc"] = '\x00CA',
            ["Euml"] = '\x00CB',
            ["Igrave"] = '\x00CC',
            ["Iacute"] = '\x00CD',
            ["Icirc"] = '\x00CE',
            ["Iuml"] = '\x00CF',
            ["ETH"] = '\x00D0',
            ["Ntilde"] = '\x00D1',
            ["Ograve"] = '\x00D2',
            ["Oacute"] = '\x00D3',
            ["Ocirc"] = '\x00D4',
            ["Otilde"] = '\x00D5',
            ["Ouml"] = '\x00D6',
            ["times"] = '\x00D7',
            ["Oslash"] = '\x00D8',
            ["Ugrave"] = '\x00D9',
            ["Uacute"] = '\x00DA',
            ["Ucirc"] = '\x00DB',
            ["Uuml"] = '\x00DC',
            ["Yacute"] = '\x00DD',
            ["THORN"] = '\x00DE',
            ["szlig"] = '\x00DF',
            ["agrave"] = '\x00E0',
            ["aacute"] = '\x00E1',
            ["acirc"] = '\x00E2',
            ["atilde"] = '\x00E3',
            ["auml"] = '\x00E4',
        };
        private string Decode(string s)
        {
            char[] result = ArrayPool<char>.Shared.Rent(s.Length);
            var last = 0;
            var stpos = 0;
            for (int i = 0; i < s.Length; ++i)
            {
                if (s[i] == '&')
                {
                    stpos = i;
                    while (i < s.Length)
                    {
                        i++;
                        if (s[i] == ';') break;
                    }
                    if (i - stpos < 1) continue;
                    result[last++] = DecodeTable[s.Substring(stpos + 1, i - stpos - 1)];
                    continue;
                }
                result[last++] = s[i];
            }
            try
            {
                return result.AsSpan(0, last).ToString();
            }
            finally
            {
                ArrayPool<char>.Shared.Return(result);
            }
        }
        private List<(string, string, string, string, int)> GetRecords(IEnumerable<HtmlNode> tables, Physics physics)
        {
            var table = tables.FirstOrDefault(x => x.ChildNodes.Any(y => y.Name == "caption" && y.ChildNodes.Any(z => z.InnerHtml.ToLowerInvariant() == physics.ToString().ToLowerInvariant())));
            var recs = table?.Descendants("tr").Skip(1)
                .Select((x, i) =>
                {
                    var name = HttpUtility.HtmlDecode(x.Descendants("td").Skip(1).FirstOrDefault()?.InnerText.Trim());//String.Join("", <>  First().Descendants("span").Select(y => y.InnerText));
                    var demoa = x.Descendants("td").Skip(2).FirstOrDefault()?.Descendants("a").First();
                    if (demoa != null)
                    {
                        var demoref = Decode(demoa.GetAttributeValue("href", "")); //Uri.UnescapeDataString();
                        var demoname = demoref.Split('/').LastOrDefault();
                        var time = HttpUtility.HtmlDecode(demoa.InnerText);
                        return (name, demoname, demoref, time, i);
                    }
                    return (null, null, null, null, -1);
                }).Where(x => x.name != null);

            return recs?.ToList();
        }
        public async Task LoadNicknames()
        {
            using var hc = new HttpClient();
            using var response = await hc.GetAsync($"https://dfwc.q3df.org/comp/dfwc2021/players.html");
            var page = await response.Content.ReadAsStringAsync();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(page);
            var table = htmlDoc.DocumentNode.Descendants("table").Where(x => x.HasClass("players-table")).FirstOrDefault();
            _nicknames = table.Descendants("tr").Skip(1).Select(x =>
            {
                if (!int.TryParse(HttpUtility.HtmlDecode(x.Descendants("td").FirstOrDefault()?.InnerText.Trim()), out var _)) return null;
                return HttpUtility.HtmlDecode(x.Descendants("td").Skip(1).First().InnerText.Trim());
            }).Where(x => x != null && x != "?").ToList();

        }
        private async Task<string> LoadAllDemos(HttpClient hc, int round, CancellationToken cancellationToken = default)
        {
            using var arch = await hc.GetAsync($"https://dfwc.q3df.org/comp/dfwc2021/round/{round}/demo-pack/all_players");
            var p = Path.Combine(Path.GetTempPath(), $"df-{Path.GetRandomFileName()}");
            Directory.CreateDirectory(p);
            var fname = Path.Combine(p, $"dm{round}.zip");
            byte[] array = ArrayPool<byte>.Shared.Rent(16 * 1024 * 1024);
            try
            {
                using (var fstream = File.OpenWrite(fname))
                using (var s = await arch.Content.ReadAsStreamAsync())
                {
                    while (true)
                    {
                        int len = await s.ReadAsync(array, cancellationToken);
                        if (len == 0) break;
                        await fstream.WriteAsync(array, 0, len, cancellationToken);
                    }
                }
                return fname;
            }
            catch (Exception)
            {
                Directory.Delete(p, true);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        public async Task<Places> LoadPlaces(int round, bool loadArchive = false)
        {
            using var hc = new HttpClient();
            var archive = loadArchive ? LoadAllDemos(hc, round) : Task.FromResult<string>(null);
            using var response = await hc.GetAsync($"https://dfwc.q3df.org/comp/dfwc2021/round/{round}/");
            var page = await response.Content.ReadAsStringAsync();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(page);

            var nodes = htmlDoc.DocumentNode.Descendants("table").Where(
                x => x.ChildNodes.Any(
                    y => y.Name == "caption" && y.ChildNodes.Any(
                        z => z.InnerHtml.ToLowerInvariant() == "vq3" || z.InnerHtml.ToLowerInvariant() == "cpm"
                    )
                )
            ).ToList();
            if (nodes.Count == 0) return null;
            return new Places(round, GetRecords(nodes, Physics.Vq3), GetRecords(nodes, Physics.Cpm), archive);
        }
    }
}
