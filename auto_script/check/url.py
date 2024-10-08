import os
import re
import sys
import yaml
import requests

fail = 0

def find_urls(data):
    """
    Recursively find URLs in nested dictionaries or lists.
    """
    urls = set()
    if isinstance(data, dict):
        for key, value in data.items():
            if isinstance(value, str):
                # Use regex to find potential URLs
                found_urls = re.findall(r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\\(\\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+', value)
                urls.update(found_urls)
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
    for root, _, files in os.walk(folder_path):
        for filename in files:
            if filename.endswith(".yaml"):
                file_path = os.path.join(root, filename)
                try:
                    with open(file_path, 'r', encoding='utf-8') as file:
                        yaml_data = yaml.safe_load(file)
                        # Find all possible URLs in the YAML data
                        urls = find_urls(yaml_data)
                        for url in urls:
                            try:
                                response = requests.head(url, allow_redirects=True, verify=True)
                                if response.status_code == 405:
                                    # Retry with GET request
                                    response = requests.get(url, allow_redirects=True, verify=True)
                                if response.status_code >= 400:
                                    if response.status_code == 404 and url.endswith((".exe", ".zip", ".msi", ".msix", ".appx")):
                                        print(f"\n[Fail (installer return 404)] URL {url} in file {file_path} returned status code {response.status_code} (Not found)")
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
if folder_path.startswith(("'", '"')) and folder_path.endswith(("'", '"')):
    folder_path = folder_path[1:-1]

if not folder_path.endswith('\\'):
    folder_path += '\\'

if not os.path.exists(folder_path):
    print(f"[Fail] The specified path does not exist.")
    #input("Press Enter to continue...\n")
    exit()
check_urls_in_yaml_files(folder_path)

if fail == 0:
    print(f"All urls normal.")
else:
    sys.exit(fail)
