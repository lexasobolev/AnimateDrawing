using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AnimatedDrawingsWorld.Logic.Imaging;

namespace AnimatedDrawingsWorld.Logic.Characters
{
    [Serializable]
    public struct SkeletonJoint
    {
        public string Name;
        public string Parent; // null for root
        public V2 Location;   // pixel coordinates in the cropped texture, y DOWN (Meta convention)

        public SkeletonJoint(string name, string parent, V2 location)
        {
            Name = name;
            Parent = parent;
            Location = location;
        }
    }

    // In-memory equivalent of the files Meta's image_to_annotations.py writes:
    // texture.png (cropped drawing), mask.png (segmentation) and char_cfg.yaml (skeleton).
    public sealed class CharacterAnnotation
    {
        public static readonly string[] JointNames =
        {
            "root", "hip", "torso", "neck",
            "right_shoulder", "right_elbow", "right_hand",
            "left_shoulder", "left_elbow", "left_hand",
            "right_hip", "right_knee", "right_foot",
            "left_hip", "left_knee", "left_foot",
        };

        public static readonly Dictionary<string, string> ParentOf = new()
        {
            ["root"] = null,
            ["hip"] = "root",
            ["torso"] = "hip",
            ["neck"] = "torso",
            ["right_shoulder"] = "torso",
            ["right_elbow"] = "right_shoulder",
            ["right_hand"] = "right_elbow",
            ["left_shoulder"] = "torso",
            ["left_elbow"] = "left_shoulder",
            ["left_hand"] = "left_elbow",
            ["right_hip"] = "root",
            ["right_knee"] = "right_hip",
            ["right_foot"] = "right_knee",
            ["left_hip"] = "root",
            ["left_knee"] = "left_hip",
            ["left_foot"] = "left_knee",
        };

        public int Width;
        public int Height;
        public List<SkeletonJoint> Skeleton = new();
        public RgbaImage Texture; // cropped drawing, alpha = mask
        public GrayImage Mask;    // 255 inside the character
        public string Source = "unknown"; // "meta-torchserve", "heuristic", "file"

        public bool TryGetJoint(string name, out SkeletonJoint joint)
        {
            foreach (var j in Skeleton)
            {
                if (j.Name != name) continue;
                joint = j;
                return true;
            }
            joint = default;
            return false;
        }

        public V2 Joint(string name) => TryGetJoint(name, out var j) ? j.Location : throw new KeyNotFoundException(name);

        public static CharacterAnnotation FromJointLocations(int width, int height, IDictionary<string, V2> locations)
        {
            var result = new CharacterAnnotation { Width = width, Height = height };
            foreach (var name in JointNames)
            {
                var p = locations[name];
                p = new V2(MathUtil.Clamp(p.X, 0, width - 1), MathUtil.Clamp(p.Y, 0, height - 1));
                result.Skeleton.Add(new SkeletonJoint(name, ParentOf[name], p));
            }
            return result;
        }

        // Writes char_cfg.yaml in the same layout Meta's pipeline produces, so a heuristic or
        // runtime annotation can be fed back to Meta's own renderer as well.
        public string ToCharCfgYaml()
        {
            var sb = new StringBuilder();
            sb.Append("height: ").Append(Height).Append('\n');
            sb.Append("skeleton:\n");
            foreach (var j in Skeleton)
            {
                sb.Append("- loc:\n");
                sb.Append("  - ").Append(((int)MathF.Round(j.Location.X)).ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("  - ").Append(((int)MathF.Round(j.Location.Y)).ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("  name: ").Append(j.Name).Append('\n');
                sb.Append("  parent: ").Append(j.Parent ?? "null").Append('\n');
            }
            sb.Append("width: ").Append(Width).Append('\n');
            return sb.ToString();
        }

        // Parses Meta's char_cfg.yaml (the fixed structure yaml.dump emits; not general YAML).
        public static CharacterAnnotation ParseCharCfgYaml(string yaml)
        {
            var result = new CharacterAnnotation();
            SkeletonJoint? current = null;
            var locValues = new List<float>();
            var inLoc = false;

            void Flush()
            {
                if (current == null) return;
                var joint = current.Value;
                if (locValues.Count >= 2) joint.Location = new V2(locValues[0], locValues[1]);
                result.Skeleton.Add(joint);
                current = null;
                locValues.Clear();
            }

            foreach (var rawLine in yaml.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0) continue;
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;

                if (indent == 0 && !trimmed.StartsWith("-"))
                {
                    Flush();
                    inLoc = false;
                    var (key, value) = SplitKey(trimmed);
                    if (key == "width") result.Width = (int)ParseFloat(value);
                    else if (key == "height") result.Height = (int)ParseFloat(value);
                    continue;
                }

                if (trimmed.StartsWith("- ") && indent == 0)
                {
                    // new joint entry: "- loc:" or "- name: x"
                    Flush();
                    current = new SkeletonJoint();
                    trimmed = trimmed.Substring(2).TrimStart();
                    indent = 2;
                }

                if (current == null) continue;
                if (trimmed.StartsWith("- ") && inLoc)
                {
                    locValues.Add(ParseFloat(trimmed.Substring(2)));
                    continue;
                }

                var (k, v) = SplitKey(trimmed);
                var joint = current.Value;
                switch (k)
                {
                    case "loc":
                        inLoc = true;
                        // inline flow style: loc: [x, y]
                        if (v.StartsWith("["))
                        {
                            foreach (var part in v.Trim('[', ']').Split(','))
                                locValues.Add(ParseFloat(part));
                            inLoc = false;
                        }
                        break;
                    case "name":
                        joint.Name = v;
                        inLoc = false;
                        break;
                    case "parent":
                        joint.Parent = v == "null" || v == "~" || v.Length == 0 ? null : v;
                        inLoc = false;
                        break;
                }
                current = joint;
            }
            Flush();
            return result;
        }

        private static (string key, string value) SplitKey(string s)
        {
            var colon = s.IndexOf(':');
            if (colon < 0) return (s.Trim(), "");
            return (s.Substring(0, colon).Trim(), s.Substring(colon + 1).Trim().Trim('\'', '"'));
        }

        private static float ParseFloat(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
