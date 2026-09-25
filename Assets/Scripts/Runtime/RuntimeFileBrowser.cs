using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Minimal in-game "open image" window (Unity has no native file dialog in player builds).
    public sealed class RuntimeFileBrowser
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        public bool Visible { get; private set; }
        public string Title = "Open drawing";
        private Action<string> onPicked;
        private string directory;
        private string pathField = "";
        private Vector2 scroll;
        private string[] directories = Array.Empty<string>();
        private string[] files = Array.Empty<string>();
        private string error;
        private Rect window = new(0, 0, 560, 440);

        private static string lastDirectory;

        public void Open(string title, Action<string> picked)
        {
            Title = title;
            onPicked = picked;
            Visible = true;
            Navigate(lastDirectory ?? DefaultDirectory());
            window.x = (Screen.width - window.width) * 0.5f;
            window.y = (Screen.height - window.height) * 0.5f;
        }

        public Rect Rect => window;

        private static string DefaultDirectory()
        {
            foreach (var folder in new[] { Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.UserProfile })
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
            }
            return Application.persistentDataPath;
        }

        private void Navigate(string path)
        {
            try
            {
                directory = Path.GetFullPath(path);
                directories = Directory.GetDirectories(directory).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => d).ToArray();
                files = Directory.GetFiles(directory).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray();
                pathField = directory;
                lastDirectory = directory;
                error = null;
                scroll = Vector2.zero;
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        }

        public void OnGUI()
        {
            if (!Visible) return;
            window = GUI.ModalWindow(0x5eed, window, Draw, Title);
        }

        private void Draw(int id)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Up", GUILayout.Width(50)))
            {
                var parent = Directory.GetParent(directory);
                if (parent != null) Navigate(parent.FullName);
            }
            pathField = GUILayout.TextField(pathField);
            if (GUILayout.Button("Go", GUILayout.Width(40)))
            {
                if (File.Exists(pathField)) Pick(pathField);
                else Navigate(pathField);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            void Shortcut(string label, Environment.SpecialFolder folder)
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && GUILayout.Button(label)) Navigate(p);
            }
            Shortcut("Pictures", Environment.SpecialFolder.MyPictures);
            Shortcut("Desktop", Environment.SpecialFolder.Desktop);
            Shortcut("Documents", Environment.SpecialFolder.MyDocuments);
            Shortcut("Home", Environment.SpecialFolder.UserProfile);
            if (GUILayout.Button("Samples")) Navigate(Path.Combine(Application.streamingAssetsPath, "Samples"));
            GUILayout.EndHorizontal();

            if (error != null) GUILayout.Label("<color=red>" + error + "</color>");

            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var d in directories)
                if (GUILayout.Button("[folder] " + Path.GetFileName(d), GUI.skin.label)) Navigate(d);
            foreach (var f in files)
                if (GUILayout.Button(Path.GetFileName(f))) Pick(f);
            if (directories.Length == 0 && files.Length == 0) GUILayout.Label("(no PNG/JPG images here)");
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel")) Visible = false;
            GUI.DragWindow();
        }

        private void Pick(string file)
        {
            Visible = false;
            onPicked?.Invoke(file);
        }
    }
}
