"""
Multi-camera pose fusion.

Calibrated path: triangulates per-camera 2D landmarks to true 3D using DLT
(cv2.triangulatePoints) and camera extrinsics. Output is in Unity space.

Fallback path (no calibration): picks whichever camera has highest mean
landmark confidence and applies the standard MediaPipe→Unity coordinate
conversion. Accuracy is equivalent to single-camera mode.

Coordinate conventions:
  MediaPipe world: x right, y down, z toward camera (hip-centered, meters)
  OpenCV world (camera 0 frame): x right, y down, z into scene (meters)
  Unity: x right, y up, z forward

MediaPipe world → Unity: x stays, -y, -z
OpenCV world   → Unity: x stays, -y,  z
"""

import cv2
import numpy as np
from typing import List, Optional

from camera_calibration import CameraCalibration
from pose_detector import DetectionResult

LANDMARK_COUNT = 33
VISIBILITY_THRESHOLD = 0.5


class MultiCameraFusion:
    """
    Fuses pose detections from N cameras into a (33, 4) skeleton in Unity space.

    Pass one CameraCalibration per camera (or None for uncalibrated cameras).
    Calibrated mode requires all cameras to have calibration data.
    """

    def __init__(self, calibrations: List[Optional[CameraCalibration]]):
        self._calibrations = calibrations
        self._is_calibrated = (
            len(calibrations) >= 2 and all(c is not None for c in calibrations)
        )
        self._prev_fused = np.zeros((LANDMARK_COUNT, 4), dtype=np.float32)

        if not self._is_calibrated:
            print("WARNING: No calibration provided — using best-confidence camera per frame. "
                  "Run mediapipe_calibrate and pass --calibration for true 3D fusion.")

    def fuse(self, detections: List[Optional[DetectionResult]]) -> Optional[np.ndarray]:
        """
        Returns (33, 4) [x, y, z, confidence] in Unity space,
        or None if all cameras have lost tracking.
        """
        valid = [(i, det) for i, det in enumerate(detections) if det is not None]
        if not valid:
            return None

        if self._is_calibrated and len(valid) >= 2:
            result = self._fuse_calibrated(valid)
        else:
            result = self._fuse_fallback(valid)

        self._prev_fused = result.copy()
        return result

    # ── Fallback ────────────────────────────────────────────────────────────

    def _fuse_fallback(self, valid: list) -> np.ndarray:
        _, best_det = max(valid, key=lambda x: float(x[1].landmarks_world[:, 3].mean()))
        return _mediapipe_world_to_unity(best_det.landmarks_world)

    # ── Calibrated: DLT triangulation ───────────────────────────────────────

    def _fuse_calibrated(self, valid: list) -> np.ndarray:
        fused = np.zeros((LANDMARK_COUNT, 4), dtype=np.float32)
        for lm_idx in range(LANDMARK_COUNT):
            fused[lm_idx] = self._fuse_landmark(lm_idx, valid)
        return fused

    def _fuse_landmark(self, lm_idx: int, valid: list) -> np.ndarray:
        """Return (4,) [x, y, z, conf] in Unity space for one landmark index."""
        visible = []
        for cam_local_idx, det in valid:
            vis = float(det.landmarks_2d[lm_idx, 2])
            if vis >= VISIBILITY_THRESHOLD and self._calibrations[cam_local_idx] is not None:
                px = float(det.landmarks_2d[lm_idx, 0] * det.image_width)
                py = float(det.landmarks_2d[lm_idx, 1] * det.image_height)
                visible.append((cam_local_idx, np.array([px, py], dtype=np.float64), vis))

        if len(visible) == 0:
            prev = self._prev_fused[lm_idx].copy()
            prev[3] = 0.0
            return prev

        if len(visible) == 1:
            # can't triangulate — hold last known position with reduced confidence
            prev = self._prev_fused[lm_idx].copy()
            prev[3] = visible[0][2] * 0.3
            return prev

        pts3d = []
        weights = []
        for i in range(len(visible)):
            for j in range(i + 1, len(visible)):
                cam_i, pt_i, vis_i = visible[i]
                cam_j, pt_j, vis_j = visible[j]
                pt3d = _triangulate(pt_i, self._calibrations[cam_i],
                                    pt_j, self._calibrations[cam_j])
                if pt3d is not None:
                    pts3d.append(pt3d)
                    weights.append(vis_i * vis_j)

        if not pts3d:
            prev = self._prev_fused[lm_idx].copy()
            prev[3] = 0.0
            return prev

        w = np.array(weights, dtype=np.float64)
        w /= w.sum()
        pt3d = np.average(pts3d, axis=0, weights=w).astype(np.float32)
        max_vis = max(v for _, _, v in visible)

        # OpenCV world (camera-0 frame) → Unity: x stays, -y (down→up), z stays (into scene = forward)
        return np.array([pt3d[0], -pt3d[1], pt3d[2], max_vis], dtype=np.float32)


# ── Module-level helpers ─────────────────────────────────────────────────────

def _mediapipe_world_to_unity(world: np.ndarray) -> np.ndarray:
    """Convert (33, 4) MediaPipe world landmarks [x,y,z,vis] to Unity space."""
    out = world.copy()
    out[:, 1] = -world[:, 1]  # y: down → up
    out[:, 2] = -world[:, 2]  # z: toward camera → forward
    return out


def _triangulate(pt_a: np.ndarray, cal_a: CameraCalibration,
                 pt_b: np.ndarray, cal_b: CameraCalibration) -> Optional[np.ndarray]:
    """
    Triangulate a 3D world point (in camera-0 frame) from two pixel observations.

    Uses cv2.undistortPoints to remove lens distortion and the intrinsic matrix,
    then cv2.triangulatePoints with the [R|t] projection matrices (no K needed
    since undistortPoints already normalized the coordinates).

    Returns (3,) float32 or None if the homogeneous w component is degenerate.
    """
    pts_a = cv2.undistortPoints(
        pt_a.reshape(1, 1, 2), cal_a.camera_matrix, cal_a.dist_coeffs
    )
    pts_b = cv2.undistortPoints(
        pt_b.reshape(1, 1, 2), cal_b.camera_matrix, cal_b.dist_coeffs
    )

    # [R|t] without K — matching the undistorted normalized coordinates
    P_a = np.hstack([cal_a.R, cal_a.t])
    P_b = np.hstack([cal_b.R, cal_b.t])

    pts4d = cv2.triangulatePoints(
        P_a, P_b,
        pts_a.reshape(2, 1),
        pts_b.reshape(2, 1),
    )

    w = float(pts4d[3])
    if abs(w) < 1e-8:
        return None

    return (pts4d[:3] / w).flatten().astype(np.float32)
