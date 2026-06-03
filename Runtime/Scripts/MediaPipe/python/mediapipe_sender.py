"""
MediaPipe Pose Landmark sender for Unity.

Captures webcam frames, runs MediaPipe Pose detection, converts landmarks
to Unity's coordinate system (right-handed, Y-up), and sends them over UDP.

Unity receives a flat binary packet each frame:
  [timestamp: float32]
  [landmark_count: int32]
  [x0, y0, z0, confidence0, x1, y1, z1, confidence1, ...] : float32 each

Install dependencies:
  pip install mediapipe opencv-python numpy

Single-camera usage:
  python mediapipe_sender.py --list-cameras          # show available cameras
  python mediapipe_sender.py --camera 1              # use second camera
  python mediapipe_sender.py --camera "Logitech"     # match camera by name
  python mediapipe_sender.py --port 7001             # different port
  python mediapipe_sender.py --show                  # show webcam preview
  python mediapipe_sender.py --model heavy           # use heavy model

Multi-camera usage:
  python mediapipe_sender.py --cameras 0,1                          # fallback mode (no calibration)
  python mediapipe_sender.py --cameras 0,1 --calibration cal.json   # calibrated 3D fusion
  python mediapipe_sender.py --cameras 0,1,2 --calibration cal.json # three cameras
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
import numpy as np

from camera_source import WebcamSource
from pose_detector import PoseDetector
from camera_calibration import CameraCalibration
from multi_camera_fusion import MultiCameraFusion


# ── Model management ──────────────────────────────────────────────────────────

MODEL_URLS = {
    "lite": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task",
    "full": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task",
    "heavy": "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task",
}


def get_model_path(model_name: str) -> str:
    """Get the model .task file path. Checks PyInstaller bundle, script dir, then downloads."""
    filename = f"pose_landmarker_{model_name}.task"

    search_dirs = []
    if getattr(sys, '_MEIPASS', None):
        search_dirs.append(sys._MEIPASS)
    search_dirs.append(os.path.dirname(os.path.abspath(sys.executable if getattr(sys, 'frozen', False) else __file__)))
    search_dirs.append(os.path.dirname(os.path.abspath(__file__)))

    for d in search_dirs:
        model_path = os.path.join(d, filename)
        if os.path.exists(model_path):
            print(f"Using model: {model_path}")
            return model_path

    url = MODEL_URLS.get(model_name)
    if url is None:
        print(f"ERROR: Unknown model '{model_name}'. Options: lite, full, heavy")
        sys.exit(1)

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


# ── Camera enumeration ────────────────────────────────────────────────────────

def list_cameras(max_index: int = 10):
    """List available camera devices by trying to open each index."""
    print("Scanning for cameras...")
    available = []
    for i in range(max_index):
        cap = cv2.VideoCapture(i)
        if cap.isOpened():
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
    """Select a camera by index or name substring match."""
    try:
        return int(camera_arg)
    except ValueError:
        pass

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


# ── Packet serialization ──────────────────────────────────────────────────────

def landmarks_to_unity_packet(world_landmarks, timestamp: float) -> bytes:
    """
    Single-camera path: convert MediaPipe world landmark objects to a binary Unity packet.

    MediaPipe world: x right, y down, z toward camera (hip-centered, meters)
    Unity:           x right, y up,   z forward
    Conversion: x stays, -y, -z
    """
    landmarks = world_landmarks[0]
    landmark_count = len(landmarks)

    packet = struct.pack('fi', timestamp, landmark_count)
    for lm in landmarks:
        packet += struct.pack('ffff',
                              lm.x,
                              -lm.y,
                              -lm.z,
                              lm.visibility)
    return packet


def array_to_unity_packet(landmarks: np.ndarray, timestamp: float) -> bytes:
    """
    Multi-camera path: pack a (33, 4) numpy array already in Unity space into a binary packet.
    Coordinates must already be converted to Unity space (x right, y up, z forward).
    """
    packet = struct.pack('fi', timestamp, len(landmarks))
    for lm in landmarks:
        packet += struct.pack('ffff', float(lm[0]), float(lm[1]), float(lm[2]), float(lm[3]))
    return packet


# ── Single-camera loop (unchanged from original) ─────────────────────────────

def run_single_camera(args, model_path: str, sock: socket.socket, dest: tuple):
    camera_index = select_camera(args.camera)

    cap = cv2.VideoCapture(camera_index)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    if not cap.isOpened():
        print(f"ERROR: Could not open camera {camera_index}")
        return

    actual_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    actual_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    print(f"Camera {camera_index}: {actual_w}x{actual_h}")

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

            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)

            timestamp_ms = int((time.time() - start_time) * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.pose_world_landmarks and len(result.pose_world_landmarks) > 0:
                timestamp = time.time() - start_time
                packet = landmarks_to_unity_packet(result.pose_world_landmarks, timestamp)
                sock.sendto(packet, dest)

            if args.show:
                if result.pose_landmarks and len(result.pose_landmarks) > 0:
                    landmarks = result.pose_landmarks[0]
                    h, w = frame.shape[:2]

                    for a, b in POSE_CONNECTIONS:
                        if a < len(landmarks) and b < len(landmarks):
                            pt1 = (int(landmarks[a].x * w), int(landmarks[a].y * h))
                            pt2 = (int(landmarks[b].x * w), int(landmarks[b].y * h))
                            cv2.line(frame, pt1, pt2, (0, 255, 0), 2)

                    for lm in landmarks:
                        cx, cy = int(lm.x * w), int(lm.y * h)
                        cv2.circle(frame, (cx, cy), 4, (0, 0, 255), -1)

                cv2.imshow('MediaPipe Pose', frame)
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    break

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
        if args.show:
            cv2.destroyAllWindows()
        print("Cleaned up.")


# ── Multi-camera loop ─────────────────────────────────────────────────────────

def run_multi_camera(args, model_path: str, sock: socket.socket, dest: tuple):
    camera_indices = [int(x.strip()) for x in args.cameras.split(',')]
    print(f"Multi-camera mode: cameras {camera_indices}")

    # load calibration if provided
    calibrations = [None] * len(camera_indices)
    if args.calibration:
        try:
            cal_map = CameraCalibration.load_all(args.calibration)
            for i, cam_idx in enumerate(camera_indices):
                if cam_idx in cal_map:
                    calibrations[i] = cal_map[cam_idx]
                else:
                    print(f"WARNING: Camera {cam_idx} not found in calibration file — treating as uncalibrated")
            print(f"Loaded calibration: {args.calibration}")
        except Exception as e:
            print(f"WARNING: Failed to load calibration file ({e}) — running in fallback mode")

    fusion = MultiCameraFusion(calibrations)

    sources = []
    detectors = []
    try:
        for cam_idx in camera_indices:
            sources.append(WebcamSource(cam_idx, args.width, args.height))
            detectors.append(PoseDetector(model_path, args.min_detection, args.min_tracking))
    except RuntimeError as e:
        print(f"ERROR: {e}")
        for s in sources:
            s.release()
        return

    start_time = time.time()
    frame_count = 0
    fps_report_interval = 60

    print(f"Running {len(camera_indices)}-camera mode... Press Ctrl+C to stop.")

    try:
        while True:
            timestamp_ms = int((time.time() - start_time) * 1000)
            timestamp = time.time() - start_time

            detections = []
            frames = []
            for src, det in zip(sources, detectors):
                frame = src.read()
                if frame is None:
                    print(f"WARNING: Camera {src.index} failed to read frame")
                    detections.append(None)
                    frames.append(None)
                    continue
                detection = det.detect(frame, timestamp_ms)
                detections.append(detection)
                frames.append(frame)

            fused = fusion.fuse(detections)
            if fused is not None:
                packet = array_to_unity_packet(fused, timestamp)
                sock.sendto(packet, dest)

            if args.show:
                for cam_idx, frame, detection in zip(camera_indices, frames, detections):
                    if frame is None:
                        continue
                    display = frame.copy()
                    if detection is not None:
                        h, w = display.shape[:2]
                        for lm in detection.landmarks_2d:
                            cx, cy = int(lm[0] * w), int(lm[1] * h)
                            cv2.circle(display, (cx, cy), 4, (0, 0, 255), -1)
                    cv2.imshow(f'Camera {cam_idx}', display)
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    break

            frame_count += 1
            if frame_count % fps_report_interval == 0:
                elapsed = time.time() - start_time
                fps = frame_count / elapsed
                detected_count = sum(1 for d in detections if d is not None)
                print(f"  FPS: {fps:.1f} | Cameras with pose: {detected_count}/{len(camera_indices)}")

    except KeyboardInterrupt:
        print("\nStopping...")

    finally:
        for det in detectors:
            det.close()
        for src in sources:
            src.release()
        if args.show:
            cv2.destroyAllWindows()
        print("Cleaned up.")


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description='MediaPipe Pose → Unity UDP sender')
    parser.add_argument('--list-cameras', action='store_true', help='List available cameras and exit')

    # single-camera
    parser.add_argument('--camera', type=str, default='0',
                        help='Camera index (int) or name to match (single-camera mode)')

    # multi-camera
    parser.add_argument('--cameras', type=str, default=None,
                        help='Comma-separated camera indices for multi-camera mode (e.g. 0,1)')
    parser.add_argument('--calibration', type=str, default=None,
                        help='Path to calibration JSON produced by mediapipe_calibrate')

    parser.add_argument('--width', type=int, default=1280, help='Capture width')
    parser.add_argument('--height', type=int, default=720, help='Capture height')
    parser.add_argument('--host', type=str, default='127.0.0.1', help='Unity UDP host')
    parser.add_argument('--port', type=int, default=7000, help='Unity UDP port')
    parser.add_argument('--model', type=str, default='full', choices=['lite', 'full', 'heavy'],
                        help='Model complexity: lite, full, heavy')
    parser.add_argument('--show', action='store_true', help='Show webcam preview window(s)')
    parser.add_argument('--min-detection', type=float, default=0.5,
                        help='Min detection confidence (0-1)')
    parser.add_argument('--min-tracking', type=float, default=0.5,
                        help='Min tracking confidence (0-1)')
    args = parser.parse_args()

    if args.list_cameras:
        list_cameras()
        return

    model_path = get_model_path(args.model)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.host, args.port)
    print(f"Sending landmarks to {dest[0]}:{dest[1]}")

    try:
        if args.cameras:
            run_multi_camera(args, model_path, sock, dest)
        else:
            run_single_camera(args, model_path, sock, dest)
    finally:
        sock.close()


if __name__ == '__main__':
    main()
