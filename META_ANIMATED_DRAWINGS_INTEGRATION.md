# Meta Animated Drawings integration

The game uses Meta's [AnimatedDrawings](https://github.com/facebookresearch/AnimatedDrawings) in
two places:

* **Skeletons for new drawings (optional).** When Meta's TorchServe container is running, drawings
  added through **Add character...** are sent to its `drawn_humanoid_detector` and
  `drawn_humanoid_pose_estimator` (the same requests Meta's `image_to_annotations.py` makes, see
  `Assets/Scripts/Runtime/MetaTorchServeClient.cs`). Without the container the game uses its
  built-in cut-out and skeleton estimator.
* **Motions.** Meta's BVH motions are retargeted once, offline, with Meta's own `Retargeter` and
  stored in `Assets/StreamingAssets/AnimatedDrawings/Motions`; the game animates every drawing with
  them in real time on a C# port of Meta's rig.

## Running Meta's models (optional)

Clone the Meta repository to `Tools/AnimatedDrawings` (it is git-ignored apart from this project's
own files there: the TorchServe `Dockerfile`/`config.properties` and an extra retarget config).
Docker Desktop must be running:

```powershell
cd Tools/AnimatedDrawings/torchserve
docker build -t animated-drawings-torchserve .
docker run -d --name animated-drawings-torchserve -p 8080:8080 -p 8081:8081 animated-drawings-torchserve
Invoke-WebRequest http://localhost:8080/ping
```

The ping should report a healthy TorchServe instance; the game checks it at startup
(`GameController → Torch Serve Url`). Meta's models expect a mostly human-like drawing — monsters
and animals usually get a better skeleton from the built-in estimator.

## Re-baking the motions

`Tools/MotionBaker/bake_meta_motions.py` needs a clone of the Meta repository and numpy < 1.25,
scikit-learn, pyyaml, pillow and opencv:

```bash
python Tools/MotionBaker/bake_meta_motions.py --meta-repo <AnimatedDrawings clone> \
    --out Assets/StreamingAssets/AnimatedDrawings/Motions
```
