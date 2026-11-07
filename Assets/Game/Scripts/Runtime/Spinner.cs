using UnityEngine;

namespace BattleBlood
{
    /// <summary>The loading spinner: a three-quarter ring, fading round its length, turning at a steady pace (real time, so slow motion cannot stop it).</summary>
    public class Spinner : MonoBehaviour
    {
        public float DegreesPerSecond = 300f;
        void Update() => transform.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime);

        static Sprite ring;
        public static Sprite Ring()
        {
            if (ring != null) return ring;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, outer = n * 0.48f, inner = n * 0.36f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01(outer - r) * Mathf.Clamp01(r - inner);          // soft edges, a pixel wide
                    float a = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;                 // 0..1 round the ring
                    float tail = a < 0.25f ? 0f : (a - 0.25f) / 0.75f;                           // a gap, then fading in to the head
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * edge * tail));
                }
            tex.SetPixels32(px); tex.Apply();
            ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
            return ring;
        }
    }
}
