import os
import yaml
import re
import requests
from colorama import init, Fore

init(autoreset=True)

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
                                    if response.status_code != 404:
                                        print(f"\n{Fore.YELLOW}[Warning]{Fore.RESET} URL {Fore.BLUE}{url}{Fore.RESET} in file {Fore.BLUE}{filename}{Fore.RESET} returned status code {Fore.YELLOW}{response.status_code}{Fore.RESET} (≥400)")
                                    else:
                                        print(f"\n{Fore.RED}[Fail (return 404)]{Fore.RESET} URL {Fore.BLUE}{url}{Fore.RESET} in file {Fore.BLUE}{filename}{Fore.RESET} returned status code {Fore.YELLOW}{response.status_code}{Fore.RESET} (Not found)")
                                    input("Please check the URL manually and press Enter to continue...\n")
                                    # Handle logic for status code 400 and above here
                                else:
                                    print("*", end="")
                            except requests.exceptions.RequestException as e:
                                print(f"\n{Fore.YELLOW}[Warning]{Fore.RESET} Unable to access URL {Fore.BLUE}{url}{Fore.RESET} in file {Fore.BLUE}{filename}{Fore.RESET}: {Fore.RED}{e}{Fore.RESET}")
                                input("Please check the URL manually and press Enter to continue...\n")
                except IOError as e:
                    print(f"\n{Fore.RED}[Fail]{Fore.RESET} Can not open file {Fore.BLUE}{filename}{Fore.RESET} : {Fore.RED}{e}{Fore.RESET}")
                    input("Please check the file permissions and coding, press Enter to continue...\n")
                except yaml.YAMLError as e:
                    print(f"\n{Fore.RED}[Fail]{Fore.RESET} Error parsing YAML for file {Fore.BLUE}{filename}{Fore.RESET} : {Fore.RED}{e}{Fore.RESET}")
                    input("Please check that the file follows YAML syntax, press Enter to continue...\n")
                except Exception as e:
                    print(f"\n{Fore.RED}[Fail]{Fore.RESET} An unknown error occurred while processing file {Fore.BLUE}{filename}{Fore.RESET} : {Fore.RED}{e}{Fore.RESET}")
                    input("Press Enter to continue...\n")

folder_path = input("Please enter the directory you want to check: ")
if folder_path.startswith(("'", '"')) and folder_path.endswith(("'", '"')):
    folder_path = folder_path[1:-1]

if not folder_path.endswith('\\'):
    folder_path += '\\'

if not os.path.exists(folder_path):
    print(f"{Fore.RED}[Fail]{Fore.RESET} The specified path does not exist.")
    input("Press Enter to continue...\n")
    exit()
check_urls_in_yaml_files(folder_path)
