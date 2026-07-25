import os
import subprocess
import sys

def build():
    print("Building Switch Handover Generator standalone application...")
    
    cmd = [
        sys.executable,
        "-m", "PyInstaller",
        "--noconfirm",
        "--onefile",
        "--windowed",
        "--add-data", "templates;templates",
        "--name", "SwitchHandoverGenerator",
        "main.py"
    ]
    
    subprocess.run(cmd, check=True)
    print("Build complete! The executable is located in the 'dist' folder.")

if __name__ == "__main__":
    build()
