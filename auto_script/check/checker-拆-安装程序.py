import os
import re
import sys
import yaml
import requests

fail = 0

def find_urls(data):
    # 在嵌套字典或列表中递归查找 URL
    urls = set()
    if isinstance(data, dict):
        for key, value in data.items():
            if isinstance(value, str):
                # Use regex to find potential URLs
                found_urls = re.findall(r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\\(\\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+', value)
                # Filter URLs
                excluded_domains = { # 豁免
                    'sourceforge', # 总是 403
                    '123', '360', 'effie', 'typora', 'tchspt', 'mysql', 'voicecloud', 'iflyrec', 'jisupdf', # 之前豁免的
                    'floorp' # 较难处理
                }
                filtered_urls = {url for url in found_urls if not any(domain in url for domain in excluded_domains)}
                urls.update(filtered_urls)
            elif isinstance(value, (dict, list)):
                urls.update(find_urls(value))
    elif isinstance(data, list):
        for item in data:
            urls.update(find_urls(item))
    return urls

def check_urls_in_yaml_files(folder_path):
    # 递归检查指定文件夹及其子文件夹中 YAML 文件中的 URL
    global fail
    for root, _, files in os.walk(folder_path):
        for filename in files:
            if filename.endswith(".yaml"):
                file_path = os.path.join(root, filename)
                try:
                    with open(file_path, 'r', encoding='utf-8') as file:
                        yaml_data = yaml.safe_load(file)
                        # 在 YAML 清单数据中查找任何可能的 URL
                        urls = find_urls(yaml_data)
                        for url in urls:
                            try:
                                headers = {
                                    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3'
                                }
                                response = requests.head(url, allow_redirects=True, verify=True, headers=headers)
                                if response.status_code == 405:
                                    # 使用 GET 请求重试
                                    response = requests.get(url, allow_redirects=True, verify=True, headers=headers)
                                if response.status_code >= 400:
                                    if response.status_code == 404 and url.endswith((".exe", ".zip", ".msi", ".msix", ".appx")):
                                        print(f"\n[Error] (安装程序返回 404) {file_path} 中的 {url} 返回了状态码 {response.status_code} (Not found - 未找到)")
                                        fail = 1
                                        sys.exit(1)
                                    if response.status_code == 403 and url.endswith((".exe", ".zip", ".msi", ".msix", ".appx")):
                                        print(f"\n[Error] (安装程序返回 403) {file_path} 中的 {url} 返回了状态码 {response.status_code} (Forbidden - 已禁止)")
                                        fail = 1
                                        sys.exit(1)
                                    else:
                                        print(f"\n[Warning] {file_path} 中的 {url} 返回了状态码 {response.status_code} (≥400)\n")
                                else:
                                    print("*", end="")
                            except requests.exceptions.RequestException as e:
                                print(f"\n[Warning] 无法访问 {file_path} 中的 {url} : {e}")
                except IOError as e:
                    print(f"\n[Error] 打不开 {file_path} : {e}")
                except yaml.YAMLError as e:
                    print(f"\n[Error] 处理文件 {file_path} 时发生 YAML 错误: {e}")
                except Exception as e:
                    print(f"\n[Error] 处理文件 {file_path} 时发生意外错误: {e}")

folder_path = "winget-pkgs"
os.path.abspath(folder_path)
if (not sys.argv[1]):
    print("[Error] 没有指定需要检查的目录")
folder_path = os.path.join(folder_path, "manifests", sys.argv[1])

if not os.path.exists(folder_path):
    print(f"[Error] 指定的检查目录不存在")
    sys.exit(1)
check_urls_in_yaml_files(folder_path)

if fail == 0:
    print(f"所有安装程序链接正常")
else:
    sys.exit(fail)
