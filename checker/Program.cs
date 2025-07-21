using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
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
#if DEBUG
                Console.WriteLine($"[Debug] 检查目录 {folderPath}");
#endif
                Console.WriteLine("[Error] 指定的检查目录不存在");
                return;
            }

            string failureLevel;
            // ================================
            //           错误等级说明
            // 错误: 只检查 InstallerUrl 和 ReturnResponseUrl。忽略在非安装程序清单中检查到的 403 Forbidden 警告。
            // 详细: 检查所有可能的 URL 后依据是否有错误或警告决定工作流是否失败。
            // 不失败: (在工作流文件中) 使工作流不会失败。以 默认 等级检查 URL。
            // 默认: 检查所有可能的 URL 后依据是否有错误决定工作流是否失败。只有警告不会使工作流失败。
            // ================================
            if (args.Length >= 2)
            {
                failureLevel = args[1];
            }
            else
            {
#if DEBUG
                Console.WriteLine("[Debug] 失败级别获取失败，使用 默认 错误等级");
#endif
                failureLevel = "默认";
            }

            // 最大并发数
            int maxConcurrency = 8;
            if (args.Length == 3)
            {
                if (!int.TryParse(args[2], out maxConcurrency))
                {
                    Console.WriteLine("[Warning] 指定的最大并发数无效，默认为 8。");
                    maxConcurrency = 8;
                }
            }
            else
            {
#if DEBUG
                Console.WriteLine("[Debug] 最大并发数未定义，默认为 8");
#endif
            }

            if (await CheckUrlsInYamlFilesParallel(folderPath, failureLevel, maxConcurrency))
            {
                Console.WriteLine("\n所有检查的链接正常");
            }
            else
            {
                Environment.Exit(1);
            }
        }

        internal static readonly string[] installerType = [".exe", ".zip", ".msi", ".msix", ".appx", "download", ".msixbundle"];
        // &download 为 sourceforge 和类似网站的下载链接

        private static async Task<bool> CheckUrlsInYamlFilesParallel(string folderPath, string failureLevel, int maxConcurrency)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            client.Timeout = TimeSpan.FromSeconds(15);

            bool failed = false; // 在失败模式 默认 下的标记
            var allUrls = new List<(string filePath, string url)>();

            Console.WriteLine("[INFO] 正在查找 URL...");

            // 先收集所有 URL
            HashSet<string> urlSet = [];
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
                        // 只添加未出现过的 url
                        if (urlSet.Add(url) || failureLevel == "详细")
                        {
                            allUrls.Add((filePath, url));
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"\n[Error] 处理文件 {filePath} 时发生错误: {e.Message}");
                    failed = true;
                }
            }

            Console.WriteLine("[INFO] 查找 URL 完毕");

            // 再并发检查所有 URL
            var semaphore = new SemaphoreSlim(maxConcurrency); // 控制最大并发数
            var urlTasks = allUrls.Select(async tuple =>
            {
                await semaphore.WaitAsync();
                try
                {
                    bool result = await CheckUrlAsync(client, tuple.filePath, tuple.url, failureLevel);
                    if (!result)
                    {
                        failed = true;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(urlTasks);

            return !failed;
        }

        static string GetPackageIdentifier(string filePath)
        {
            // 传入路径，先获取文件名
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            // 此时，有这三种可能
            // xxx.xxx
            // xxx.xxx.installer
            // xxx.xxx.locale.<区域>

            // 判断文件名是否包含 .installer/.locale，然后获取最后一个 .installer/.locale 前的内容
            if (fileName.Contains(".installer"))
            {
                return fileName[..fileName.LastIndexOf(".installer")];
            }
            else if (fileName.Contains(".locale"))
            {
                return fileName[..fileName.LastIndexOf(".locale")];
            }
            else
            {
                return fileName;
            }
        }

        static void GetFrequentlyFailingPackageHint(string filePath)
        {
            Dictionary<string, string> FrequentlyFailingPackages = new()
            {
                ["calibre.calibre.portable"] = "有个笨蛋经常使用 GitHub Release 的链接，GitHub Release 的链接只保留最新版本的文件，应改为 https://download.calibre-ebook.com/x.y.z/calibre-portable-installer-x.y.z.exe",
                ["7S2P.Effie.CN"] = "包发布者经常移除此包的旧版本",
                ["AppByTroye.KoodoReader"] = "在一段时间后，发布者会删除旧版本",
                // GeoGebra
                ["GeoGebra.GraphingCalculator"] = "在一段时间后，发布者会删除多个旧版本",
                ["GeoGebra.Classic"] = "在一段时间后，发布者会删除多个旧版本",
                ["GeoGebra.Geometry"] = "在一段时间后，发布者会删除多个旧版本",
                // ========
            };
            if (FrequentlyFailingPackages.TryGetValue(GetPackageIdentifier(filePath), out string? hint))
            {
                Console.WriteLine($"这是常失败软件包: {hint}");
            }
        }

        static async Task<bool> CheckUrlAsync(HttpClient client, string filePath, string url, string failureLevel)
        {
            try
            {
                HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if ((int)response.StatusCode >= 400)
                {
                    response = await client.GetAsync(url);
                }

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    if ((response.StatusCode == System.Net.HttpStatusCode.NotFound) || (response.StatusCode == System.Net.HttpStatusCode.Gone))
                    {
                        string message = response.StatusCode switch
                        {
                            System.Net.HttpStatusCode.NotFound => "NotFound - 未找到",
                            System.Net.HttpStatusCode.Gone => "Gone - 永久移除",
                            _ => "Unknown - 未知"
                        };

                        if (filePath.Contains("installer.yaml"))
                        {
                            if (installerType.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            {
                                Console.WriteLine($"\n[Error] (安装程序返回 {(int)response.StatusCode}) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} ({message})");
                                Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))}");
                                GetFrequentlyFailingPackageHint(filePath);
                                return false;
                            }
                            else
                            {
                                Console.WriteLine($"\n[Warning] (安装程序? 返回 {(int)response.StatusCode}) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} ({message})");
                                Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))}");
                                GetFrequentlyFailingPackageHint(filePath);
                                if (failureLevel == "详细")
                                {
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"\n[Warning] (一般链接返回 {(int)response.StatusCode}) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} ({message})");
                            Console.WriteLine($"[Hint] Sundry 命令: sundry modify {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))} \"(一般链接返回 {(int)response.StatusCode}) {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} ({message})\"");
                            if (failureLevel == "详细")
                            {
                                return false;
                            }
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        if (filePath.Contains("installer.yaml") || failureLevel != "错误")
                        {
                            Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Forbidden - 已禁止)");
                            Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))} \"It returns a 403 status code in GitHub Action.\"");
                            GetFrequentlyFailingPackageHint(filePath);
                            if (failureLevel == "详细")
                            {
                                return false;
                            }
                        }
                        else
                        {
                            Console.Write("-");
#if DEBUG
                            Console.WriteLine($"\n[Debug] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Forbidden - 已禁止)");
#endif
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Console.Write("-");
#if DEBUG
                        Console.WriteLine($"\n[Debug] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Too many requests - 请求过多)");
#endif
                        // 等待 1 秒钟以缓解请求过多的问题
                        await Task.Delay(1000);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                    {
                        // 抛出 TimeoutException 异常，并说明返回了 408，然后让下面的 catch 处理
                        throw new TimeoutException("Url 返回了状态码 408 (Request Timeout - 请求超时)");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                    {
                        Console.Write("-");
#if DEBUG
                        Console.WriteLine($"\n[Debug] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (Method Not Allowed - 方法不允许)");
#endif
                    }
                    else if ((int)response.StatusCode == 418)
                    {
                        // 418 I'm a teapot
                        Console.Write("-");
#if DEBUG
                        Console.WriteLine($"\n[Debug] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (I'm a teapot - 服务器拒绝冲泡咖啡，因为它一直都是茶壶)");
                        Console.WriteLine("[Debug] 这可能只是因为服务器不想处理我们的请求。");
#endif
                    }
                    else
                    {
                        Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (≥400 - 客户端错误)");
                        GetFrequentlyFailingPackageHint(filePath);
                        if (failureLevel == "详细")
                        {
                            Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))} \"It returns a {(int)response.StatusCode} (≥ 400) status code in GitHub Action.\"");
                            return false;
                        }
                    }
                }
                else if ((int)response.StatusCode >= 500)
                {
                    Console.Write("-");
#if DEBUG
                    Console.WriteLine($"\n[Debug] {filePath} 中的 {url} 返回了状态码 {(int)response.StatusCode} (≥500 - 服务端错误)");
#endif
                }
                else
                // 除了上面的 400 - 500
                {
                    // 如果这个 URL 是 HTTP 而不是 HTTPS 的
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        // 尝试使用 HTTPS 访问
                        string httpsUrl = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
                        try
                        {
                            HttpResponseMessage httpsResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, httpsUrl));
                            // 如果可以访问 (<400)
                            if ((int)httpsResponse.StatusCode < 400)
                            {
                                Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 不安全 (HTTP)，请使用安全 URL {httpsUrl} (HTTPS) 替代。");
                                Console.WriteLine($"[Hint] Sundry 命令: sundry modify {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))} \"{filePath} 中的 {url} 不安全 (HTTP)，请使用安全 URL {httpsUrl} (HTTPS) 替代。\"");
                                if (failureLevel == "详细")
                                {
                                    return false;
                                }
                            }
                        }
                        catch
                        {
                            // 忽略异常
                        }
                    }
                    Console.Write("*");
                }
            }
            catch (HttpRequestException e)
            {
                if (e.Message.Contains("Resource temporarily unavailable"))
                {
                    Console.Write("-");
#if DEBUG
                    Console.WriteLine($"\n[Debug] 访问 {filePath} 中的 {url} 时发生错误: {e.Message} (资源暂时不可用)");
                    // 定义的 e 无论如何都会在判断时使用，故无需丢弃
#endif
                }
                else if (e.Message.Contains("Name or service not known"))
                {
                    // 视作 404 Not Found 按错误处理
                    if (filePath.Contains("installer.yaml"))
                    {
                        if (installerType.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine($"\n[Error] (安装程序 Name or service not known) {filePath} 中的 {url} 域名或服务器未知 ({e.Message})");
                            Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))}");
                            return false;
                        }
                        else
                        {
                            Console.WriteLine($"\n[Warning] (安装程序? Name or service not known) {filePath} 中的 {url} 域名或服务器未知 ({e.Message})");
                            if (failureLevel == "详细")
                            {
                                Console.WriteLine($"[Hint] Sundry 命令: sundry remove {GetPackageIdentifier(filePath)} {Path.GetFileName(Path.GetDirectoryName(filePath))}");
                                return false;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"\n[Warning] (一般链接 Name or service not known) {filePath} 中的 {url} 域名或服务器未知 ({e.Message})");
                        if (failureLevel == "详细")
                        {
                            return false;
                        }
                    }
                }
                else if (e.Message.Contains("The SSL connection could not be established, see inner exception."))
                {
                    Console.Write("-");
#if DEBUG
                    Console.WriteLine($"\n[Debug] 无法访问 {filePath} 中的 {url} : {e.Message} - {e.InnerException?.Message ?? "没有内部异常"} (SSL 连接无法建立)");
                    // 定义的 e 无论如何都会在判断时使用，故无需丢弃
#endif
                }
                else if (e.Message.Contains("An error occurred while sending the request."))
                {
                    Console.Write("-");
#if DEBUG
                    Console.WriteLine($"\n[Debug] 无法访问 {filePath} 中的 {url} : {e.Message} - {e.InnerException?.Message ?? "没有内部异常"} (发送请求时发生错误)");
                    // 定义的 e 无论如何都会在判断时使用，故无需丢弃
#endif
                }
                else
                {
                    Console.WriteLine($"\n[Warning] 无法访问 {filePath} 中的 {url} : {e.Message} - {e.InnerException?.Message ?? "没有内部异常"}");
                    if (failureLevel == "详细")
                    {
                        return false;
                    }
                }
            }
            catch (TaskCanceledException e)
            {
                Console.Write("-");
#if DEBUG
                if (e.InnerException is TimeoutException)
                {
                    Console.WriteLine($"\n[Debug] 访问 {filePath} 中的 {url} 时超时: {e.Message}");
                }
                else
                {
                    Console.WriteLine($"\n[Debug] 访问 {filePath} 中的 {url} 时任务取消: {e.Message} ({e.InnerException?.Message ?? "没有内部异常"})");
                }
#else
                _ = e; // 非 Debug 模式下忽略 e 的定义以避免 CS0168 警告
#endif
            }
            catch (TimeoutException e)
            {
                Console.Write("-");
#if DEBUG
                Console.WriteLine($"\n[Debug] 访问 {filePath} 中的 {url} 时超时: {e.Message}");
#else
                _ = e; // 非 Debug 模式下忽略 e 的定义以避免 CS0168 警告
#endif
            }
            catch (UriFormatException e)
            {
                if (failureLevel == "详细" || filePath.Contains("installer.yaml"))
                {
                    Console.WriteLine($"\n[Error] {filePath} 中的 {url} 无效: {e.Message}");
                    return false;
                }
                else
                {
                    Console.WriteLine($"\n[Warning] {filePath} 中的 {url} 无效: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n[Error] {filePath} 中的 {url} 发生错误: {e.Message}");
                return false;
            }
            return true;
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
                        if (failureLevel == "详细")
                        {
                            // 如果是 详细，检查所有 URL
                            flag = all_manifest_keys.Contains(keyNode.Value);
                        }
                        else
                        {
                            // 否则，始终检查 InstallerUrl 和 ReturnResponseUrl
                            flag = must_check_manifest_keys.Contains(keyNode.Value);
                        }

                        // #if DEBUG
                        // Console.WriteLine($"遍历到 {keyNode.Value} 键，值 {entry.Value}，标记为 {flag}...");
                        // #endif
                        // 仅当清单很少时才建议启用此输出

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
                "https://www.betterbird.eu/", "https://software.sonicwall.com/GlobalVPNClient/GVCSetup32.exe", "https://github.com/coq/platform/releases/", "typora", "https://storage.jd.com/joymeeting-app/app/JoyMeeting.exe", "https://cdn.kde.org/", // 过于复杂
                "https://github.com/paintdotnet/release/", "https://cdn.kde.org/ci-builds/education/kiten/master/windows/", // 更新时移除
                "https://cdn.krisp.ai", "https://www.huaweicloud.com/", "https://mirrors.kodi.tv", "https://scache.vzw.com", "https://acessos.fiorilli.com.br/api/instalacao/webextension.exe", "https://www.magicdesktop.com/get/kiosk?src=winget", "https://dl.makeblock.com/", "https://download.voicecloud.cn/", "https://dl.jisupdftoword.com/", "123pan.com", "jisupdf.com", "jisupdfeditor.com", // 假404
                "https://www.deezer.com/", ".mil", "https://download.wondershare.com/cbs_down/", "https://catsxp.oss-cn-hongkong.aliyuncs.com/", // 无法验证
                "https://downloads.mysql.com/", "https://swcdn.apple.com/content/downloads/", "sourceforge.net", "https://static.centbrowser.cn/", "https://cdn1.waterfox.net/waterfox/releases/", "https://downloads.tableau.com/public/", "https://sp.thsi.cn/staticS3/mobileweb-upload-static-server.file/app_6/downloadcenter/THS_insoft", "https://files02.tchspt.com/down/", "https://cdn-dl.yinxiang.com/", "https://download.mono-project.com/archive/", "https://cdn-resource.aunbox.cn/", "https://www.fischertechnik.de/-/media/fischertechnik/fite/service/downloads/robotics/robo-pro/documents/update-robopro.ashx", "https://azcopyvnext-awgzd8g7aagqhzhe.b02.azurefd.net/releases/", "https://files03.tchspt.com/down/iview466_plugins_setup.exe", // 假403
                "https://issuepcdn.baidupcs.com/", "https://lf-luna-release.qishui.com/obj/luna-release/", "https://down.360safe.com/cse/", // 超时
                "https://www.argyllcms.com/", // 服务器拒绝冲泡咖啡
                "https://aurorabuilder.com/downloads/Aurora%20Setup.zip", // 假400
                "https://softpedia-secure-download.com/dl/8742021e52b6c4d3799cf12fa6dddc89/686a99d4/100021113/software/network/HostsMan_4.8.106_installer.zip", // 过于复杂 - https://github.com/microsoft/winget-pkgs/pull/271669#issue-3206719827
                "https://github.com/AlistGo", // 备受争议的发布者
            ];
            return excludedDomains.Any(url.Contains);
        }
    }
}
