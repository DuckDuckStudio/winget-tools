using System;
using System.Net.Http;

namespace checker.PackageChecks.a.abbodi1406.vcredist
{
    internal class Checker
    {
        public static int Check()
        {
            // https://github.com/microsoft/winget-pkgs/issues/314513#issuecomment-3568299336
            // 检查这个包在这个 Issue 中移除的这两个版本是否又恢复可用。
            // [version 0.101.0]
            // https://github.com/abbodi1406/vcredist/releases/download/v0.101.0/VisualCppRedist_AIO_x86_x64.exe
            // https://github.com/abbodi1406/vcredist/releases/download/v0.101.0/VisualCppRedist_AIO_x86only.exe
            // [version 0.102.0]
            // https://github.com/abbodi1406/vcredist/releases/download/v0.102.0/VisualCppRedist_AIO_x86only.exe
            // https://github.com/abbodi1406/vcredist/releases/download/v0.102.0/VisualCppRedist_AIO_x86_x64.exe

            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            client.Timeout = TimeSpan.FromSeconds(15);

            int anyVersionOK = 0;

            foreach (string version in new[] { "0.101.0", "0.102.0" })
            {
                bool OK = true;
                foreach (string arch in new[] { "x86_x64", "x86only" })
                {
                    try
                    {
                        string url = $"https://github.com/abbodi1406/vcredist/releases/download/v{version}/VisualCppRedist_AIO_{arch}.exe";
                        using HttpResponseMessage response = client.Send(new HttpRequestMessage(HttpMethod.Head, url));
                        response.EnsureSuccessStatusCode();
                    }
                    catch (Exception e)
                    {
                        OK = false;
                        Console.WriteLine($"看起来 abbodi1406.vcredist 版本 {version} 依旧无效 ({e.Message})");
                        break;
                    }
                }
                if (OK)
                {
                    anyVersionOK++;
                    Console.WriteLine($"[Hint] 看起来 abbodi1406.vcredist 版本 {version} 又回来了，也许您应该将它添加回去？");
                }
            }
            return anyVersionOK;
        }
    }
}
