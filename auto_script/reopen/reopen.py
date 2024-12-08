import os
import sys
import shutil
import requests
import subprocess
from colorama import init, Fore

init(autoreset=True)

REPO_OWNER = 'microsoft'
REPO_NAME = 'winget-pkgs'
script_path = os.path.dirname(os.path.dirname(sys.argv[0]))
print(script_path)
os.chdir(script_path)

while True:
    PR_NUMBER = "196910" #input("Please enter the Pull Request number: ")
    # 检测适用性
    try:
        PR_NUMBER = int(PR_NUMBER)
        if PR_NUMBER <= 0:
            print(f"{Fore.RED}✕{Fore.RESET} Too small! Please specify a value greater than 0!")
        else:
            print(f"{Fore.GREEN}✓{Fore.RESET} The Pull Request number has been set: {Fore.BLUE}{PR_NUMBER}{Fore.RESET}")
            break
    except ValueError as e:
        print(f"{Fore.RED}✕{Fore.RESET} The entered value is not valid, it must be a positive integer!")

# 可忽略的标签列表
IGNORE_LABELS = {'Retry-1', 'Validation-Retry'}

def check_repo_exists():
    try:
        response = requests.get(
            f'https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}'
        )
        return response.status_code == 404
    except Exception:
        return False

def check_pr_exists():
    try:
        response = requests.get(
            f'https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/issues/{PR_NUMBER}'
        )
        return response.status_code == 404
    except Exception:
        return False

def check_pr_labels():
    # --- 无法应对网络错误 ---
    if check_repo_exists():
        return f'Repository {Fore.BLUE}{REPO_OWNER}{Fore.RESET}/{Fore.BLUE}{REPO_NAME}{Fore.RESET} does not exist.'
    
    if check_pr_exists():
        return f'Pull Request {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} does not exist.'
    
    try:
        response = requests.get(
            f'https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/issues/{PR_NUMBER}'
        )
        
        if response.status_code == 200:
            pr_data = response.json()
            labels = pr_data.get('labels', [])
            
            if labels:
                label_names = {label['name'] for label in labels}
                relevant_labels = label_names - IGNORE_LABELS
                
                if relevant_labels:
                    if any('Internal-Error' in label for label in relevant_labels):
                        subprocess.run(['gh', 'pr', 'close', PR_NUMBER], capture_output=True, text=True)
                        subprocess.run(['gh', 'pr', 'comment', PR_NUMBER, "--body", "[Auto] Reopen this pull request to get the validation pipeline running again."], capture_output=True, text=True)
                        subprocess.run(['gh', 'pr', 'reopen', PR_NUMBER], capture_output=True, text=True)
                        return f'\n{Fore.BLUE}[INFO({Fore.YELLOW}rules{Fore.BLUE})]{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has the following relevant labels: {Fore.BLUE}{", ".join(relevant_labels)}{Fore.RESET}\n{Fore.BLUE}[INFO]{Fore.RESET} This pull request appears to have encountered an {Fore.YELLOW}internal error{Fore.RESET}.'
                    elif 'Moderator-Approved' in relevant_labels: # 优先级高于 Azure-Pipeline-Passed / Validation-Completed
                        return f'\n{Fore.BLUE}[INFO({Fore.YELLOW}rules{Fore.BLUE})]{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has the following relevant labels: {Fore.BLUE}{", ".join(relevant_labels)}{Fore.RESET}\n{Fore.BLUE}[INFO]{Fore.RESET} 🎉 This pull request appears to have been {Fore.GREEN}approved{Fore.RESET} by the moderator.'
                    elif 'Validation-Completed' in relevant_labels: # 优先级高于 Azure-Pipeline-Passed
                        os.remove(os.path.join(script_path, ".github\\workflows\\auto-reopen.yml"))
                        shutil.rmtree(os.path.join(script_path, "auto_script\\reopen"))
                        subprocess.run(['git', 'add', '.'], capture_output=True, text=True)
                        subprocess.run(['git', 'commit', '-m', "[Auto] 移除PR Reopen工作流"], capture_output=True, text=True)
                        subprocess.run(['git', 'push'], capture_output=True, text=True)
                        return f'\n{Fore.BLUE}[INFO({Fore.YELLOW}rules{Fore.BLUE})]{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has the following relevant labels: {Fore.BLUE}{", ".join(relevant_labels)}{Fore.RESET}\n{Fore.BLUE}[INFO]{Fore.RESET} 🎉 Validation of this pull request appears to have been {Fore.GREEN}completed{Fore.RESET}.'
                    return f'\n{Fore.BLUE}[INFO]{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has the following relevant labels: {Fore.BLUE}{", ".join(relevant_labels)}{Fore.RESET}\n{Fore.YELLOW}⚠{Fore.RESET} No rules are matched.'
                else:
                    return f'\n{Fore.YELLOW}⚠{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has labels, but all are {Fore.YELLOW}ignored{Fore.RESET}.'
            else:
                return f'\n{Fore.BLUE}[INFO]{Fore.RESET} PR {Fore.BLUE}#{PR_NUMBER}{Fore.RESET} has {Fore.BLUE}no labels{Fore.RESET}.'
    except Exception as e:
        return f'\n{Fore.RED}✕{Fore.RESET} Failed to retrieve PR data:\n{Fore.RED}✕ {e}{Fore.RESET}'

def wait_for_labels():
    global time_counter
    result = check_pr_labels()
    print(result)

if __name__ == "__main__":
    wait_for_labels()
