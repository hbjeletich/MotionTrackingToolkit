import cv2
import mediapipe as mp
import numpy as np
from dataclasses import dataclass
from typing import Optional


@dataclass
class DetectionResult:
    landmarks_2d: np.ndarray    # (33, 3) — x, y normalized [0,1] and visibility
    landmarks_world: np.ndarray  # (33, 4) — x, y, z in meters (hip-centered) and visibility
    image_width: int
    image_height: int


class PoseDetector:
    """
    Wraps one MediaPipe PoseLandmarker.

    Returns both 2D normalized landmarks (for triangulation) and
    world landmarks (for single-camera / fallback path).
    """

    def __init__(self, model_path: str, min_detection_confidence: float = 0.5,
                 min_tracking_confidence: float = 0.5):
        BaseOptions = mp.tasks.BaseOptions
        PoseLandmarkerOptions = mp.tasks.vision.PoseLandmarkerOptions
        VisionRunningMode = mp.tasks.vision.RunningMode

        options = PoseLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=model_path),
            running_mode=VisionRunningMode.VIDEO,
            num_poses=1,
            min_pose_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
            output_segmentation_masks=False,
        )
        self._landmarker = mp.tasks.vision.PoseLandmarker.create_from_options(options)

    def detect(self, bgr_frame: np.ndarray, timestamp_ms: int) -> Optional[DetectionResult]:
        """Run pose detection on a BGR frame. Returns None if no pose is found."""
        h, w = bgr_frame.shape[:2]
        rgb = cv2.cvtColor(bgr_frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        result = self._landmarker.detect_for_video(mp_image, timestamp_ms)

        if (not result.pose_world_landmarks or len(result.pose_world_landmarks) == 0 or
                not result.pose_landmarks or len(result.pose_landmarks) == 0):
            return None

        world = result.pose_world_landmarks[0]
        lm2d = result.pose_landmarks[0]

        landmarks_world = np.array(
            [[lm.x, lm.y, lm.z, lm.visibility] for lm in world], dtype=np.float32
        )
        landmarks_2d = np.array(
            [[lm.x, lm.y, lm.visibility] for lm in lm2d], dtype=np.float32
        )

        return DetectionResult(
            landmarks_2d=landmarks_2d,
            landmarks_world=landmarks_world,
            image_width=w,
            image_height=h,
        )

    def close(self):
        self._landmarker.close()
