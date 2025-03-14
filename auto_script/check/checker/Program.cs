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
            client.Timeout = TimeSpan.FromSeconds(10);

            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*.yaml", SearchOption.AllDirectories))
            {
                try
                {
                    YamlStream yaml = [];
                    using StreamReader reader = new(filePath);
                    yaml.Load(reader);

                    YamlMappingNode rootNode = (YamlMappingNode)yaml.Documents[0].RootNode;
                    HashSet<string> urls = FindUrls(rootNode, failureLevel);

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
                                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && filePath.Contains("installer.yaml"))
                                {
                                    if (url.EndsWith(".exe") || url.EndsWith(".zip") || url.EndsWith(".msi") || url.EndsWith(".msix") || url.EndsWith(".appx"))
                                    {
                                        Console.WriteLine($"\n[Error] (安装程序返回 404) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Not found - 未找到)");
                                        Environment.Exit(1);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"\n[Warning] (安装程序? 返回 404) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Not found - 未找到)");
                                        if (failureLevel == "warning")
                                        {
                                            Environment.Exit(1);
                                        }
                                    }
                                }
                                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && !filePath.Contains("installer.yaml"))
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
                            if (failureLevel == "warning" || filePath.Contains("installer.yaml"))
                            {
                                Console.WriteLine($"\n[Error] {filePath} 中的 {url} 无效: {e.Message}");
                                Environment.Exit(1);
                            }
                            else
                            {
                                Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 无效: {e.Message}");
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"\n[Error] {filePath} 中的 {url} 发生错误: {e.Message}");
                            Environment.Exit(1);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"\n[Error] 处理文件 {filePath} 时发生错误: {e.Message}");
                    Environment.Exit(1);
                }
            }
        }

        static HashSet<string> FindUrls(YamlNode node, string failureLevel)
        {
            HashSet<string> urls = [];
            if (node is YamlMappingNode mappingNode)
            {
                foreach (KeyValuePair<YamlNode, YamlNode> entry in mappingNode.Children)
                {
                    HashSet<string> must_check_manifest_keys =
                    [
                        "InstallerUrl", "ReturnResponseUrl"
                    ];

                    HashSet<string> all_manifest_keys =
                    [
                        .. must_check_manifest_keys,
                        "PublisherUrl", "PublisherSupportUrl", "PrivacyUrl", "PackageUrl", "LicenseUrl", 
                        "CopyrightUrl", "AgreementUrl", "DocumentUrl", "ReleaseNotesUrl", "PurchaseUrl"
                    ];

                    // 判断 entry.Key 是否为 YamlScalarNode 且 keyNode.Value 是否不为空
                    if (entry.Key is YamlScalarNode keyNode && keyNode.Value != null)
                    {
                        bool flag;

                        // 根据 failureLevel 判断需要检查的集合
                        if (failureLevel == "warning")
                        {
                            // 如果是 warning，检查所有 URL
                            flag = all_manifest_keys.Contains(keyNode.Value);
                        }
                        else
                        {
                            // 否则，始终检查 InstallerUrl 和 ReturnResponseUrl
                            flag = must_check_manifest_keys.Contains(keyNode.Value);
                        }
#if DEBUG
                        Console.WriteLine($"遍历到 {keyNode.Value} 键，值 {entry.Value}，标记为 {flag}...");
#endif

                        // 如果 flag 为 true，检查 entry.Value 是否为 YamlScalarNode 且非 null
                        if (flag && entry.Value is YamlScalarNode scalarNode && scalarNode.Value != null)
                        {
                            // 如果没被忽略
                            if (!IsExcluded(scalarNode.Value))
                            {
                                // 向 urls 添加键的值
                                urls.Add(scalarNode.Value);
                            }
                        }
                        // 无论是否处理当前键，都递归处理值节点以查找深层URL
                        urls.UnionWith(FindUrls(entry.Value, failureLevel));
                    }
                    else
                    {
                        // 如果是非标量节点，递归查找 urls
                        urls.UnionWith(FindUrls(entry.Value, failureLevel));
                    }
                }
            }
            else if (node is YamlSequenceNode sequenceNode)
            {
                foreach (YamlNode item in sequenceNode.Children)
                {
                    urls.UnionWith(FindUrls(item, failureLevel));
                }
            }
            return urls;
        }

        static bool IsExcluded(string url)
        {
            HashSet<string> excludedDomains =
            [
                // "123", "360", "effie", "typora", "tchspt", "mysql", "iflyrec", "jisupdf", "floorp", "https://pot.pylogmon", // 之前忽略的
                "https://www.betterbird.eu/", "https://software.sonicwall.com/GlobalVPNClient/GVCSetup32.exe", "https://github.com/coq/platform/releases/", // 过于复杂
                "https://github.com/paintdotnet/release/", "https://cdn.kde.org/ci-builds/education/kiten/master/windows/", // 更新时移除
                "https://cdn.krisp.ai", "https://www.huaweicloud.com/", "https://mirrors.kodi.tv", "https://scache.vzw.com", "https://acessos.fiorilli.com.br/api/instalacao/webextension.exe", "https://www.magicdesktop.com/get/kiosk?src=winget", "https://raw.githubusercontent.com/jazzdelightsme/WingetPathUpdater/v1.2/WingetPathUpdaterInstall.ps1)", "https://dl.makeblock.com/", "https://download.voicecloud.cn/", // 假404
                "https://www.deezer.com/", // 无法验证
                "https://b2.zczc.men/file/dos-electron-assets/release/", "https://sf1-cdn-tos.douyinstatic.com/", "https://download.hellofont.cn/Client/Release/channels_backup/4.1.1/official/", "https://alistatic.lanhuapp.com/Axure/", // 不完整的url
                "https://sourceforge.net/", // 假403
                "https://download.effie.co/effie/effie_setup_4.3.5.exe", // WIP - 237374
            ];
            return excludedDomains.Any(domain => url.Contains(domain));
        }
    }
}
