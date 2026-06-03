import json
import numpy as np
from typing import Dict


class CameraCalibration:
    """
    Intrinsic and extrinsic calibration for one camera.

    Coordinate convention: camera 0 is the world origin (R=I, t=0).
    R and t express the world-to-camera transform:
      p_camera = R @ p_world + t
    """

    def __init__(self, camera_matrix: np.ndarray, dist_coeffs: np.ndarray,
                 R: np.ndarray, t: np.ndarray):
        self.camera_matrix = camera_matrix.astype(np.float64)  # (3, 3)
        self.dist_coeffs = dist_coeffs.astype(np.float64)      # (5,) or similar
        self.R = R.astype(np.float64)                           # (3, 3)
        self.t = t.astype(np.float64).reshape(3, 1)            # (3, 1)

    @classmethod
    def from_dict(cls, data: dict) -> 'CameraCalibration':
        return cls(
            camera_matrix=np.array(data['K']),
            dist_coeffs=np.array(data['D']),
            R=np.array(data['R']),
            t=np.array(data['t']),
        )

    @classmethod
    def load_all(cls, path: str) -> Dict[int, 'CameraCalibration']:
        """Load all cameras from a calibration JSON. Returns dict keyed by camera index."""
        with open(path) as f:
            data = json.load(f)
        return {cam['index']: cls.from_dict(cam) for cam in data['cameras']}

    def to_dict(self, camera_index: int) -> dict:
        return {
            'index': camera_index,
            'K': self.camera_matrix.tolist(),
            'D': self.dist_coeffs.tolist(),
            'R': self.R.tolist(),
            't': self.t.flatten().tolist(),
        }
