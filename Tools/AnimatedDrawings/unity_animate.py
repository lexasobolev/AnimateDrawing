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

# The renderer accumulates the BVH root joint's actual frame-to-frame translation (all three
# axes, not just horizontal) into the character's on-screen position
# (Retargeter.scale_root_positions_for_character) — meant for clips where the character should
# visibly travel across the frame. Our clips loop in place while Unity moves the GameObject
# itself, so left as-is a motion with real root movement (e.g. zombie.bvh, whose root drifts
# far more vertically than horizontally) carries the character off-camera and never returns.
# Freezing the root's X/Y/Z position to its first frame keeps every clip centered, in place —
# limb motion (and any natural bob) survives untouched since that comes from joint rotations,
# not the root's own position channels.
def make_inplace_bvh(src_bvh: Path, cache_dir: Path) -> Path:
    dst_bvh = cache_dir / f"{src_bvh.stem}_inplace.bvh"
    if dst_bvh.exists() and dst_bvh.stat().st_mtime >= src_bvh.stat().st_mtime:
        return dst_bvh

    lines = src_bvh.read_text().splitlines()
    motion_idx = lines.index("MOTION")
    frame_count = int(lines[motion_idx + 1].split(":")[-1])
    data_start = motion_idx + 3  # MOTION / Frames: / Frame Time: / <data...>

    out_lines = lines[:data_start]
    first_xyz = None
    for line in lines[data_start:data_start + frame_count]:
        values = line.split()
        if first_xyz is None:
            first_xyz = values[:3]
        values[0], values[1], values[2] = first_xyz
        out_lines.append(" ".join(values))

    cache_dir.mkdir(parents=True, exist_ok=True)
    dst_bvh.write_text("\n".join(out_lines) + "\n", encoding="utf-8")
    return dst_bvh


def make_inplace_motion_cfg(motion_yaml: Path, cache_dir: Path) -> Path:
    motion = yaml.safe_load(motion_yaml.read_text(encoding="utf-8"))
    src_bvh = (REPO_ROOT / motion["filepath"]).resolve()
    motion["filepath"] = str(make_inplace_bvh(src_bvh, cache_dir))

    dst_yaml = cache_dir / motion_yaml.name
    dst_yaml.write_text(yaml.safe_dump(motion), encoding="utf-8")
    return dst_yaml


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
    inplace_cache = REPO_ROOT / ".inplace_bvh_cache"
    for state, motion_name in MOTIONS.items():
        state_dir = output / state
        state_dir.mkdir(parents=True, exist_ok=True)
        print(f"[AnimatedDrawings] rendering {state} with {motion_name}", flush=True)
        target = state_dir / "video.mp4"
        motion_retarget = MOTION_RETARGETS.get(motion_name)
        motion_retarget_path = Path(motion_retarget).resolve() if motion_retarget else retarget
        motion_cfg_path = make_inplace_motion_cfg(motion_dir / motion_name, inplace_cache)
        config = {
            "scene": {
                "ANIMATED_CHARACTERS": [{
                    "character_cfg": str((annotations / "char_cfg.yaml").resolve()),
                    "motion_cfg": str(motion_cfg_path),
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
