using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace BattleBlood
{
    /// <summary>The one object in the scene. Shows the main menu over the generated video; Start builds a battle from a fresh random seed.</summary>
    public class Boot : MonoBehaviour
    {
        public static bool StartDirectly;
        public static int ForcedSeed = -1;
        Canvas canvas;
        GameObject menuRoot, menuCam;
        VideoPlayer video;
        RenderTexture videoTarget;
        Text loadText; GameObject loadRoot;
        bool started;

        void Awake()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            Art.ClearCache();
            GameAudio.Create();
            UiKit.EnsureEventSystem();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BuildMenu();
            if (StartDirectly) { StartDirectly = false; StartBattle(); }
        }

        void BuildMenu()
        {
            var camGo = new GameObject("Menu camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            menuCam = camGo;

            canvas = UiKit.MakeCanvas("Menu", 20);
            var c = new Vector2(.5f, .5f);
            menuRoot = new GameObject("root", typeof(RectTransform));
            menuRoot.transform.SetParent(canvas.transform, false);
            UiKit.Fill(menuRoot.GetComponent<RectTransform>());

            // The background: the generated video, or its still when no video is there.
            var bgRect = UiKit.Rect(menuRoot.transform, "background", c, Vector2.zero, Vector2.zero);
            UiKit.Fill(bgRect);
            var bg = bgRect.gameObject.AddComponent<RawImage>();
            bg.raycastTarget = false;
            bg.texture = Art.Ui("menu_key");
            var clip = Resources.Load<VideoClip>("Art/Video/menu");
            if (clip != null)
            {
                videoTarget = new RenderTexture(1280, 704, 0);
                video = gameObject.AddComponent<VideoPlayer>();
                video.clip = clip; video.isLooping = true; video.audioOutputMode = VideoAudioOutputMode.None;
                video.renderMode = VideoRenderMode.RenderTexture; video.targetTexture = videoTarget;
                video.prepareCompleted += _ => { bg.texture = videoTarget; video.Play(); };
                video.Prepare();
            }
            var veil = UiKit.Img(menuRoot.transform, "veil", c, Vector2.zero, Vector2.zero, new Color(0.03f, 0, 0, 0.32f));
            UiKit.Fill(veil.rectTransform);

            var logoTex = Art.Ui("logo");
            if (logoTex != null)
            {
                var logo = UiKit.Rect(menuRoot.transform, "Title", new Vector2(.5f, .69f), Vector2.zero, new Vector2(1150, 1150f * logoTex.height / logoTex.width)).gameObject.AddComponent<RawImage>();
                logo.rectTransform.pivot = c;
                logo.texture = logoTex; logo.raycastTarget = false;
            }
            else
            {
                var t = UiKit.Label(menuRoot.transform, "Title", new Vector2(.5f, .69f), Vector2.zero, Vector2.zero, "BATTLE BLOOD", 120, TextAnchor.MiddleCenter, new Color(0.8f, 0.1f, 0.06f));
                t.rectTransform.pivot = c;
            }

            var play = UiKit.MakeButton(menuRoot.transform, "Play", new Vector2(.5f, .36f), Vector2.zero, new Vector2(440, 88), "Start Battle", 42);
            play.GetComponent<RectTransform>().pivot = c;
            play.onClick.AddListener(() => { Battle.PracticeMode = null; StartBattle(); });
            play.Select();
            var practice = UiKit.MakeButton(menuRoot.transform, "Practice", new Vector2(.5f, .36f), new Vector2(0, -98), new Vector2(440, 70), "Practice", 32);
            practice.GetComponent<RectTransform>().pivot = c;
            practice.onClick.AddListener(() => { GameAudio.Play2D("ui_click", 0.8f); practiceRoot.SetActive(true); menuButtons.SetActive(false); });
            BuildPracticePanel(c);
            var quit = UiKit.MakeButton(menuRoot.transform, "Quit", new Vector2(.5f, .36f), new Vector2(0, -184), new Vector2(440, 70), "Quit", 32);
            quit.GetComponent<RectTransform>().pivot = c;
            quit.onClick.AddListener(Application.Quit);
            menuButtons = new GameObject("buttons", typeof(RectTransform));
            menuButtons.transform.SetParent(menuRoot.transform, false);
            UiKit.Fill(menuButtons.GetComponent<RectTransform>());
            foreach (var btn in new[] { play.transform, practice.transform, quit.transform }) btn.SetParent(menuButtons.transform, true);

            var keys = UiKit.Label(menuRoot.transform, "keys", new Vector2(.5f, 0), new Vector2(0, 26), Vector2.zero,
                "Four hundred against four hundred on new ground every battle. You are one soldier of the blue army: fall, and you fight on as another.", 24, TextAnchor.MiddleCenter, new Color(0.92f, 0.87f, 0.76f));
            keys.rectTransform.pivot = new Vector2(.5f, 0);

            // Loading, hidden until needed.
            loadRoot = new GameObject("loading", typeof(RectTransform));
            loadRoot.transform.SetParent(canvas.transform, false);
            UiKit.Fill(loadRoot.GetComponent<RectTransform>());
            var dark = UiKit.Img(loadRoot.transform, "dark", c, Vector2.zero, Vector2.zero, new Color(0.02f, 0.005f, 0.005f, 0.86f));
            UiKit.Fill(dark.rectTransform);
            // A spinning ring in the middle of the screen's width, with what is being made written under it.
            var spin = new GameObject("spinner", typeof(RectTransform), typeof(Image), typeof(Spinner));
            spin.transform.SetParent(loadRoot.transform, false);
            var srt = spin.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(.5f, .26f); srt.pivot = new Vector2(.5f, .5f);
            srt.anchoredPosition = Vector2.zero; srt.sizeDelta = new Vector2(72, 72);
            var ring = spin.GetComponent<Image>();
            ring.sprite = Spinner.Ring(); ring.color = new Color(0.72f, 0.06f, 0.04f); ring.raycastTarget = false;
            loadText = UiKit.Label(loadRoot.transform, "text", new Vector2(.5f, .26f), new Vector2(0, -70), Vector2.zero, "", 30, TextAnchor.MiddleCenter, new Color(0.92f, 0.87f, 0.76f));
            loadText.rectTransform.pivot = new Vector2(.5f, .5f);
            loadRoot.SetActive(false);

            GameAudio.Instance.PlayMusic("music_menu", 0.62f);
        }

        GameObject practiceRoot, menuButtons;
        readonly PracticeSetup setup = new();

        /// <summary>Pick your soldier and the opponent, then Begin.</summary>
        void BuildPracticePanel(Vector2 c)
        {
            practiceRoot = new GameObject("practice", typeof(RectTransform));
            practiceRoot.transform.SetParent(menuRoot.transform, false);
            UiKit.Fill(practiceRoot.GetComponent<RectTransform>());
            var cream = new Color(0.92f, 0.87f, 0.76f);
            var rows = new[] { ("Your soldier", 0), ("Opponent", 1) };
            foreach (var (title, row) in rows)
            {
                float y = row == 0 ? 0f : -150f;
                var l = UiKit.Label(practiceRoot.transform, title, new Vector2(.5f, .4f), new Vector2(0, y + 58), new Vector2(600, 40), title, 30, TextAnchor.MiddleCenter, cream);
                l.rectTransform.pivot = c;
                var buttons = new Button[3];
                for (int i = 0; i < 3; i++)
                {
                    var type = (UnitType)i;
                    var bt = UiKit.MakeButton(practiceRoot.transform, title + type, new Vector2(.5f, .4f), new Vector2((i - 1) * 250f, y), new Vector2(230, 70), type.ToString(), 30);
                    bt.GetComponent<RectTransform>().pivot = c;
                    buttons[i] = bt;
                    int r = row;
                    bt.onClick.AddListener(() => { if (r == 0) setup.Mine = type; else setup.Theirs = type; Mark(buttons, (int)type); GameAudio.Play2D("ui_click", 0.6f); });
                }
                Mark(buttons, 0);
            }
            var begin = UiKit.MakeButton(practiceRoot.transform, "Begin", new Vector2(.5f, .4f), new Vector2(-125, -290), new Vector2(230, 76), "Begin", 34);
            begin.GetComponent<RectTransform>().pivot = c;
            begin.onClick.AddListener(() => { Battle.PracticeMode = setup; StartBattle(); });
            var back = UiKit.MakeButton(practiceRoot.transform, "Back", new Vector2(.5f, .4f), new Vector2(125, -290), new Vector2(230, 76), "Back", 34);
            back.GetComponent<RectTransform>().pivot = c;
            back.onClick.AddListener(() => { practiceRoot.SetActive(false); menuButtons.SetActive(true); });
            practiceRoot.SetActive(false);
        }

        static void Mark(Button[] buttons, int chosen)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].GetComponent<Image>();
                if (img != null) img.color = i == chosen ? new Color(0.75f, 0.12f, 0.08f, 0.95f) : new Color(1f, 1f, 1f, 0.55f);
            }
        }

        public void StartBattle()
        {
            if (started) return;
            started = true;
            GameAudio.Play2D("ui_click", 0.8f);
            // The menu's buttons (and the practice panel) go, so the spinner and its words stand alone.
            if (menuButtons != null) menuButtons.SetActive(false);
            if (practiceRoot != null) practiceRoot.SetActive(false);
            loadRoot.SetActive(true);
            Battle.OnProgress = (p, what) => { loadText.text = what; };
            StartCoroutine(Run());
        }

        System.Collections.IEnumerator Run()
        {
            yield return null;
            int seed = ForcedSeed >= 0 ? ForcedSeed : new System.Random().Next(1, int.MaxValue / 64);
            if (Battle.PracticeMode != null) Debug.Log($"[BOOT] practice: {Battle.PracticeMode.Mine} against {Battle.PracticeMode.Theirs}");
            var battle = Battle.Begin(seed);
            while (!battle.Ready) yield return null;
            if (video != null) { video.Stop(); Destroy(video); }
            if (videoTarget != null) videoTarget.Release();
            Destroy(menuCam);
            Destroy(canvas.gameObject);
            Battle.OnProgress = null;
        }
    }
}
