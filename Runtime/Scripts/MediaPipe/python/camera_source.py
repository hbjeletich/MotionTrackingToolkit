import cv2
import numpy as np
from typing import Optional


class WebcamSource:
    """Owns a single camera capture. Call read() each frame to get a BGR image."""

    def __init__(self, camera_index: int, width: int = 1280, height: int = 720):
        self._index = camera_index
        self._cap = cv2.VideoCapture(camera_index)
        self._cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self._cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        if not self._cap.isOpened():
            raise RuntimeError(f"Could not open camera {camera_index}")
        actual_w = int(self._cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        actual_h = int(self._cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        print(f"Camera {camera_index}: {actual_w}x{actual_h}")

    @property
    def index(self) -> int:
        return self._index

    def read(self) -> Optional[np.ndarray]:
        """Returns a BGR frame, or None on read failure."""
        ret, frame = self._cap.read()
        return frame if ret else None

    def release(self):
        self._cap.release()
