import os
import re
import sys
import yaml
import requests

fail = 0

def find_urls(data):
    """
    Recursively find URLs in nested dictionaries or lists, 
    but exclude some URLs.
    """
    urls = set()
    if isinstance(data, dict):
        for key, value in data.items():
            if isinstance(value, str):
                # Use regex to find potential URLs
                found_urls = re.findall(r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\\(\\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+', value)
                # Filter URLs
                excluded_domains = {
                    'sourceforge'# 豁免
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
    """
    Recursively check URLs found in YAML files in the specified folder and its subfolders.
    """
    global fail
    for root, _, files in os.walk(folder_path):
        for filename in files:
            if filename.endswith(".yaml"):
                file_path = os.path.join(root, filename)
                try:
                    exclude_pattern = re.compile(r'manifest/[0-9a-z]') # 忽略检查的路径 (0-9, a-b)
                    if not exclude_pattern.search(file_path):
                        with open(file_path, 'r', encoding='utf-8') as file:
                            yaml_data = yaml.safe_load(file)
                            # Find all possible URLs in the YAML data
                            urls = find_urls(yaml_data)
                            for url in urls:
                                try:
                                    headers = {
                                        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3'
                                    }
                                    response = requests.head(url, allow_redirects=True, verify=True, headers=headers)
                                    if response.status_code == 405:
                                        # Retry with GET request
                                        response = requests.get(url, allow_redirects=True, verify=True, headers=headers)
                                    if response.status_code >= 400:
                                        if response.status_code == 404:
                                            if url.endswith((".exe", ".zip", ".msi", ".msix", ".appx")):
                                                print(f"\n[Fail (installer return 404)] URL {url} in file {file_path} returned status code {response.status_code} (Not found)")
                                            else:
                                                print(f"\n[Fail] URL {url} in file {file_path} returned status code {response.status_code} (Not found)")
                                            fail = 1
                                            sys.exit(1)
                                            #input("Please check the URL manually and press Enter to continue...\n")
                                        if response.status_code == 403 and url.endswith((".exe", ".zip", ".msi", ".msix", ".appx")):
                                            print(f"\n[Fail (installer return 403)] URL {url} in file {file_path} returned status code {response.status_code} (Forbidden)")
                                            fail = 1
                                            sys.exit(1)
                                            #input("Please check the URL manually and press Enter to continue...\n")
                                        else:
                                            print(f"\n[Warning] URL {url} in file {file_path} returned status code {response.status_code} (≥400)\n")
                                        # Handle logic for status code 400 and above here
                                    else:
                                        print("*", end="")
                                except requests.exceptions.RequestException as e:
                                    print(f"\n[Warning] Unable to access URL {url} in file {file_path}: {e}")
                                    #input("Please check the URL manually and press Enter to continue...\n")
                except IOError as e:
                    print(f"\n[Fail] Can not open file {file_path} : {e}")
                    #input("Please check the file permissions and coding, press Enter to continue...\n")
                except yaml.YAMLError as e:
                    print(f"\n[Fail] Error parsing YAML for file {file_path} : {e}")
                    #input("Please check that the file follows YAML syntax, press Enter to continue...\n")
                except Exception as e:
                    print(f"\n[Fail] An unknown error occurred while processing file {file_path} : {e}")
                    #input("Press Enter to continue...\n")

folder_path = "winget-pkgs"
os.path.abspath(folder_path)
folder_path = os.path.join(folder_path, "manifests", "e") # 改这里

if not os.path.exists(folder_path):
    print(f"[Fail] The specified path does not exist.")
    #input("Press Enter to continue...\n")
    sys.exit(1)
check_urls_in_yaml_files(folder_path)

if fail == 0:
    print(f"All urls normal.")
else:
    sys.exit(fail)
