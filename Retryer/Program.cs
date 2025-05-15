using System;
using System.Linq;
using Retryer.Methods;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;


namespace Retryer
{
    internal class Program
    {
        private static bool canceled; // Canceled vs Cancelled ? - https://learn.microsoft.com/zh-cn/dotnet/fundamentals/code-analysis/quality-rules/ca1805
        private static readonly ConsoleCancelEventHandler cancelHandler = static (sender, e) =>
        {
            Print.PrintWarning("收到取消请求，将在本 PR 处理完成后取消后续运行");
            e.Cancel = true;
            canceled = true;
        };

        // 使用
        // retryer [模式] [需要重试的拉取请求(空格分隔)]
        // 模式: auto(默认)、specify
        // 需要重试的拉取请求(空格分隔): 仅在模式为 specify 才需指定，可使用拉取请求完整 URL 或拉取请求 ID。

        private static async Task<int> Main(string[] args)
        {
            Print.PrintDebug($"获取到的参数: {string.Join(", ", args)} ({args.Length}个)");

            // 定义模式，默认为 auto
            string mode;

            switch (args[0].ToLowerInvariant())
            {
                // 依据第一个参数设置模式
                case "auto":
                case "自动":
                case "自动识别":
                case "自动查找":
                    mode = "auto";
                    break;
                case "specify":
                case "指定":
                case "手动指定":
                    mode = "specify";
                    break;
                default:
                    Print.PrintWarning("未定义重试模式，默认为 auto 模式。");
                    mode = "auto";
                    break;
            }

            // 从环境变量 GITHUB_LOGIN 或第二个参数中获取用户名，如果都没有则抛出异常
            string username = Environment.GetEnvironmentVariable("GITHUB_LOGIN") ?? args[1];
            if (string.IsNullOrWhiteSpace(username))
            {
                Print.PrintError("未指定 GitHub 用户名，请设置环境变量 GITHUB_LOGIN 或在命令行中指定。");
                return 1;
            }

            // 从环境变量 GITHUB_TOKEN 中获取 Token，如果没有则抛出异常
            string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                Print.PrintError("未指定 GitHub Token，请设置环境变量 GITHUB_TOKEN。");
                Print.PrintHint(@"在 GitHub Action 中使用时，请在工作流中添加您的 Token。
env:
    GITHUB_TOKEN: ${{ secrets.RETRY_TOKEN }}");
                return 1;
            }

            // 开始干正事
            // 定义一个列表，存放需要重试的拉取请求的 ID
            List<string> PullRequestsID = [];

            // 如果是自动模式
            if (mode == "auto")
            {
                // 自动从 FindPullRequests 获取需要重试的拉取请求的 ID
                PullRequestsID = await FindPullRequests(username, token);
            }
            else if (mode == "specify")
            {
                int startAt;

                // 如果是手动指定
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_LOGIN")))
                {
                    // 如果环境变量中有 GitHub 用户名，则表示不用指定第 2 个参数为用户名。
                    // 即拉取请求列表从第 2 个参数开始
                    // 数组索引从 0 开始，所以第 2 个参数的索引为 1
                    startAt = 1;
                }
                else
                {
                    // 否则拉取请求列表从第 3 个参数开始
                    // 数组索引从 0 开始，所以第 3 个参数的索引为 2
                    startAt = 2;
                }

                Print.PrintDebug($"从第 {startAt + 1} 个参数开始获取拉取请求 ID");

                for (int i = startAt; i < args.Length; i++)
                {
                    Print.PrintDebug($"检查参数 {i} 是否是有效的拉取请求 ID。");

                    int pullRequest = await ValidatePullRequest(args[i], mode, token);

                    if (pullRequest != 0)
                    {
                        Print.PrintDebug($"添加拉取请求 {pullRequest} 到重试列表。");
                        PullRequestsID.Add(pullRequest.ToString());
                    }
                    else
                    {
                        Print.PrintWarning($"需要重试的拉取请求 {args[i]} 无效，已忽略。");
                    }
                }
            }

            // 无论是什么方法获取的拉取请求 ID，都要在此步
            // 检查是否有获取到拉取请求 ID (PullRequestsID 是否为空)
            if (PullRequestsID.Count == 0)
            {
                // 这可能是个好消息？
                Print.PrintWarning("未获取到需要重试的拉取请求 ID。");
            }
            else
            {
                // 重试这些拉取请求
                int result = await RetryPullRequests(PullRequestsID, token);
                if (result == 2)
                {
                    Print.PrintError("操作被取消");
                }
                else if (result != 0)
                {
                    Print.PrintError("处理过程中出现了错误，参阅日志了解详情。");
                }
                else
                {
                    Console.WriteLine("处理完毕 🎉");
                }
                Console.CancelKeyPress -= cancelHandler; // 移除事件处理程序
            }

            return Environment.ExitCode;
        }

        // 定义一个方法，用于查找该用户在 microsoft/winget-pkgs 中的所有 打开的 拉取请求，并返回查找到的所有拉取请求的 ID
        // 接受 username 和 token 作为参数
        // 返回一个字符串数组，包含所有拉取请求的 ID
        private static async Task<List<string>> FindPullRequests(string username, string token)
        {
            Print.PrintInfo("正在查找可能需要重试的拉取请求...");

            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");

            try
            {
                // 获取 microsoft/winget-pkgs 中的所有 打开的 拉取请求
                // https://api.github.com/repos/microsoft/winget-pkgs/pulls?state=open&base=master
                // https://docs.github.com/zh/rest/pulls/pulls?apiVersion=2022-11-28#list-pull-requests
                // 需要分页

                string url = "https://api.github.com/repos/microsoft/winget-pkgs/pulls?state=open&base=master&per_page=100";
                string nextUrl = url;

                List<string> allPullRequests = [];

                while (!string.IsNullOrWhiteSpace(nextUrl))
                {
                    Print.PrintDebug($"请求 {nextUrl}");
                    HttpResponseMessage response = await client.GetAsync(nextUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        JsonNode? jsonNode = JsonNode.Parse(jsonResponse);
                        if (jsonNode == null || jsonNode.AsArray() == null)
                        {
                            Print.PrintError("无法解析 GitHub API 返回的 Json 数据。");
                            Print.PrintDebug($"返回的 Json 数据: {jsonResponse}");
                            return [];
                        }

                        // 添加所有用户创建的拉取请求的 ID 到 allPullRequests 数组中
                        foreach (JsonNode? pullRequest in jsonNode.AsArray())
                        {
                            string userLogin = pullRequest?["user"]?["login"]?.ToString() ?? "";
                            if (userLogin == username)
                            {
                                string? pullRequestId = pullRequest?["number"]?.ToString() ?? "";
                                Print.PrintDebug($"检查拉取请求 {pullRequestId} 是否有效且需要重试。");
                                if (!string.IsNullOrWhiteSpace(pullRequestId) && (await ValidatePullRequest(pullRequestId, "auto", token) != 0))
                                {
                                    // 将拉取请求 id 添加到数组
                                    Print.PrintDebug($"添加拉取请求 {pullRequestId} 到重试列表。");
                                    allPullRequests.Add(pullRequestId);
                                }
                            }
                        }

                        // 检查 Link 头以获取分页信息
                        string? linkHeader = response.Headers.Contains("Link") ? response.Headers.GetValues("Link").FirstOrDefault() : null;
                        nextUrl = GetNextPageUrlFromLinkHeader(linkHeader);
                    }
                    else
                    {
                        Print.PrintError($"获取拉取请求失败: {(int)response.StatusCode} {response.StatusCode}");
                        Print.PrintDebug($"返回的内容: {await response.Content.ReadAsStringAsync()}");
                        return [];
                    }
                }

                return allPullRequests;
            }
            catch (Exception e)
            {
                Print.PrintError($"获取创建的拉取请求时出错: {e}");
                return [];
            }
        }

        // 定义一个方法，用于重试需要重试的拉取请求
        // 接受拉取请求 ID 列表和 token 作为参数
        // 返回一个 整型 ，表示重试的结果
        // 0 表示成功，1 表示失败
        private static async Task<int> RetryPullRequests(List<string> pullRequests, string token)
        {
            // 捕获 Ctrl + C 信号
            Console.CancelKeyPress += cancelHandler;
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            foreach (string pullRequestId in pullRequests)
            {
                try
                {
                    // 如果操作被取消，跳过后续处理
                    if (canceled == true)
                    {
                        break;
                    }

                    // https://docs.github.com/zh/rest/pulls/pulls?apiVersion=2022-11-28#update-a-pull-request
                    string url = $"https://api.github.com/repos/microsoft/winget-pkgs/pulls/{pullRequestId}";
                    StringContent content;

                    // =============== 关闭拉取请求 ===============
                    content = new("{\"state\":\"closed\"}", System.Text.Encoding.UTF8, "application/json");
                    // {
                    //    "state": "closed"
                    // }
                    HttpResponseMessage response = await client.PatchAsync(url, content);
                    if (response.IsSuccessStatusCode)
                    {
                        Print.PrintInfo($"已关闭拉取请求 https://github.com/microsoft/winget-pkgs/pull/{pullRequestId}");
                    }
                    else
                    {
                        Print.PrintError($"关闭拉取请求 https://github.com/microsoft/winget-pkgs/pull/{pullRequestId} 失败: {(int)response.StatusCode} {response.StatusCode}");
                        return 1;
                    }

                    // =============== 重新打开拉取请求 ===============
                    content = new("{\"state\":\"open\"}", System.Text.Encoding.UTF8, "application/json");
                    // {
                    //    "state": "open"
                    // }
                    response = await client.PatchAsync(url, content);
                    if (response.IsSuccessStatusCode)
                    {
                        Print.PrintInfo($"已重新打开拉取请求 https://github.com/microsoft/winget-pkgs/pull/{pullRequestId}");
                    }
                    else
                    {
                        Print.PrintError($"重新打开拉取请求 https://github.com/microsoft/winget-pkgs/pull/{pullRequestId} 失败: {(int)response.StatusCode} {response.StatusCode}");
                        return 1;
                    }
                }
                catch (Exception e)
                {
                    Print.PrintError($"重试拉取请求 https://github.com/microsoft/winget-pkgs/pull/{pullRequestId} 时出错: {e}");
                    return 1;
                }
            }

            if (canceled)
            {
                return 2; // 操作取消
            }

            return 0;
        }

        // 定义一个方法，用于验证指定的拉取请求是否有效且真的需要重试
        // 接受 3 个参数：拉取请求，模式和 token
        // 返回一个布尔值，表示拉取请求是否有效且真的需要重试
        // 对于指定的拉取请求 ID，仅验证拉取请求 ID 是否有效
        private static async Task<int> ValidatePullRequest(string pullRequest, string mode, string token)
        {
            pullRequest = pullRequest
                        .Replace("#", "")
                        .Replace("https://github.com/microsoft/winget-pkgs/pull/", "")
                        .Trim();
            Print.PrintDebug($"处理后的拉取请求 ID: {pullRequest} ({int.TryParse(pullRequest, out _)})");
            if (int.TryParse(pullRequest, out int pullRequestId))
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
                // 获取拉取请求的状态
                string url = $"https://api.github.com/repos/microsoft/winget-pkgs/pulls/{pullRequestId}";
                HttpResponseMessage response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JsonNode? jsonNode = JsonNode.Parse(jsonResponse);
                    if (jsonNode == null)
                    {
                        Print.PrintError("无法解析 GitHub API 返回的 Json 数据。");
                        return 0;
                    }
                    // 获取拉取请求的状态
                    string? state = jsonNode["state"]?.ToString() ?? "";
                    if (state == "open")
                    {
                        Print.PrintDebug($"拉取请求 {pullRequestId} 是打开的。");
                        if (mode == "specify")
                        {
                            // 如果是手动指定的拉取请求，则直接返回拉取请求 ID
                            Print.PrintDebug($"拉取请求 {pullRequestId} 是手动指定的，直接返回。");
                            return pullRequestId;
                        }
                        else
                        {
                            // 如果是自动模式，则再检查是否存在需要重试的标签
                            // 定义一个数组，存放需要重试的标签
                            string[] retryLabels = [
                                "Internal-Error", "Internal-Error-Dynamic-Scan", "Internal-Error-Static-Scan",
                                // 未知内部错误、动态扫描错误、静态扫描错误
                                // 其他内部错误可能需要进一步调查，不应草率重试。
                            ];

                            // 定义一个数组，存放不能重试的标签
                            string[] noRetryLabels = [
                                "Needs-Author-Feedback", "Needs-Review", "Needs-Manual-Merge",
                                // 需要作者反馈、需要软件包维护者审查、没你啥事了它们合并了就行
                                "Validation-Retry",
                                // 此拉取请求已经经过太多的重试了
                                // 这些标签表示拉取请求需要进一步调查或需要作者反馈，不应重试。
                            ];

                            // 获取拉取请求的标签
                            JsonNode? labels = jsonNode["labels"];
                            if (labels != null)
                            {
                                bool needsRetry = false;
                                foreach (JsonNode? label in labels.AsArray())
                                {
                                    string? labelName = label?["name"]?.ToString() ?? "";
                                    if (retryLabels.Contains(labelName))
                                    {
                                        Print.PrintDebug($"拉取请求 {pullRequestId} 需要重试，因为它带有标签: {labelName}");
                                        needsRetry = true;
                                    }
                                    else if (noRetryLabels.Contains(labelName))
                                    {
                                        Print.PrintDebug($"拉取请求 {pullRequestId} 不应该重试，因为它带有标签: {labelName}");
                                        needsRetry = false;
                                        break;
                                    }
                                    else
                                    {
                                        Print.PrintDebug($"标签 {labelName} 不代表拉取请求 {pullRequestId} 需要重试");
                                    }
                                }

                                if (needsRetry)
                                {
                                    return pullRequestId;
                                }
                                else
                                {
                                    Print.PrintDebug($"拉取请求 {pullRequestId} 不需要重试，跳过。");
                                }
                            }
                            else
                            {
                                Print.PrintDebug($"拉取请求 {pullRequestId} 没有标签，跳过");
                            }
                        }
                    }
                    else
                    {
                        Print.PrintDebug($"拉取请求 {pullRequestId} 已关闭，跳过。");
                        return 0;
                    }
                }
            }
            return 0;
        }

        // 解析 Link 头，获取下一页的 URL
        private static string GetNextPageUrlFromLinkHeader(string? linkHeader)
        {
            if (string.IsNullOrEmpty(linkHeader)) return "";

            Regex regex = new(@"<([^>]+)>;\s*rel=""next""");
            Match match = regex.Match(linkHeader);

            if (match.Success)
            {
                return match.Groups[1].Value; // 返回匹配的 URL 部分
            }

            return "";
        }
    }
}
