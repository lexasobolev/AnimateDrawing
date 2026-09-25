"""Bake Meta Animated Drawings BVH motions into compact JSON clips for the Unity game.

Meta's renderer (animated_drawings.render) turns a BVH motion into per-frame 2D bone
orientations with its Retargeter (3D joints -> projection planes -> angle CCW from +Y),
then rotates the drawing's rig so each bone matches. That retargeting step does not depend
on the character at all, so we run Meta's own Retargeter once per motion here and store the
result. The Unity game (DrawingRig / MotionClip) then applies those orientations to any
drawing's skeleton at runtime, exactly like AnimatedDrawingRig.set_global_orientations does,
without needing Python, OpenGL or pre-rendered MP4 clips per character.

Usage (from anywhere; only numpy, scikit-learn, pyyaml and setuptools are required):
    python bake_meta_motions.py --meta-repo <path to AnimatedDrawings clone> \
        --out ../../Assets/StreamingAssets/AnimatedDrawings/Motions
"""
import argparse
import json
import sys
import types
from pathlib import Path

import numpy as np

# motion name -> (motion cfg, retarget cfg), relative to the Meta repo root
MOTIONS = {
    "dab": ("examples/config/motion/dab.yaml", "examples/config/retarget/fair1_ppf.yaml"),
    "zombie": ("examples/config/motion/zombie.yaml", "examples/config/retarget/fair1_ppf.yaml"),
    "jumping": ("examples/config/motion/jumping.yaml", "examples/config/retarget/fair1_ppf.yaml"),
    "wave_hello": ("examples/config/motion/wave_hello.yaml", "examples/config/retarget/fair1_ppf.yaml"),
    "jumping_jacks": ("examples/config/motion/jumping_jacks.yaml", "examples/config/retarget/cmu1_pfp.yaml"),
    "jesse_dance": ("examples/config/motion/jesse_dance.yaml", "examples/config/retarget/rokoko1_ppf.yaml"),
}

PROJECT_META_FILES = Path(__file__).resolve().parents[1] / "AnimatedDrawings"

# the renderer plays clips at most this long; long BVHs are trimmed to keep JSON small
MAX_SECONDS = 12.0
# resample to this rate; plenty for crayon drawings and keeps files ~100KB
TARGET_FPS = 30.0


def import_meta(meta_repo: Path):
    sys.path.insert(0, str(meta_repo))
    # box.py (pulled in by bvh.py) imports OpenGL only for drawing; the retargeter never draws
    for name in ("OpenGL", "OpenGL.GL"):
        if name not in sys.modules:
            try:
                __import__(name)
            except ImportError:
                sys.modules[name] = types.ModuleType(name)
    from animated_drawings.config import MotionConfig, RetargetConfig
    from animated_drawings.model.retargeter import Retargeter
    return MotionConfig, RetargetConfig, Retargeter


def bake(meta_repo: Path, motion_cfg_rel: str, retarget_cfg_rel: str):
    MotionConfig, RetargetConfig, Retargeter = import_meta(meta_repo)
    motion_cfg = MotionConfig(str(meta_repo / motion_cfg_rel))
    retarget_cfg = RetargetConfig(str(_resolve(meta_repo, retarget_cfg_rel)))
    retargeter = Retargeter(motion_cfg, retarget_cfg)

    # same call AnimatedDrawing._initialize_retargeter_bvh makes for every mapped joint
    mapping = retarget_cfg.char_joint_bvh_joints_mapping
    for char_joint, (prox, dist) in mapping.items():
        retargeter.compute_orientations(prox, dist, char_joint)

    # vertical bob of the root (in "leg lengths"), so jumps/dances keep their up-down motion
    # while the game moves the character horizontally itself
    root = retargeter.bvh_root_positions
    leg = _bvh_leg_length(retargeter, retarget_cfg)
    root_y = (root[:, 1] - root[0, 1]) / max(leg, 1e-6)

    src_dt = retargeter.bvh.frame_time
    src_n = retargeter.bvh.frame_max_num
    duration = min(src_n * src_dt, MAX_SECONDS)
    n = max(2, int(round(duration * TARGET_FPS)))
    src_idx = np.clip(np.round(np.arange(n) / TARGET_FPS / src_dt).astype(int), 0, src_n - 1)

    joints = list(retargeter.char_joint_to_orientation.keys())
    orient = np.stack([retargeter.char_joint_to_orientation[j][src_idx] for j in joints], axis=1)

    groups = []
    for g in retarget_cfg.char_bodypart_groups:
        driver = g["bvh_depth_drivers"][0]
        depth = retargeter.bvh_joint_to_projection_depth.get(driver)
        groups.append({
            "driver": driver,
            "charJoints": list(g["char_joints"]),
            "depths": [round(float(v), 3) for v in (depth[src_idx] if depth is not None else np.zeros(n))],
        })

    return {
        "frameTime": round(1.0 / TARGET_FPS, 6),
        "frameCount": int(n),
        "joints": joints,
        "orientations": [round(float(v), 2) for v in orient.reshape(-1)],
        "rootY": [round(float(v), 4) for v in root_y[src_idx]],
        "depthGroups": groups,
    }


def _resolve(meta_repo: Path, rel: str) -> Path:
    # this project ships extra retarget configs (e.g. rokoko1_ppf.yaml) next to its own
    # customizations of the Meta repo; fall back to those when upstream doesn't have them
    upstream = meta_repo / rel
    if upstream.exists():
        return upstream
    return PROJECT_META_FILES / rel


def _bvh_leg_length(retargeter, retarget_cfg) -> float:
    total = 0.0
    for chain in retarget_cfg.char_bvh_root_offset["bvh_joints"]:
        for a, b in zip(chain, chain[1:]):
            ja = retargeter.bvh.root_joint.get_transform_by_name(a)
            jb = retargeter.bvh.root_joint.get_transform_by_name(b)
            total += float(np.linalg.norm(jb.get_world_position() - ja.get_world_position()))
    return total / max(1, len(retarget_cfg.char_bvh_root_offset["bvh_joints"]))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--meta-repo", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()

    meta_repo = args.meta_repo.resolve()
    args.out.mkdir(parents=True, exist_ok=True)
    for name, (motion_rel, retarget_rel) in MOTIONS.items():
        clip = bake(meta_repo, motion_rel, retarget_rel)
        clip["name"] = name
        path = args.out / f"{name}.json"
        path.write_text(json.dumps(clip, separators=(",", ":")), encoding="utf-8")
        print(f"baked {name}: {clip['frameCount']} frames, {len(clip['joints'])} joints -> {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
