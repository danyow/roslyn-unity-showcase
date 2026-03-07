using UnityEngine;
using UnityEngine.SceneManagement;

namespace Showcase
{
    public class BackButton : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.skin.button.fontSize = 18;
            if (GUI.Button(new Rect(12, 12, 200, 40), "\u2190 \u8fd4\u56de\u4e3b\u83dc\u5355"))
                SceneManager.LoadScene("Main");
        }
    }
}
