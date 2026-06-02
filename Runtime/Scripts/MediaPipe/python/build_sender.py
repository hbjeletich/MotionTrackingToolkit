"""
Build script for compiling mediapipe_sender.py into a standalone executable.

This creates a distributable exe that includes Python, MediaPipe, OpenCV,
and the pose model — no Python installation needed on the target machine.

Usage:
  1. Install PyInstaller:     pip install pyinstaller
  2. Run this script:         python build_sender.py
  3. Output lands in:         dist/MediaPipeSender/

  Copy the dist/MediaPipeSender/ folder into your Unity project at:
    Assets/StreamingAssets/MediaPipe/

The Unity MediaPipeProcessManager component will launch it from there.

Notes:
  - Uses --onedir (folder mode) instead of --onefile for faster startup.
    A single exe has to unpack to a temp folder every launch, which adds
    several seconds. Folder mode starts instantly.
  - The model .task file is auto-downloaded if not present, then bundled.
  - First build takes a few minutes. Subsequent builds are faster.
"""

import os
import subprocess
import sys
import urllib.request


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SENDER_SCRIPT = os.path.join(SCRIPT_DIR, "mediapipe_sender.py")
MODEL_NAME = "pose_landmarker_full"
MODEL_FILE = f"{MODEL_NAME}.task"
MODEL_URL = f"https://storage.googleapis.com/mediapipe-models/pose_landmarker/{MODEL_NAME}/float16/latest/{MODEL_FILE}"


def ensure_model():
    """Download the model if it doesn't exist."""
    model_path = os.path.join(SCRIPT_DIR, MODEL_FILE)
    if os.path.exists(model_path):
        print(f"Model found: {model_path}")
        return model_path

    print(f"Downloading model: {MODEL_FILE}...")
    urllib.request.urlretrieve(MODEL_URL, model_path)
    print(f"Saved to: {model_path}")
    return model_path


def build():
    if not os.path.exists(SENDER_SCRIPT):
        print(f"ERROR: {SENDER_SCRIPT} not found!")
        sys.exit(1)

    model_path = ensure_model()

    # PyInstaller command
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm",
        "--name", "MediaPipeSender",

        # folder mode (faster startup than --onefile)
        "--onedir",

        # don't show a console window when launched from Unity
        # comment this out during debugging if you want to see print output
        #"--noconsole",

        # bundle the model file — accessible at runtime via sys._MEIPASS
        "--add-data", f"{model_path}{os.pathsep}.",

        # collect ALL of mediapipe including native C bindings
        # this is the nuclear option but it's the only reliable way —
        # mediapipe loads .pyd/.so files dynamically via importlib.resources
        # and PyInstaller can't trace those automatically
        "--collect-all", "mediapipe",

        # hidden imports that PyInstaller sometimes misses
        "--hidden-import", "cv2",

        # the script to compile
        SENDER_SCRIPT,
    ]

    print("Running PyInstaller...")
    print(f"  Command: {' '.join(cmd)}")
    print()

    result = subprocess.run(cmd, cwd=SCRIPT_DIR)

    if result.returncode == 0:
        dist_path = os.path.join(SCRIPT_DIR, "dist", "MediaPipeSender")
        print()
        print("=" * 60)
        print("BUILD SUCCESSFUL!")
        print(f"  Output: {dist_path}")
        print()
        print("Next steps:")
        print("  1. Copy the 'dist/MediaPipeSender/' folder to your Unity project:")
        print("     Assets/StreamingAssets/MediaPipe/")
        print()
        print("  2. The folder structure should look like:")
        print("     Assets/StreamingAssets/MediaPipe/MediaPipeSender/")
        print("       MediaPipeSender.exe")
        print("       pose_landmarker_full.task")
        print("       ... (other bundled files)")
        print("=" * 60)
    else:
        print()
        print("BUILD FAILED! Check the output above for errors.")
        sys.exit(1)


if __name__ == "__main__":
    build()