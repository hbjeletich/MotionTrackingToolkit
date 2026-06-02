"""
MediaPipe Pose Landmark sender for Unity.

Captures webcam frames, runs MediaPipe Pose detection, converts landmarks
to Unity's coordinate system (right-handed, Y-up), and sends them over UDP.

Unity receives a flat binary packet each frame:
  [timestamp: float32]
  [landmark_count: int32]
  [x0, y0, z0, confidence0, x1, y1, z1, confidence1, ...] : float32 each

Install dependencies:
  pip install mediapipe opencv-python

Usage:
  python mediapipe_sender.py --list-cameras          # show available cameras
  python mediapipe_sender.py --camera 1              # use second camera
  python mediapipe_sender.py --camera "Logitech"     # match camera by name
  python mediapipe_sender.py --port 7001             # different port
  python mediapipe_sender.py --show                  # show webcam preview
  python mediapipe_sender.py --model heavy           # use heavy model
"""

import argparse
import os
import socket
import struct
import sys
import time
import urllib.request

import cv2
import mediapipe as mp


# ── Model management ──

MODEL_URLS = {
    "lite": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task",
    "full": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task",
    "heavy": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task",
}


def get_model_path(model_name: str) -> str:
    """Get the model .task file path. Checks PyInstaller bundle, script dir, then downloads."""
    filename = f"pose_landmarker_{model_name}.task"

    # when running as a PyInstaller bundle, check the bundle directory first
    search_dirs = []
    if getattr(sys, '_MEIPASS', None):
        search_dirs.append(sys._MEIPASS)  # --onefile temp dir
    search_dirs.append(os.path.dirname(os.path.abspath(sys.executable if getattr(sys, 'frozen', False) else __file__)))
    search_dirs.append(os.path.dirname(os.path.abspath(__file__)))

    for d in search_dirs:
        model_path = os.path.join(d, filename)
        if os.path.exists(model_path):
            print(f"Using model: {model_path}")
            return model_path

    # not found — download
    url = MODEL_URLS.get(model_name)
    if url is None:
        print(f"ERROR: Unknown model '{model_name}'. Options: lite, full, heavy")
        sys.exit(1)

    # download to the script/exe directory
    download_dir = search_dirs[0]
    model_path = os.path.join(download_dir, filename)

    print(f"Model '{filename}' not found. Downloading from Google...")
    print(f"  URL: {url}")
    try:
        urllib.request.urlretrieve(url, model_path)
        print(f"  Saved to: {model_path}")
    except Exception as e:
        print(f"  ERROR: Download failed — {e}")
        sys.exit(1)

    return model_path


# ── Camera enumeration ──

def list_cameras(max_index: int = 10):
    """List available camera devices by trying to open each index."""
    print("Scanning for cameras...")
    available = []
    for i in range(max_index):
        cap = cv2.VideoCapture(i)
        if cap.isOpened():
            # try to get the backend name
            backend = cap.getBackendName()
            w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            available.append((i, backend, w, h))
            cap.release()

    if not available:
        print("  No cameras found!")
    else:
        print(f"  Found {len(available)} camera(s):")
        for idx, backend, w, h in available:
            print(f"    [{idx}] {backend} — {w}x{h}")

    return available


def select_camera(camera_arg: str, max_index: int = 10) -> int:
    """
    Select a camera by index or name substring match.
    If camera_arg is a digit string, use it as the index.
    Otherwise, scan cameras and match by backend/name.
    """
    # try as integer index first
    try:
        return int(camera_arg)
    except ValueError:
        pass

    # try matching by name
    search = camera_arg.lower()
    print(f"Searching for camera matching '{camera_arg}'...")

    for i in range(max_index):
        cap = cv2.VideoCapture(i)
        if cap.isOpened():
            backend = cap.getBackendName()
            cap.release()
            if search in backend.lower():
                print(f"  Matched camera [{i}]: {backend}")
                return i

    print(f"  No camera matching '{camera_arg}' found. Use --list-cameras to see available devices.")
    sys.exit(1)


# ── Landmark conversion ──

def landmarks_to_unity_packet(world_landmarks, timestamp: float) -> bytes:
    """
    Convert MediaPipe world landmarks to a binary packet in Unity's coordinate system.

    MediaPipe world landmarks:
      x: right (positive = person's left, camera's right)
      y: down (positive = downward)
      z: toward camera (positive = toward camera)
      Origin: roughly at hip center
      Units: meters

    Unity:
      x: right, y: up, z: forward (away from camera)

    Conversion: x stays, y flips, z flips.
    """
    landmarks = world_landmarks[0]  # first detected pose
    landmark_count = len(landmarks)

    # header: timestamp + count
    packet = struct.pack('fi', timestamp, landmark_count)

    # per-landmark: x, y, z, confidence
    for lm in landmarks:
        packet += struct.pack('ffff',
                              lm.x,         # x: right (same)
                              -lm.y,        # y: flip (down → up)
                              -lm.z,        # z: flip (toward camera → away)
                              lm.visibility  # confidence/visibility score
                              )

    return packet


# ── Main ──

def main():
    parser = argparse.ArgumentParser(description='MediaPipe Pose → Unity UDP sender')
    parser.add_argument('--list-cameras', action='store_true', help='List available cameras and exit')
    parser.add_argument('--camera', type=str, default='0', help='Camera index (int) or name to match')
    parser.add_argument('--width', type=int, default=1280, help='Capture width')
    parser.add_argument('--height', type=int, default=720, help='Capture height')
    parser.add_argument('--host', type=str, default='127.0.0.1', help='Unity UDP host')
    parser.add_argument('--port', type=int, default=7000, help='Unity UDP port')
    parser.add_argument('--model', type=str, default='full', choices=['lite', 'full', 'heavy'],
                        help='Model complexity: lite, full, heavy')
    parser.add_argument('--show', action='store_true', help='Show webcam preview window')
    parser.add_argument('--min-detection', type=float, default=0.5, help='Min detection confidence (0-1)')
    parser.add_argument('--min-tracking', type=float, default=0.5, help='Min tracking confidence (0-1)')
    args = parser.parse_args()

    # list cameras mode
    if args.list_cameras:
        list_cameras()
        return

    # get model
    model_path = get_model_path(args.model)

    # select camera
    camera_index = select_camera(args.camera)

    # set up UDP socket
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.host, args.port)
    print(f"Sending landmarks to {dest[0]}:{dest[1]}")

    # set up webcam
    cap = cv2.VideoCapture(camera_index)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    if not cap.isOpened():
        print(f"ERROR: Could not open camera {camera_index}")
        return

    actual_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    actual_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    print(f"Camera {camera_index}: {actual_w}x{actual_h}")

    # set up MediaPipe PoseLandmarker (Tasks API)
    BaseOptions = mp.tasks.BaseOptions
    PoseLandmarker = mp.tasks.vision.PoseLandmarker
    PoseLandmarkerOptions = mp.tasks.vision.PoseLandmarkerOptions
    VisionRunningMode = mp.tasks.vision.RunningMode

    options = PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=model_path),
        running_mode=VisionRunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=args.min_detection,
        min_tracking_confidence=args.min_tracking,
        output_segmentation_masks=False,
    )

    landmarker = PoseLandmarker.create_from_options(options)

    # pose connections for drawing (same as the old mp.solutions.pose.POSE_CONNECTIONS)
    POSE_CONNECTIONS = [
        (0,1),(1,2),(2,3),(3,7),(0,4),(4,5),(5,6),(6,8),
        (9,10),(11,12),(11,13),(13,15),(15,17),(15,19),(15,21),
        (12,14),(14,16),(16,18),(16,20),(16,22),
        (11,23),(12,24),(23,24),(23,25),(25,27),(27,29),(27,31),
        (24,26),(26,28),(28,30),(28,32),
    ]

    start_time = time.time()
    frame_count = 0
    fps_report_interval = 60

    print("Running... Press 'q' in preview window or Ctrl+C to stop.")

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                print("WARNING: Failed to read frame, retrying...")
                continue

            # convert to MediaPipe Image
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)

            # detect
            timestamp_ms = int((time.time() - start_time) * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.pose_world_landmarks and len(result.pose_world_landmarks) > 0:
                timestamp = time.time() - start_time
                packet = landmarks_to_unity_packet(result.pose_world_landmarks, timestamp)
                sock.sendto(packet, dest)

            # optional preview
            if args.show:
                if result.pose_landmarks and len(result.pose_landmarks) > 0:
                    landmarks = result.pose_landmarks[0]
                    h, w = frame.shape[:2]

                    # draw connections
                    for a, b in POSE_CONNECTIONS:
                        if a < len(landmarks) and b < len(landmarks):
                            pt1 = (int(landmarks[a].x * w), int(landmarks[a].y * h))
                            pt2 = (int(landmarks[b].x * w), int(landmarks[b].y * h))
                            cv2.line(frame, pt1, pt2, (0, 255, 0), 2)

                    # draw landmarks
                    for lm in landmarks:
                        cx, cy = int(lm.x * w), int(lm.y * h)
                        cv2.circle(frame, (cx, cy), 4, (0, 0, 255), -1)

                cv2.imshow('MediaPipe Pose', frame)
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    break

            # fps reporting
            frame_count += 1
            if frame_count % fps_report_interval == 0:
                elapsed = time.time() - start_time
                fps = frame_count / elapsed
                detected = "YES" if result.pose_world_landmarks else "NO"
                print(f"  FPS: {fps:.1f} | Pose detected: {detected}")

    except KeyboardInterrupt:
        print("\nStopping...")

    finally:
        landmarker.close()
        cap.release()
        sock.close()
        if args.show:
            cv2.destroyAllWindows()
        print("Cleaned up.")


if __name__ == '__main__':
    main()
