using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Characters
{
    // Detector-free fallback for Meta's drawn_humanoid_pose_estimator: guesses the same
    // 16-joint skeleton from the silhouette alone. It is used when the TorchServe container
    // isn't running, and also for monsters/animals that Meta's humanoid models reject.
    // Image coordinates, y down; "right_*" joints sit on the IMAGE LEFT like Meta's
    // front-facing convention.
    public static class SkeletonEstimator
    {
        public static CharacterAnnotation Estimate(GrayImage mask)
        {
            var w = mask.Width;
            var h = mask.Height;
            GetBounds(mask, out var left, out var top, out var right, out var bottom);
            var bh = Math.Max(1, bottom - top);
            var bw = Math.Max(1, right - left);

            var rowMin = new int[h];
            var rowMax = new int[h];
            var rowCount = new int[h];
            for (var y = 0; y < h; y++)
            {
                rowMin[y] = int.MaxValue;
                rowMax[y] = -1;
                for (var x = 0; x < w; x++)
                {
                    if (mask[x, y] == 0) continue;
                    if (x < rowMin[y]) rowMin[y] = x;
                    if (x > rowMax[y]) rowMax[y] = x;
                    rowCount[y]++;
                }
            }

            float RowCenter(int y)
            {
                y = MathUtil.Clamp(y, top, bottom);
                double sum = 0;
                var n = 0;
                for (var x = 0; x < w; x++)
                {
                    if (mask[x, y] == 0) continue;
                    sum += x;
                    n++;
                }
                return n > 0 ? (float)(sum / n) : (left + right) * 0.5f;
            }

            // --- legs: the lowest stretch of rows where the silhouette splits into 2+ runs
            var minRunWidth = Math.Max(2, (int)(bw * 0.03f));
            var crotchY = -1;
            var consecutiveSingle = 0;
            for (var y = bottom - (int)(bh * 0.04f); y > top + bh * 0.3f; y--)
            {
                var runs = SignificantRuns(mask, y, minRunWidth);
                if (runs.Count >= 2)
                {
                    crotchY = y;
                    consecutiveSingle = 0;
                }
                else if (crotchY >= 0 && ++consecutiveSingle > Math.Max(2, bh * 0.03f)) break;
                else if (crotchY < 0 && y < bottom - bh * 0.25f) break; // no separation near the bottom
            }

            var hipY = crotchY > 0 ? crotchY - bh * 0.03f : top + bh * 0.6f;
            hipY = MathUtil.Clamp(hipY, top + bh * 0.35f, top + bh * 0.8f);
            var hipX = RowCenter((int)hipY);

            // --- feet: mean x of the bottom rows on each side of the hip line
            var footBandTop = bottom - Math.Max(2, (int)(bh * 0.07f));
            double lSum = 0, rSum = 0;
            int lN = 0, rN = 0;
            for (var y = footBandTop; y <= bottom; y++)
            for (var x = 0; x < w; x++)
            {
                if (mask[x, y] == 0) continue;
                if (x < hipX) { lSum += x; lN++; }
                else { rSum += x; rN++; }
            }
            var bottomSpanL = rowMin[bottom] == int.MaxValue ? left : rowMin[bottom];
            var bottomSpanR = rowMax[bottom] < 0 ? right : rowMax[bottom];
            var imageLeftFootX = lN > 0 ? (float)(lSum / lN) : MathUtil.Lerp(bottomSpanL, hipX, 0.5f);
            var imageRightFootX = rN > 0 ? (float)(rSum / rN) : MathUtil.Lerp(hipX, bottomSpanR, 0.5f);
            // a single blob foot (monster) gets two feet spread across it
            if (imageRightFootX - imageLeftFootX < bw * 0.15f)
            {
                imageLeftFootX = MathUtil.Lerp(left, hipX, 0.45f);
                imageRightFootX = MathUtil.Lerp(hipX, right, 0.55f);
            }
            var footY = bottom - bh * 0.02f;

            // --- head: look for a neck constriction below the top of the drawing
            var headBottom = -1;
            var widestAbove = 0;
            for (var y = top; y < top + bh * 0.5f; y++)
            {
                var width = rowMax[y] >= 0 ? rowMax[y] - rowMin[y] : 0;
                if (y > top + bh * 0.1f && widestAbove > 0 && width < widestAbove * 0.7f && rowCount[y] > 0)
                {
                    // continue down while it keeps narrowing to find the narrowest point
                    var best = y;
                    var bestWidth = width;
                    for (var yy = y; yy < Math.Min(top + bh * 0.5f, y + bh * 0.1f); yy++)
                    {
                        var ww = rowMax[yy] >= 0 ? rowMax[yy] - rowMin[yy] : 0;
                        if (ww < bestWidth) { bestWidth = ww; best = yy; }
                    }
                    headBottom = best;
                    break;
                }
                widestAbove = Math.Max(widestAbove, width);
            }
            if (headBottom < 0 || headBottom > hipY - bh * 0.1f) headBottom = (int)(top + Math.Min(0.3f * bh, (hipY - top) * 0.45f));

            var headCenterY = (top + headBottom) * 0.5f;
            var neck = new V2(RowCenter((int)headCenterY), headCenterY + (headBottom - top) * 0.1f);
            var torsoY = headBottom + bh * 0.05f;
            var torso = new V2(RowCenter((int)torsoY), torsoY);

            // body width just under the head, ignoring outstretched arms further down
            var bodyWidth = 0f;
            var samples = 0;
            for (var y = headBottom; y < headBottom + bh * 0.06f && y < h; y++)
            {
                if (rowMax[y] < 0) continue;
                bodyWidth += rowMax[y] - rowMin[y];
                samples++;
            }
            bodyWidth = samples > 0 ? bodyWidth / samples : bw * 0.4f;
            bodyWidth = MathUtil.Clamp(bodyWidth, bw * 0.12f, bw * 0.9f);

            // --- hands: the ink farthest from the chest on each side, outside the body column.
            // Farthest-point (not just leftmost) also catches arms raised above the head.
            var armBottom = hipY - bh * 0.02f;
            var bodyHalf = bodyWidth * 0.5f;
            var chest = new V2(torso.X, MathUtil.Lerp(torsoY, hipY, 0.2f));
            var rightHand = FarthestInk(mask, chest, top, (int)armBottom, x => x < torso.X - bodyHalf * 1.1f);
            var leftHand = FarthestInk(mask, chest, top, (int)armBottom, x => x > torso.X + bodyHalf * 1.1f);
            var chestY = MathUtil.Lerp(torsoY, hipY, 0.35f);
            var chestRow = MathUtil.Clamp((int)chestY, top, bottom);
            // "arms" too short to be limbs (armless monsters, arms painted on the body) become
            // small stubs at the sides of the chest
            var minReach = bodyWidth * 0.45f + bh * 0.08f;
            if (rightHand == null || V2.Distance(rightHand.Value, chest) < minReach)
                rightHand = new V2((rowMin[chestRow] == int.MaxValue ? torso.X - bodyHalf : rowMin[chestRow]) + bodyWidth * 0.05f, chestY + bh * 0.08f);
            if (leftHand == null || V2.Distance(leftHand.Value, chest) < minReach)
                leftHand = new V2((rowMax[chestRow] < 0 ? torso.X + bodyHalf : rowMax[chestRow]) - bodyWidth * 0.05f, chestY + bh * 0.08f);

            var shoulderHalf = Math.Min(bodyWidth * 0.35f, Math.Abs(leftHand.Value.X - rightHand.Value.X) * 0.25f);
            var shoulderY = torso.Y + bh * 0.02f;
            var rightShoulder = new V2(torso.X - shoulderHalf, shoulderY);
            var leftShoulder = new V2(torso.X + shoulderHalf, shoulderY);

            var hip = new V2(hipX, hipY);
            var hipHalf = Math.Max(bw * 0.04f, Math.Min(bodyWidth * 0.25f, (imageRightFootX - imageLeftFootX) * 0.25f));
            var rightHip = new V2(hipX - hipHalf, hipY);
            var leftHip = new V2(hipX + hipHalf, hipY);
            var rightFoot = new V2(imageLeftFootX, footY);
            var leftFoot = new V2(imageRightFootX, footY);

            var joints = new Dictionary<string, V2>
            {
                ["root"] = hip,
                ["hip"] = hip,
                ["torso"] = torso,
                ["neck"] = neck,
                ["right_shoulder"] = rightShoulder,
                ["right_elbow"] = V2.Lerp(rightShoulder, rightHand.Value, 0.5f),
                ["right_hand"] = rightHand.Value,
                ["left_shoulder"] = leftShoulder,
                ["left_elbow"] = V2.Lerp(leftShoulder, leftHand.Value, 0.5f),
                ["left_hand"] = leftHand.Value,
                ["right_hip"] = rightHip,
                ["right_knee"] = V2.Lerp(rightHip, rightFoot, 0.5f),
                ["right_foot"] = rightFoot,
                ["left_hip"] = leftHip,
                ["left_knee"] = V2.Lerp(leftHip, leftFoot, 0.5f),
                ["left_foot"] = leftFoot,
            };

            // limbs of kids' drawings are rarely straight lines; pull the midpoints onto the ink
            foreach (var name in new[] { "right_elbow", "left_elbow", "right_knee", "left_knee", "right_hand", "left_hand", "right_foot", "left_foot" })
                joints[name] = SnapToMask(mask, joints[name], bh * 0.12f);

            var annotation = CharacterAnnotation.FromJointLocations(w, h, joints);
            annotation.Mask = mask;
            annotation.Source = "heuristic";
            return annotation;
        }

        private static List<(int start, int end)> SignificantRuns(GrayImage mask, int y, int minWidth)
        {
            var runs = CharacterSegmenter.RowRuns(mask, y);
            runs.RemoveAll(r => r.end - r.start + 1 < minWidth);
            return runs;
        }

        private static V2? FarthestInk(GrayImage mask, V2 from, int y0, int y1, Func<int, bool> columnFilter)
        {
            V2? best = null;
            var bestD = 0f;
            y1 = Math.Min(y1, mask.Height - 1);
            for (var y = Math.Max(0, y0); y <= y1; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask[x, y] == 0 || !columnFilter(x)) continue;
                var d = (x - from.X) * (x - from.X) + (y - from.Y) * (y - from.Y);
                if (d <= bestD) continue;
                bestD = d;
                best = new V2(x, y);
            }
            return best;
        }

        public static V2 SnapToMask(GrayImage mask, V2 p, float maxDistance)
        {
            var px = MathUtil.Clamp((int)MathF.Round(p.X), 0, mask.Width - 1);
            var py = MathUtil.Clamp((int)MathF.Round(p.Y), 0, mask.Height - 1);
            if (mask[px, py] != 0) return p;
            var r = (int)MathF.Ceiling(maxDistance);
            var best = p;
            var bestD = float.MaxValue;
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                var x = px + dx;
                var y = py + dy;
                if (!mask.InBounds(x, y) || mask[x, y] == 0) continue;
                var d = dx * dx + dy * dy;
                if (d >= bestD) continue;
                bestD = d;
                best = new V2(x, y);
            }
            return best;
        }

        public static void GetBounds(GrayImage mask, out int left, out int top, out int right, out int bottom)
        {
            left = mask.Width;
            top = mask.Height;
            right = -1;
            bottom = -1;
            for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask[x, y] == 0) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
            if (right < 0)
            {
                left = 0;
                top = 0;
                right = mask.Width - 1;
                bottom = mask.Height - 1;
            }
        }
    }

    // Maps the 17 COCO keypoints returned by Meta's drawn_humanoid_pose_estimator into the
    // 16-joint rig — the exact mapping from image_to_annotations.py.
    public static class CocoSkeletonMapping
    {
        public static CharacterAnnotation FromKeypoints(int width, int height, IReadOnlyList<V2> k)
        {
            if (k.Count < 17) throw new ArgumentException("Expected 17 COCO keypoints");
            var joints = new Dictionary<string, V2>
            {
                ["root"] = (k[11] + k[12]) * 0.5f,
                ["hip"] = (k[11] + k[12]) * 0.5f,
                ["torso"] = (k[5] + k[6]) * 0.5f,
                ["neck"] = k[0],
                ["right_shoulder"] = k[6],
                ["right_elbow"] = k[8],
                ["right_hand"] = k[10],
                ["left_shoulder"] = k[5],
                ["left_elbow"] = k[7],
                ["left_hand"] = k[9],
                ["right_hip"] = k[12],
                ["right_knee"] = k[14],
                ["right_foot"] = k[16],
                ["left_hip"] = k[11],
                ["left_knee"] = k[13],
                ["left_foot"] = k[15],
            };
            var annotation = CharacterAnnotation.FromJointLocations(width, height, joints);
            annotation.Source = "meta-torchserve";
            return annotation;
        }
    }
}
