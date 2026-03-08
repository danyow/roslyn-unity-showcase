using UnityEngine;
using UnityEngine.SceneManagement;

namespace Showcase
{
    public class MainMenu : MonoBehaviour
    {
        private Vector2 _scroll;

        private readonly string[] _levels =
        {
            "Level0 - Logger",
            "Level1 - AutoProperty",
            "Level2 - AutoImplement",
            "Level3 - MathType",
            "Level4 - AutoService",
            "Level5 - EventInterface",
            "Level6 - EventExecute",
            "Level7 - ModuleMesh",
        };

        private readonly string[] _chapters =
        {
            "Chapter1 - Prefix/Postfix",
            "Chapter2 - Transpiler",
            "Chapter3 - Finalizer",
            "Chapter4 - AutoPatch",
            "Chapter5 - DebugLog Color",
        };

        private void OnGUI()
        {
            var w = Screen.width;
            var h = Screen.height;
            var col = w / 2f;
            var btnH = 48f;
            var pad = 12f;

            GUI.skin.button.fontSize = 20;
            GUI.skin.label.fontSize = 24;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;

            // Title
            GUI.Label(new Rect(0, 20, w, 40), "Roslyn & Harmony Showcase");

            _scroll = GUI.BeginScrollView(new Rect(0, 80, w, h - 100), _scroll, new Rect(0, 0, w, 600));

            // Left column - Roslyn
            GUI.Label(new Rect(pad, 0, col - pad * 2, 36), "Roslyn Source Generators");
            for (int i = 0; i < _levels.Length; i++)
            {
                var rect = new Rect(pad, 40 + i * (btnH + pad), col - pad * 2, btnH);
                if (GUI.Button(rect, _levels[i]))
                    SceneManager.LoadScene("Level" + i);
            }

            // Right column - Harmony
            GUI.Label(new Rect(col + pad, 0, col - pad * 2, 36), "Harmony Patching");
            for (int i = 0; i < _chapters.Length; i++)
            {
                var rect = new Rect(col + pad, 40 + i * (btnH + pad), col - pad * 2, btnH);
                if (GUI.Button(rect, _chapters[i]))
                    SceneManager.LoadScene("Chapter" + (i + 1));
            }

            GUI.EndScrollView();
        }
    }
}
