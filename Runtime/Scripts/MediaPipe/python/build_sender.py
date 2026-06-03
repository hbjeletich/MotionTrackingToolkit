"""
Build script for compiling mediapipe_sender.py and mediapipe_calibrate.py
into standalone executables.

This creates distributable exes that include Python, MediaPipe, OpenCV,
and the pose model — no Python installation needed on the target machine.

Usage:
  1. Install PyInstaller:     pip install pyinstaller
  2. Run this script:         python build_sender.py
  3. Output lands in:         dist/MediaPipeSender/
                              dist/MediaPipeCalibrate/

  Copy both dist/ folders into your Unity project at:
    Assets/StreamingAssets/MediaPipe/

The Unity MediaPipeProcessManager component launches MediaPipeSender from there.
MediaPipeCalibrate is run manually by users setting up multi-camera.

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
CALIBRATE_SCRIPT = os.path.join(SCRIPT_DIR, "mediapipe_calibrate.py")
MODEL_NAME = "pose_landmarker_full"
MODEL_FILE = f"{MODEL_NAME}.task"
MODEL_URL = f"https://storage.googleapis.com/mediapipe-models/pose_landmarker/{MODEL_NAME}/float16/latest/{MODEL_FILE}"

# Python modules shared by both exes
SHARED_MODULES = [
    "camera_source.py",
    "pose_detector.py",
    "camera_calibration.py",
    "multi_camera_fusion.py",
]


def ensure_model():
    """Download the model file if it doesn't exist."""
    model_path = os.path.join(SCRIPT_DIR, MODEL_FILE)
    if os.path.exists(model_path):
        print(f"Model found: {model_path}")
        return model_path

    print(f"Downloading model: {MODEL_FILE}...")
    urllib.request.urlretrieve(MODEL_URL, model_path)
    print(f"Saved to: {model_path}")
    return model_path


def build_exe(script_path: str, name: str, model_path: str, extra_args: list = None):
    """Run PyInstaller for one script."""
    if not os.path.exists(script_path):
        print(f"ERROR: {script_path} not found!")
        sys.exit(1)

    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm",
        "--name", name,
        "--onedir",

        # bundle the model file — accessible at runtime via sys._MEIPASS
        "--add-data", f"{model_path}{os.pathsep}.",

        # collect ALL of mediapipe including native C bindings
        "--collect-all", "mediapipe",

        # hidden imports
        "--hidden-import", "cv2",

        # bundle shared modules as source (PyInstaller traces imports from them)
        *[arg for mod in SHARED_MODULES
          for arg in ("--add-data", f"{os.path.join(SCRIPT_DIR, mod)}{os.pathsep}.")],

        *(extra_args or []),
        script_path,
    ]

    print(f"\nBuilding {name}...")
    print(f"  Command: {' '.join(cmd)}\n")

    result = subprocess.run(cmd, cwd=SCRIPT_DIR)
    return result.returncode == 0


def build():
    model_path = ensure_model()

    sender_ok = build_exe(SENDER_SCRIPT, "MediaPipeSender", model_path)
    calibrate_ok = build_exe(CALIBRATE_SCRIPT, "MediaPipeCalibrate", model_path)

    print()
    print("=" * 60)

    if sender_ok and calibrate_ok:
        sender_dist = os.path.join(SCRIPT_DIR, "dist", "MediaPipeSender")
        calibrate_dist = os.path.join(SCRIPT_DIR, "dist", "MediaPipeCalibrate")
        print("BUILD SUCCESSFUL!")
        print()
        print("Outputs:")
        print(f"  {sender_dist}")
        print(f"  {calibrate_dist}")
        print()
        print("Next steps:")
        print("  1. Copy both dist/ folders into your Unity project:")
        print("     Assets/StreamingAssets/MediaPipe/MediaPipeSender/")
        print("     Assets/StreamingAssets/MediaPipe/MediaPipeCalibrate/")
        print()
        print("  2. Single-camera (unchanged):")
        print("     MediaPipeSender/MediaPipeSender.exe")
        print()
        print("  3. Multi-camera setup:")
        print("     a) Run MediaPipeCalibrate/MediaPipeCalibrate.exe --cameras 0,1 --output cal.json")
        print("     b) Run MediaPipeSender/MediaPipeSender.exe --cameras 0,1 --calibration cal.json")
    else:
        failed = []
        if not sender_ok:
            failed.append("MediaPipeSender")
        if not calibrate_ok:
            failed.append("MediaPipeCalibrate")
        print(f"BUILD FAILED: {', '.join(failed)}")
        print("Check the output above for errors.")
        sys.exit(1)

    print("=" * 60)


if __name__ == "__main__":
    build()
