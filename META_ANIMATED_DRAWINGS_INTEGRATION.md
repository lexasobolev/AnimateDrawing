# Meta Animated Drawings integration

The official repository is downloaded to `Tools/AnimatedDrawings`.

## One-time setup on Windows

Use Python 3.8.13 as recommended by the Meta repository. From the Unity project root:

```powershell
py -3.8 -m venv .venv-animated-drawings
.\.venv-animated-drawings\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -e .\Tools\AnimatedDrawings
```

Start the pose/detector service. Docker Desktop must be running:

```powershell
cd Tools/AnimatedDrawings/torchserve
docker build -t animated-drawings-torchserve .
docker run -d --name animated-drawings-torchserve -p 8080:8080 -p 8081:8081 animated-drawings-torchserve
Invoke-WebRequest http://localhost:8080/ping
```

The ping should report a healthy TorchServe instance.

## Use from Unity

In the Unity Editor choose:

`AnimatedDrawingsWorld -> Upload and Animate with Meta...`

Select a human-like drawing. Unity runs `Tools/AnimatedDrawings/unity_animate.py`, creates Meta annotations, renders clips for all current states, and creates a character in the `World` object.

Generated files are stored under:

`Assets/StreamingAssets/AnimatedDrawings/<drawing-name>/`

The `MetaAnimatedDrawingPlayer` switches the generated MP4 clip when the existing `BehaviorState` changes.

## Requirements and limitations

- The official pose estimator expects a mostly human-like drawing.
- Docker/TorchServe and the Python dependencies must be installed before using the menu item.
- Rendering six motion clips can take several minutes.
- Unity uses the generated MP4 clips. The original SpriteRenderer remains as a fallback if a clip is missing.
- The bridge uses the repository's MIT-licensed code and does not copy Meta model weights into the Unity project.

## Use from the game (runtime)

`Assets/Scenes/GameScene.unity` talks to the same TorchServe container directly: when
`http://localhost:8080/ping` is healthy, drawings added through **Add character...** are sent to
`drawn_humanoid_detector` and `drawn_humanoid_pose_estimator` (the requests
`image_to_annotations.py` makes) and animated in real time on a Meta-style rig — no Python or MP4
rendering needed. Without the container the game falls back to its built-in cut-out and skeleton
estimator.

Meta's BVH motions are pre-retargeted with Meta's own `Retargeter` by
`Tools/MotionBaker/bake_meta_motions.py` (needs a clone of the Meta repository and
numpy < 1.25, scikit-learn, pyyaml, pillow, opencv):

```bash
python Tools/MotionBaker/bake_meta_motions.py --meta-repo <AnimatedDrawings clone> \
    --out Assets/StreamingAssets/AnimatedDrawings/Motions
```
