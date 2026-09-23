"""Generate Meta Animated Drawings clips for the Unity project.

Run from the AnimatedDrawings repository with its Python environment installed.
TorchServe must be available at http://localhost:8080.
"""
import argparse
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(REPO_ROOT / "examples"))

import animated_drawings.render
import yaml
from image_to_annotations import image_to_annotations

MOTIONS = {
    "Idle": "dab.yaml",
    "Walk": "zombie.yaml",
    "Yawn": "jumping.yaml",
    "Sleep": "zombie.yaml",
    "Wave": "wave_hello.yaml",
    "Surprised": "jumping_jacks.yaml",
    "Dance": "jesse_dance.yaml",
}

# motions sourced from a BVH with a different joint-naming convention need their
# own retarget config; everything else falls back to the --retarget default (fair1)
MOTION_RETARGETS = {
    "jumping_jacks.yaml": "examples/config/retarget/cmu1_pfp.yaml",
    "jesse_dance.yaml": "examples/config/retarget/rokoko1_ppf.yaml",
}

# MP4 can't carry an alpha channel, so the renderer clears to this color instead of white;
# Unity's chroma-key shader on the video quad discards pixels matching it. Keep this in sync
# with the shader's default _KeyColor (Assets/Shaders/ChromaKeyUnlit.shader).
CHROMA_KEY_RGB = (1.0, 0.0, 1.0)  # magenta: unlikely to appear in a hand-drawn character
CHROMA_KEY_RGB_255 = tuple(round(c * 255) for c in CHROMA_KEY_RGB)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("image")
    parser.add_argument("output")
    parser.add_argument("--retarget", default="examples/config/retarget/fair1_ppf.yaml")
    args = parser.parse_args()

    image = Path(args.image).resolve()
    output = Path(args.output).resolve()
    annotations = output / "annotations"
    annotations.mkdir(parents=True, exist_ok=True)

    print(f"[AnimatedDrawings] annotating {image}", flush=True)
    image_to_annotations(str(image), str(annotations), background_key_color=CHROMA_KEY_RGB_255)

    retarget = Path(args.retarget).resolve()
    motion_dir = Path("examples/config/motion").resolve()
    for state, motion_name in MOTIONS.items():
        state_dir = output / state
        state_dir.mkdir(parents=True, exist_ok=True)
        print(f"[AnimatedDrawings] rendering {state} with {motion_name}", flush=True)
        target = state_dir / "video.mp4"
        motion_retarget = MOTION_RETARGETS.get(motion_name)
        motion_retarget_path = Path(motion_retarget).resolve() if motion_retarget else retarget
        config = {
            "scene": {
                "ANIMATED_CHARACTERS": [{
                    "character_cfg": str((annotations / "char_cfg.yaml").resolve()),
                    "motion_cfg": str((motion_dir / motion_name).resolve()),
                    "retarget_cfg": str(motion_retarget_path),
                }]
            },
            "controller": {
                "MODE": "video_render",
                "OUTPUT_VIDEO_PATH": str(target),
                "OUTPUT_VIDEO_CODEC": "avc1",
            },
            "view": {
                "CLEAR_COLOR": [*CHROMA_KEY_RGB, 1.0],
            },
        }
        config_path = state_dir / "mvc_cfg.yaml"
        config_path.write_text(yaml.safe_dump(config), encoding="utf-8")
        animated_drawings.render.start(str(config_path))
        if not target.exists():
            raise RuntimeError(f"Meta renderer did not create {target}")

    print(f"[AnimatedDrawings] completed: {output}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
