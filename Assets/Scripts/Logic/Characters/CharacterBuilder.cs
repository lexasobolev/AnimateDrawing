using System;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Characters
{
    public sealed class BuiltCharacter
    {
        public CharacterAnnotation Annotation;
        public DrawingRig Rig;
        public SkinnedDrawingMesh Mesh;
        public CharacterKind GuessedKind;
    }

    // Turns a drawing into something the game can animate. Meta's models (via TorchServe) give
    // the best skeletons; without them the same Meta-style annotation is produced by the
    // detector-free segmenter + skeleton estimator.
    public static class CharacterBuilder
    {
        // Photo/scan of a drawing (or a PNG that is already cut out) -> annotation, no ML needed.
        public static CharacterAnnotation AnnotateWithoutModels(RgbaImage image)
        {
            RgbaImage texture;
            GrayImage mask;
            if (CharacterSegmenter.HasMeaningfulAlpha(image))
            {
                var working = image.ResizeToFit(1000);
                (texture, mask, _, _) = CharacterSegmenter.TrimToMask(working, CharacterSegmenter.MaskFromAlpha(working), 4);
            }
            else
            {
                (texture, mask, _, _) = CharacterSegmenter.CutOut(image);
            }

            var annotation = SkeletonEstimator.Estimate(mask);
            annotation.Texture = CharacterSegmenter.ApplyMask(texture, mask);
            annotation.Mask = mask;
            return annotation;
        }

        // Completes an annotation from Meta's files (texture.png, mask.png, char_cfg.yaml).
        public static CharacterAnnotation FromMetaFiles(RgbaImage texture, GrayImage mask, string charCfgYaml)
        {
            var annotation = CharacterAnnotation.ParseCharCfgYaml(charCfgYaml);
            if (annotation.Width != texture.Width || annotation.Height != texture.Height)
                throw new ArgumentException($"char_cfg.yaml is {annotation.Width}x{annotation.Height} but the texture is {texture.Width}x{texture.Height}");
            annotation.Mask = mask;
            annotation.Texture = CharacterSegmenter.ApplyMask(texture, mask);
            annotation.Source = "meta-files";
            return annotation;
        }

        public static BuiltCharacter Build(CharacterAnnotation annotation)
        {
            if (annotation.Mask == null) throw new ArgumentException("annotation needs a mask");
            var rig = new DrawingRig(annotation);
            return new BuiltCharacter
            {
                Annotation = annotation,
                Rig = rig,
                Mesh = SkinnedDrawingMesh.Build(annotation, rig),
                GuessedKind = GuessKind(annotation),
            };
        }

        // Rough guess the player can override: wide drawings are animals, drawings with clearly
        // separate legs and arms are people, anything else is a monster.
        public static CharacterKind GuessKind(CharacterAnnotation a)
        {
            if (a.Mask == null) return CharacterKind.Human;
            SkeletonEstimator.GetBounds(a.Mask, out var l, out var t, out var r, out var b);
            var w = r - l + 1f;
            var h = b - t + 1f;
            if (w > h * 1.35f) return CharacterKind.Animal;

            if (!a.TryGetJoint("left_foot", out var lf) || !a.TryGetJoint("right_foot", out var rf) ||
                !a.TryGetJoint("hip", out var hip) || !a.TryGetJoint("left_hand", out var lh) || !a.TryGetJoint("right_hand", out var rh) ||
                !a.TryGetJoint("torso", out var torso))
                return CharacterKind.Monster;

            // legs: is there a gap in the mask between the feet, just above them?
            var gapRow = (int)MathUtil.Lerp(hip.Location.Y, lf.Location.Y, 0.7f);
            var midX = (int)((lf.Location.X + rf.Location.X) * 0.5f);
            var separateLegs = gapRow >= 0 && gapRow < a.Mask.Height && midX >= 0 && midX < a.Mask.Width && a.Mask[midX, gapRow] == 0;
            var armReach = Math.Max(V2.Distance(lh.Location, torso.Location), V2.Distance(rh.Location, torso.Location)) / h;
            // separate legs + real arms + a not-too-solid body reads as a person; blobs are monsters.
            // It's only a default: the player can change the kind when adding the character.
            var bodyFill = a.Mask.CountNonZero() / (w * h);
            if (separateLegs && armReach > 0.18f && bodyFill < 0.55f) return CharacterKind.Human;
            return CharacterKind.Monster;
        }
    }
}
