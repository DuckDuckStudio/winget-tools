using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace checker
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("[Error] 没有指定需要检查的目录");
                return;
            }

            string folderPath = Path.Combine("winget-pkgs", "manifests", args[0]);
            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine("[Error] 指定的检查目录不存在");
                return;
            }

            string failureLevel;
            if (args.Length > 2)
            {
                failureLevel = args[2];
            }
            else
            {
                failureLevel = "error";
            }

            await CheckUrlsInYamlFiles(folderPath, failureLevel);
            Console.WriteLine("\n所有安装程序链接正常");
        }

        static async Task CheckUrlsInYamlFiles(string folderPath, string failureLevel)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");

            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*.yaml", SearchOption.AllDirectories))
            {
                try
                {
                    YamlStream yaml = [];
                    using StreamReader reader = new(filePath);
                    yaml.Load(reader);

                    YamlMappingNode rootNode = (YamlMappingNode)yaml.Documents[0].RootNode;
                    HashSet<string> urls = FindUrls(rootNode);

                    foreach (string url in urls)
                    {
                        try
                        {
                            HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                            {
                                response = await client.GetAsync(url);
                            }

                            if ((int)response.StatusCode >= 400)
                            {
                                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && filePath.Contains("installer"))
                                {
                                    Console.WriteLine($"\n[Error] (安装程序返回 404) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Not found - 未找到)");
                                    Environment.Exit(1);
                                }
                                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && !filePath.Contains("installer"))
                                {
                                    Console.Write("-");
                                }
                                else
                                {
                                    Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (≥400)\n");
                                    if (failureLevel == "warning")
                                    {
                                        Environment.Exit(1);
                                    }
                                }
                            }
                            else
                            {
                                Console.Write("*");
                            }
                        }
                        catch (HttpRequestException e)
                        {
                            Console.WriteLine($"\n[Warning] 无法访问 {filePath} 中的 {url} : {e.Message}");
                            if (failureLevel == "warning")
                            {
                                Environment.Exit(1);
                            }
                        }
                        catch (TaskCanceledException e)
                        {
                            Console.WriteLine($"\n[Warning] 访问 {filePath} 中的 {url} 时超时: {e.Message}");
                            if (failureLevel == "warning")
                            {
                                Environment.Exit(1);
                            }
                        }
                        catch (UriFormatException e)
                        {
                            if (failureLevel == "warning" || (url.EndsWith(".exe") || url.EndsWith(".zip") || url.EndsWith(".msi") || url.EndsWith(".msix") || url.EndsWith(".appx")))
                            {
                                Console.WriteLine($"\n[Error] {filePath} 中的 {url} 无效: {e.Message}");
                                Environment.Exit(1);
                            }
                            else
                            {
                                Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 无效: {e.Message}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"\n[Error] 处理文件 {filePath} 时发生错误: {e.Message}");
                    Console.WriteLine($"\n[TIP] 这还可能是未捕获的链接错误导致的");
                    Environment.Exit(1);
                }
            }
        }

        static HashSet<string> FindUrls(YamlNode node)
        {
            HashSet<string> urls = [];
            if (node is YamlMappingNode mappingNode)
            {
                foreach (KeyValuePair<YamlNode, YamlNode> entry in mappingNode.Children)
                {
                    if (entry.Value is YamlScalarNode scalarNode && scalarNode.Value != null)
                    {
                        MatchCollection foundUrls = Regex.Matches(scalarNode.Value, @"http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\\(\\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+");
                        foreach (Match match in foundUrls)
                        {
                            string url = match.Value;
                            if (!IsExcluded(url))
                            {
                                urls.Add(url);
                            }
                        }
                    }
                    else
                    {
                        urls.UnionWith(FindUrls(entry.Value));
                    }
                }
            }
            else if (node is YamlSequenceNode sequenceNode)
            {
                foreach (YamlNode item in sequenceNode.Children)
                {
                    urls.UnionWith(FindUrls(item));
                }
            }
            return urls;
        }

        static bool IsExcluded(string url)
        {
            HashSet<string> excludedDomains =
            [
                "123", "360", "effie", "typora", "tchspt", "mysql", "voicecloud", "iflyrec", "jisupdf", "floorp", "https://pot.pylogmon", // 之前忽略的
                "https://www.betterbird.eu/", // 过于复杂
                "https://dtapp-pub.dingtalk.com/dingtalk-desktop/win_installer/Release/DingTalk_v7.6.15.91110808.exe", "https://download.s21i.faiusr.com/4232652/0/0/ABUIABBPGAAgn-qPsgYovJen7gY.zip?f=", "https://builds.balsamiq.com/bwd/Balsamiq_Wireframes_4.8.1_x64_Setup.exe", "https://github.com/kovidgoyal/calibre/releases/download/v7.25.0/calibre-portable-installer-7.25.0.exe", "https://gitlab.com/ImaginaryInfinity/squiid-calculator/squiid/-/jobs/8854363723/artifacts/raw/squiid-installer.exe", // WIP
                "https://github.com/paintdotnet/release/", // 更新时移除
                "https://cdn.krisp.ai", "https://www.huaweicloud.com/", "https://mirrors.kodi.tv", "https://scache.vzw.com", "https://acessos.fiorilli.com.br/api/instalacao/webextension.exe", "https://www.magicdesktop.com/get/kiosk?src=winget", // 假404
                "https://www.deezer.com/", // 无法验证
            ];
            return excludedDomains.Any(domain => url.Contains(domain));
        }
    }
}
