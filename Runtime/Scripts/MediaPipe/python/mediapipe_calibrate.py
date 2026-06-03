"""
Multi-camera calibration tool for mediapipe_sender.

Captures synchronized checkerboard frames from N cameras, computes intrinsics
per camera and extrinsics relative to camera 0, and saves a calibration.json
ready for use with:
  mediapipe_sender --cameras 0,1 --calibration calibration.json

Checkerboard: print a 10x7 square board at 25mm squares on A4 paper.
(Use --board 9x6 for the inner corner count of a 10x7 board.)

Usage:
  python mediapipe_calibrate.py --cameras 0,1 --board 9x6 --square 25 --output calibration.json

Controls during capture:
  SPACE  — capture current set (only when board is detected in ALL cameras)
  c      — compute and save calibration with collected frames
  q      — quit without saving
"""

import argparse
import json
import sys

import cv2
import numpy as np


def parse_board(board_str: str):
    """Parse '9x6' → (9, 6) inner corner count."""
    parts = board_str.lower().split('x')
    if len(parts) != 2:
        print(f"ERROR: --board must be WxH inner corners (e.g. 9x6), got '{board_str}'")
        sys.exit(1)
    return int(parts[0]), int(parts[1])


def make_object_points(corners_w: int, corners_h: int, square_mm: float) -> np.ndarray:
    """3D positions of inner corners on a flat checkerboard (mm units)."""
    objp = np.zeros((corners_w * corners_h, 3), dtype=np.float32)
    objp[:, :2] = np.mgrid[0:corners_w, 0:corners_h].T.reshape(-1, 2)
    objp *= square_mm
    return objp


def capture_phase(camera_indices, width, height, board_size, objp, min_captures):
    """
    Interactive loop: show live feeds, detect checkerboard, collect synchronized captures.
    Returns (obj_points_per_cam, img_points_per_cam, image_size).
    """
    caps = []
    for idx in camera_indices:
        cap = cv2.VideoCapture(idx)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        if not cap.isOpened():
            print(f"ERROR: Could not open camera {idx}")
            sys.exit(1)
        actual_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        actual_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        print(f"Camera {idx}: {actual_w}x{actual_h}")
        caps.append(cap)

    obj_points = [[] for _ in camera_indices]
    img_points = [[] for _ in camera_indices]
    image_size = None

    refine_criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.001)

    print(f"\nHold the checkerboard in view of ALL cameras.")
    print(f"  Green border = board detected in that camera.")
    print(f"  SPACE to capture | c to calibrate (need >= {min_captures}) | q to quit\n")

    try:
        while True:
            frames_display = []
            corners_per_cam = []

            for cap in caps:
                ret, frame = cap.read()
                if not ret:
                    frames_display.append(None)
                    corners_per_cam.append(None)
                    continue

                if image_size is None:
                    h, w = frame.shape[:2]
                    image_size = (w, h)

                gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                found, corners = cv2.findChessboardCorners(gray, board_size, None)

                if found:
                    corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), refine_criteria)
                    corners_per_cam.append(corners)
                else:
                    corners_per_cam.append(None)

                display = frame.copy()
                if found:
                    cv2.drawChessboardCorners(display, board_size, corners, found)
                    cv2.rectangle(display, (0, 0),
                                  (display.shape[1] - 1, display.shape[0] - 1), (0, 255, 0), 4)
                else:
                    cv2.rectangle(display, (0, 0),
                                  (display.shape[1] - 1, display.shape[0] - 1), (0, 0, 255), 4)

                n_captured = len(obj_points[0])
                cv2.putText(display, f"Captured: {n_captured}", (10, 30),
                            cv2.FONT_HERSHEY_SIMPLEX, 1.0, (255, 255, 0), 2)
                frames_display.append(display)

            for cam_idx, frame in zip(camera_indices, frames_display):
                if frame is not None:
                    cv2.imshow(f'Camera {cam_idx}', frame)

            all_detected = all(c is not None for c in corners_per_cam)
            n_captured = len(obj_points[0])

            key = cv2.waitKey(1) & 0xFF

            if key == ord(' '):
                if all_detected:
                    for i, corners in enumerate(corners_per_cam):
                        obj_points[i].append(objp.copy())
                        img_points[i].append(corners)
                    n_captured += 1
                    print(f"  Captured set {n_captured}")
                else:
                    missing = [camera_indices[i] for i, c in enumerate(corners_per_cam) if c is None]
                    print(f"  Board not detected in camera(s) {missing} — skipping")

            elif key == ord('c'):
                if n_captured >= min_captures:
                    break
                else:
                    print(f"  Need at least {min_captures} captures (have {n_captured})")

            elif key == ord('q'):
                print("Quitting without calibrating.")
                for cap in caps:
                    cap.release()
                cv2.destroyAllWindows()
                sys.exit(0)

    finally:
        for cap in caps:
            cap.release()
        cv2.destroyAllWindows()

    return obj_points, img_points, image_size


def calibrate(camera_indices, obj_points, img_points, image_size):
    """
    Compute intrinsics per camera (cv2.calibrateCamera) and
    extrinsics for each camera relative to camera 0 (cv2.stereoCalibrate).
    Returns (Ks, Ds, Rs, ts) — one entry per camera.
    Camera 0: R=I, t=0. Others: world-to-camera transform.
    """
    print("\nComputing intrinsics...")
    Ks, Ds = [], []
    for i, cam_idx in enumerate(camera_indices):
        ret, K, D, _, _ = cv2.calibrateCamera(
            obj_points[i], img_points[i], image_size, None, None
        )
        Ks.append(K)
        Ds.append(D)
        print(f"  Camera {cam_idx}: reprojection error = {ret:.4f} px")

    print("\nComputing extrinsics relative to camera 0...")
    Rs = [np.eye(3, dtype=np.float64)]
    ts = [np.zeros(3, dtype=np.float64)]

    for i in range(1, len(camera_indices)):
        ret, _, _, _, _, R, t, _, _ = cv2.stereoCalibrate(
            obj_points[0],   # common object points (all captures had board in all cameras)
            img_points[0],   # camera 0 image points
            img_points[i],   # camera i image points
            Ks[0], Ds[0],
            Ks[i], Ds[i],
            image_size,
            flags=cv2.CALIB_FIX_INTRINSIC,
        )
        Rs.append(R)
        ts.append(t.flatten())
        print(f"  Camera {camera_indices[i]} vs camera {camera_indices[0]}: stereo error = {ret:.4f} px")

    return Ks, Ds, Rs, ts


def save_calibration(path, camera_indices, Ks, Ds, Rs, ts, board_size, square_mm, image_size):
    data = {
        'image_size': list(image_size),
        'board': {
            'corners_w': board_size[0],
            'corners_h': board_size[1],
            'square_mm': float(square_mm),
        },
        'cameras': [
            {
                'index': cam_idx,
                'K': Ks[i].tolist(),
                'D': Ds[i].tolist(),
                'R': Rs[i].tolist(),
                't': ts[i].tolist(),
            }
            for i, cam_idx in enumerate(camera_indices)
        ],
    }

    with open(path, 'w') as f:
        json.dump(data, f, indent=2)

    print(f"\nCalibration saved to: {path}")


def main():
    parser = argparse.ArgumentParser(
        description='Multi-camera calibration tool for mediapipe_sender'
    )
    parser.add_argument('--cameras', type=str, required=True,
                        help='Comma-separated camera indices (e.g. 0,1)')
    parser.add_argument('--board', type=str, default='9x6',
                        help='Checkerboard inner corners WxH (default: 9x6 for a 10x7 board)')
    parser.add_argument('--square', type=float, default=25.0,
                        help='Physical square size in mm (default: 25)')
    parser.add_argument('--output', type=str, default='calibration.json',
                        help='Output JSON path (default: calibration.json)')
    parser.add_argument('--width', type=int, default=1280, help='Capture width')
    parser.add_argument('--height', type=int, default=720, help='Capture height')
    parser.add_argument('--min-captures', type=int, default=15,
                        help='Minimum frame sets to collect before calibrating (default: 15)')
    args = parser.parse_args()

    camera_indices = [int(x.strip()) for x in args.cameras.split(',')]
    if len(camera_indices) < 2:
        print("ERROR: --cameras must specify at least 2 camera indices.")
        sys.exit(1)

    board_size = parse_board(args.board)
    objp = make_object_points(board_size[0], board_size[1], args.square)

    print(f"Cameras:      {camera_indices}")
    print(f"Board:        {board_size[0]}x{board_size[1]} inner corners")
    print(f"Square size:  {args.square} mm")
    print(f"Min captures: {args.min_captures}")
    print(f"Output:       {args.output}")

    obj_points, img_points, image_size = capture_phase(
        camera_indices, args.width, args.height, board_size, objp, args.min_captures
    )

    Ks, Ds, Rs, ts = calibrate(camera_indices, obj_points, img_points, image_size)

    save_calibration(args.output, camera_indices, Ks, Ds, Rs, ts, board_size, args.square, image_size)

    print("\nDone! Use this calibration with mediapipe_sender:")
    cameras_str = ','.join(str(i) for i in camera_indices)
    print(f"  mediapipe_sender --cameras {cameras_str} --calibration {args.output}")


if __name__ == '__main__':
    main()
