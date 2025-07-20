# Winget Tools

> [!CAUTION]
> These tools are **made and used by me personally** and are NOT [official tools](https://github.com/microsoft/winget-pkgs/tree/master/Tools).  

## Tools

| Name | Location | Classification | To do | Language |
|-----|-----|-----|-----|-----|
| Label status notification | notification/Label_status_notification.py | pr, label, notification, status | Stay updated on the status of your pull request labels. | EN/Python |
| 无效版本检查 | .github/workflows/check-url-cai.yaml | check, url, invalid, 404 | 检查指定 winget-pkgs 仓库中的失效链接 | ZH/C# & GitHub Action |
| 获取和分析安装程序 | .github/workflows/df.yaml | download, analyze, installer | 在线分析安装程序以加快处理速度 | ZH/GitHub Action & Use Komac to analyse |
| winget-pkgs fork 同步和清理已合并分支 | .github/workflows/sync-clean.yaml | sync, clean, merged, branch | 都 5202 年了，谁还自己 rebase | ZH/GitHub Action & Use Komac |
| 验证域 | .github/workflows/vd.yaml | validate, domain | 访问不了的，放 GitHub Action 上看看行不行 | ZH/GitHub Action & Bash (Running on Ubuntu) |

Want use these GitHub Action Workflows? Fork this repo and run the workflow.  
Do not forget to set up the repository secret!  

### External tools
Do you want to integrate with winget-tools? Take a look at [Sundry](https://github.com/DuckDuckStudio/Sundry/)! - And you can install it using `winget install --id DuckStudio.Sundry --source winget --exact`.  

## About Open source
Tools author: [鸭鸭「カモ」](https://duckduckstudio.github.io/yazicbs.github.io/zh_cn/index.html)  
Open source license: [MIT](https://github.com/DuckDuckStudio/winget-tools/blob/main/LICENSE)  
