using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattleBlood
{
    /// <summary>What the player sees over the battle: health, the two armies, a map, the bow's state, kills, and blood on the lens.</summary>
    public class Hud : MonoBehaviour
    {
        Battle b;
        Canvas canvas;
        Image hpFill, hpTrail, blueBar, redBar, crosshair, hitMarker, icon, pain, drawRing, fireIcon, fireFill;
        Text hpText, blueText, redText, msg, typeText, feed, killsText, fireText, hint;
        RawImage map;
        RectTransform mapRect;
        readonly List<Image> dots = new();
        float msgLeft, hitLeft, painLevel, trail, mapTimer;
        readonly List<(string text, float left)> feedLines = new();
        GameObject pausePanel, resultPanel;
        bool arrowFire; float arrowReload;
        bool command;

        /// <summary>Command mode hides what belongs to a man's own eyes: crosshair, draw, controls, blood on the lens.</summary>
        public void SetCommandMode(bool on)
        {
            command = on;
            crosshair.gameObject.SetActive(!on);
            hint.gameObject.SetActive(!on);
            hitMarker.gameObject.SetActive(!on);
            if (on) { drawRing.gameObject.SetActive(false); fireIcon.gameObject.SetActive(false); foreach (var l in lens) l.I.color = new Color(1, 1, 1, 0); }
            else fireIcon.gameObject.SetActive(b.Player != null && b.Player.Type == UnitType.Archer);
        }

        struct Lens { public RawImage I; public float Left, Life, Slide; public Vector2 Start; }
        readonly List<Lens> lens = new();
        int lensNext;

        public void Build(Battle battle)
        {
            b = battle;
            UiKit.EnsureEventSystem();
            canvas = UiKit.MakeCanvas("HUD", 10);
            var root = canvas.transform;

            // Blood on the lens: under everything else.
            var lensTex = Art.Ui("blood_lens");
            for (int i = 0; i < 7; i++)
            {
                var r = UiKit.Rect(root, "lens", new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 900)).gameObject.AddComponent<RawImage>();
                r.texture = lensTex; r.raycastTarget = false; r.color = new Color(1, 1, 1, 0);
                r.rectTransform.pivot = new Vector2(.5f, .5f);
                lens.Add(new Lens { I = r });
            }
            pain = UiKit.Img(root, "pain", new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(0.45f, 0, 0, 0));
            UiKit.Fill(pain.rectTransform);

            // Health, bottom left.
            var ink = new Color(0.04f, 0.035f, 0.03f, 0.74f);
            var panel = UiKit.Img(root, "health panel", new Vector2(0, 0), new Vector2(36, 34), new Vector2(460, 104), ink);
            icon = UiKit.Img(panel.transform, "icon", new Vector2(0, .5f), new Vector2(10, -38), new Vector2(84, 84), Color.white);
            typeText = UiKit.Label(panel.transform, "type", new Vector2(0, 1), new Vector2(104, -36), new Vector2(240, 30), "", 26, TextAnchor.UpperLeft, new Color(0.9f, 0.86f, 0.76f));
            killsText = UiKit.Label(panel.transform, "kills", new Vector2(1, 1), new Vector2(-14, -36), new Vector2(160, 30), "", 22, TextAnchor.UpperRight, new Color(0.85f, 0.42f, 0.32f));
            UiKit.Img(panel.transform, "bar back", new Vector2(0, 0), new Vector2(104, 16), new Vector2(340, 28), new Color(0, 0, 0, 0.75f));
            hpTrail = Bar(panel.transform, new Vector2(107, 19), new Vector2(334, 22), new Color(0.95f, 0.8f, 0.55f, 0.9f));
            hpFill = Bar(panel.transform, new Vector2(107, 19), new Vector2(334, 22), new Color(0.7f, 0.07f, 0.05f));
            hpText = UiKit.Label(panel.transform, "hp", new Vector2(0, 0), new Vector2(274, 16), new Vector2(0, 28), "", 20, TextAnchor.MiddleCenter, Color.white);

            // The armies, top centre.
            var top = UiKit.Img(root, "armies", new Vector2(.5f, 1), new Vector2(0, -22), new Vector2(620, 54), ink);
            top.rectTransform.pivot = new Vector2(.5f, 1);
            blueBar = BarFrom(top.transform, new Vector2(.5f, .5f), new Vector2(-34, -13), new Vector2(240, 26), Unit.TeamColor[0], true);
            redBar = BarFrom(top.transform, new Vector2(.5f, .5f), new Vector2(34, -13), new Vector2(240, 26), Unit.TeamColor[1], false);
            blueText = UiKit.Label(top.transform, "blue", new Vector2(.5f, .5f), new Vector2(-290, -15), new Vector2(0, 30), Battle.ArmySize.ToString(), 28, TextAnchor.MiddleCenter, Color.white);
            redText = UiKit.Label(top.transform, "red", new Vector2(.5f, .5f), new Vector2(290, -15), new Vector2(0, 30), Battle.ArmySize.ToString(), 28, TextAnchor.MiddleCenter, Color.white);

            // Map, top right: a shaded relief of this battle's own land.
            mapRect = UiKit.Rect(root, "map", new Vector2(1, 1), new Vector2(-30, -30), new Vector2(240, 240));
            map = mapRect.gameObject.AddComponent<RawImage>();
            map.texture = ReliefTexture(b.Field);
            map.raycastTarget = false;
            for (int i = 0; i < b.All.Count; i++)
            {
                var d = UiKit.Img(mapRect, "dot", new Vector2(0, 0), Vector2.zero, new Vector2(6, 6), Color.white);
                d.rectTransform.pivot = new Vector2(.5f, .5f);
                dots.Add(d);
            }

            // Crosshair and hit marker.
            crosshair = UiKit.Img(root, "crosshair", new Vector2(.5f, .5f), Vector2.zero, new Vector2(5, 5), new Color(1, 1, 1, 0.75f));
            crosshair.rectTransform.pivot = new Vector2(.5f, .5f);
            hitMarker = UiKit.Img(root, "hit", new Vector2(.5f, .5f), Vector2.zero, new Vector2(26, 26), new Color(1, 0.2f, 0.1f, 0));
            hitMarker.rectTransform.pivot = new Vector2(.5f, .5f);
            hitMarker.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            drawRing = UiKit.Img(root, "draw", new Vector2(.5f, .5f), new Vector2(0, -38), new Vector2(120, 6), new Color(1, 0.9f, 0.7f, 0.85f));
            drawRing.rectTransform.pivot = new Vector2(.5f, .5f);

            // Lit arrows: mode and reload, by the health panel.
            var fp = UiKit.Img(root, "fire panel", new Vector2(0, 0), new Vector2(36, 146), new Vector2(460, 42), ink);
            fireIcon = fp;
            fireFill = Bar(fp.transform, new Vector2(4, 4), new Vector2(452, 34), new Color(0.85f, 0.36f, 0.08f, 0.75f));
            fireText = UiKit.Label(fp.transform, "fire text", new Vector2(.5f, .5f), new Vector2(0, -15), new Vector2(0, 30), "", 22, TextAnchor.MiddleCenter, Color.white);
            fireText.rectTransform.pivot = new Vector2(.5f, .5f);

            msg = UiKit.Label(root, "message", new Vector2(.5f, .72f), Vector2.zero, new Vector2(0, 60), "", 42, TextAnchor.MiddleCenter, new Color(0.95f, 0.88f, 0.72f));
            msg.rectTransform.pivot = new Vector2(.5f, .5f);
            feed = UiKit.Label(root, "feed", new Vector2(1, 0), new Vector2(-34, 34), new Vector2(620, 240), "", 22, TextAnchor.LowerRight, new Color(0.92f, 0.86f, 0.76f));
            hint = UiKit.Label(root, "hint", new Vector2(.5f, 0), new Vector2(0, 28), new Vector2(0, 26), "", 21, TextAnchor.MiddleCenter, new Color(0.9f, 0.86f, 0.76f, 0.82f));
            hint.rectTransform.pivot = new Vector2(.5f, 0);

            BuildPause(root);
            OnBodyChanged(b.Player);
            Counts(b.Alive(0), b.Alive(1));
        }

        static Image Bar(Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var i = UiKit.Img(parent, "fill", new Vector2(0, 0), pos, size, c);
            i.sprite = UiKit.WhiteSprite;
            i.type = Image.Type.Filled; i.fillMethod = Image.FillMethod.Horizontal; i.fillOrigin = 0;
            return i;
        }

        static Image BarFrom(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, bool rightToLeft)
        {
            var back = UiKit.Img(parent, "back", anchor, rightToLeft ? pos - new Vector2(size.x, 0) : pos, size, new Color(0, 0, 0, 0.6f));
            back.rectTransform.pivot = Vector2.zero;
            var i = UiKit.Img(parent, "fill", anchor, rightToLeft ? pos - new Vector2(size.x, 0) : pos, size, c);
            i.rectTransform.pivot = Vector2.zero;
            i.sprite = UiKit.WhiteSprite;
            i.type = Image.Type.Filled; i.fillMethod = Image.FillMethod.Horizontal; i.fillOrigin = rightToLeft ? 1 : 0;
            return i;
        }

        static Texture2D ReliefTexture(Field f)
        {
            const int n = 200;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = (x + 0.5f) / n * 2f * Field.Half - Field.Half, wz = (y + 0.5f) / n * 2f * Field.Half - Field.Half;
                    float h = f.Height(wx, wz);
                    var nr = f.Normal(wx, wz);
                    float light = Mathf.Clamp01(Vector3.Dot(nr, new Vector3(-0.5f, 0.7f, -0.5f).normalized));
                    Color c = h < Field.WaterLevel ? new Color(0.13f, 0.2f, 0.22f) : Color.Lerp(new Color(0.34f, 0.36f, 0.22f), new Color(0.52f, 0.47f, 0.38f), Mathf.InverseLerp(0f, 22f, h));
                    c *= 0.55f + light * 0.75f; c.a = 0.84f;
                    px[x + y * n] = c;
                }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        void BuildPause(Transform root)
        {
            var dim = UiKit.Img(root, "pause", new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.72f));
            UiKit.Fill(dim.rectTransform);
            dim.raycastTarget = true;
            pausePanel = dim.gameObject;
            var c = new Vector2(.5f, .5f);
            var t = UiKit.Label(dim.transform, "title", c, new Vector2(0, 280), Vector2.zero, "PAUSED", 72, TextAnchor.MiddleCenter, new Color(0.9f, 0.85f, 0.72f));
            t.rectTransform.pivot = c;
            var k = UiKit.Label(dim.transform, "keys", c, new Vector2(0, 70), Vector2.zero,
                "WASD move   ·   Shift run   ·   Ctrl walk   ·   Mouse look\n\n" +
                "Infantry: LMB three-cut combo   ·   RMB block   ·   Q shield bash   ·   Space roll\n" +
                "Archer: hold LMB draw, release shoot   ·   RMB aim   ·   F lit arrow (8 s reload)   ·   Space roll\n" +
                "Cavalry: LMB cut   ·   Space charge   ·   ride through men to trample them\n\n" +
                "Tab: command the army from above   ·   LMB select / drag a box   ·   RMB move, or attack an enemy   ·   A then LMB attack-move   ·   H hold   ·   1 2 3 by type", 26, TextAnchor.MiddleCenter, Color.white);
            k.rectTransform.pivot = c;
            var res = UiKit.MakeButton(dim.transform, "Resume", c, new Vector2(0, -140), new Vector2(380, 74), "Resume", 34);
            res.GetComponent<RectTransform>().pivot = c;
            res.onClick.AddListener(TogglePause);
            var menu = UiKit.MakeButton(dim.transform, "Menu", c, new Vector2(0, -230), new Vector2(380, 74), "Quit to menu", 34);
            menu.GetComponent<RectTransform>().pivot = c;
            menu.onClick.AddListener(ToMenu);
            pausePanel.SetActive(false);
        }

        public void TogglePause()
        {
            PlayerControl.Paused = !PlayerControl.Paused;
            pausePanel.SetActive(PlayerControl.Paused);
            Time.timeScale = PlayerControl.Paused ? 0f : 1f;
            Cursor.lockState = PlayerControl.Paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = PlayerControl.Paused;
        }

        static void ToMenu()
        {
            PlayerControl.Paused = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SceneManager.LoadScene(0);
        }

        // ---- events ----

        public void OnBodyChanged(Unit u)
        {
            var tex = Art.Ui("icon_" + u.Type.ToString().ToLowerInvariant());
            if (tex != null) icon.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
            typeText.text = u.S.Label.ToUpperInvariant();
            trail = 1f;
            hint.text = u.Type switch
            {
                UnitType.Infantry => "LMB cut   RMB block   Q bash   Space roll",
                UnitType.Archer => "Hold LMB draw   RMB aim   F lit arrow   Space roll",
                _ => "LMB cut   Space charge   ride through men to trample them",
            };
            fireIcon.gameObject.SetActive(u.Type == UnitType.Archer);
        }

        public void Counts(int blue, int red)
        {
            blueBar.fillAmount = blue / (float)Battle.ArmySize; redBar.fillAmount = red / (float)Battle.ArmySize;
            blueText.text = blue.ToString(); redText.text = red.ToString();
        }

        Image barTop, barBottom; float letterLeft;
        /// <summary>Black bars top and bottom for a cinematic moment.</summary>
        public void Letterbox(float seconds)
        {
            if (barTop == null)
            {
                var root = msg.canvas.transform;
                barTop = UiKit.Img(root, "bar top", new Vector2(.5f, 1), Vector2.zero, new Vector2(4000, 0), Color.black);
                barTop.rectTransform.pivot = new Vector2(.5f, 1);
                barBottom = UiKit.Img(root, "bar bottom", new Vector2(.5f, 0), Vector2.zero, new Vector2(4000, 0), Color.black);
                barBottom.rectTransform.pivot = new Vector2(.5f, 0);
            }
            letterLeft = seconds;
        }

        void LateUpdate()
        {
            if (barTop == null) return;
            letterLeft -= Time.unscaledDeltaTime;
            float h = barTop.rectTransform.sizeDelta.y;
            h = Mathf.MoveTowards(h, letterLeft > 0f ? 110f : 0f, Time.unscaledDeltaTime * 600f);
            barTop.rectTransform.sizeDelta = barBottom.rectTransform.sizeDelta = new Vector2(4000, h);
        }

        public void Message(string text, float seconds) { msg.text = text; msgLeft = seconds; }

        public void KillFeed(Unit killer, Unit victim, HitKind kind)
        {
            string Name(Unit u) => u == null ? "the field" : u.IsPlayer ? "You" : $"{(u.Team == Unit.Blue ? "Blue" : "Red")} {u.S.Label.ToLowerInvariant()}";
            string how = kind switch
            {
                HitKind.FireArrow or HitKind.Burn => "burned",
                HitKind.Arrow => "shot",
                HitKind.Trample => "trampled",
                HitKind.Bleed => "bled",
                _ => "cut down",
            };
            feedLines.Add(($"{Name(killer)} {how} {(victim.IsPlayer ? "you" : Name(victim))}", 6f));
            if (feedLines.Count > 6) feedLines.RemoveAt(0);
        }

        public void HitMarker(bool alive) { hitLeft = alive ? 0.14f : 0.3f; hitMarker.rectTransform.sizeDelta = Vector2.one * (alive ? 22 : 34); }
        public void OnPlayerHurt(float share) { painLevel = Mathf.Clamp01(painLevel + share * 2f + 0.1f); PostFx.Instance?.Pain(share); if (share > 0.05f) { LensSplat(share * 3f); b.PlayerCam.Shake(0.15f + share); } }
        public void SetArrowMode(bool fire, float reload) { arrowFire = fire; arrowReload = reload; }

        /// <summary>Blood hits the lens and runs down it.</summary>
        public void LensSplat(float strength)
        {
            if (strength < 0.1f || command) return;
            int count = strength > 0.8f ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                var l = lens[lensNext];
                l.Life = l.Left = Mathf.Lerp(1.6f, 4.6f, strength);
                l.Slide = Random.Range(10f, 34f);
                // Towards the edges of the picture: the middle is where the player is looking.
                float side = Random.value < 0.5f ? -1f : 1f;
                l.Start = new Vector2(side * Random.Range(330f, 860f), Random.Range(-420f, 420f));
                l.I.rectTransform.anchoredPosition = l.Start;
                l.I.rectTransform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                l.I.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.14f, 0.46f, strength) * Random.Range(0.8f, 1.2f);
                lens[lensNext] = l;
                lensNext = (lensNext + 1) % lens.Count;
            }
        }

        public void ShowResult(bool won)
        {
            StartCoroutine(Result(won));
        }

        System.Collections.IEnumerator Result(bool won)
        {
            float t = 0f;
            while (t < 2.6f) { t += Time.unscaledDeltaTime; yield return null; }
            var dim = UiKit.Img(canvas.transform, "result", new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(0.03f, 0, 0, 0));
            UiKit.Fill(dim.rectTransform);
            dim.raycastTarget = true;
            resultPanel = dim.gameObject;
            var c = new Vector2(.5f, .5f);
            var title = UiKit.Label(dim.transform, "title", c, new Vector2(0, 170), Vector2.zero, won ? "VICTORY" : "DEFEAT", 120, TextAnchor.MiddleCenter, won ? new Color(0.5f, 0.68f, 1f) : new Color(0.9f, 0.14f, 0.1f));
            title.rectTransform.pivot = c;
            int blue = b.Alive(0), red = b.Alive(1);
            var sum = UiKit.Label(dim.transform, "summary", c, new Vector2(0, 40), Vector2.zero,
                $"Survivors: {blue} blue, {red} red\nYour kills: {b.PlayerKills}   ·   Lives spent: {b.PlayerLives}   ·   Limbs severed: {Gore.SeveredCount}", 32, TextAnchor.MiddleCenter, Color.white);
            sum.rectTransform.pivot = c;
            var again = UiKit.MakeButton(dim.transform, "Again", c, new Vector2(0, -90), new Vector2(420, 78), "Another battle", 36);
            again.GetComponent<RectTransform>().pivot = c;
            again.onClick.AddListener(() => { Boot.StartDirectly = true; ToMenu(); });
            var menu = UiKit.MakeButton(dim.transform, "Menu", c, new Vector2(0, -186), new Vector2(420, 78), "Main menu", 36);
            menu.GetComponent<RectTransform>().pivot = c;
            menu.onClick.AddListener(ToMenu);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime; dim.color = new Color(0.03f, 0, 0, t * 0.68f); yield return null; }
        }

        void Update()
        {
            if (b == null || !b.Ready) return;
            float dt = Time.unscaledDeltaTime;
            var u = b.Player;
            if (u != null)
            {
                float hp = Mathf.Clamp01(u.Hp / u.S.MaxHp);
                hpFill.fillAmount = hp;
                trail = Mathf.MoveTowards(trail, hp, dt * 0.35f);
                if (trail < hp) trail = hp;
                hpTrail.fillAmount = trail;
                hpText.text = Mathf.CeilToInt(u.Hp) + (u.Burning ? "   BURNING" : u.BleedLeft > 0 ? "   BLEEDING" : "");
                hpText.color = u.Burning ? new Color(1f, 0.72f, 0.3f) : Color.white;
                killsText.text = b.PlayerKills + " kills";

                bool archer = u.Type == UnitType.Archer;
                drawRing.gameObject.SetActive(!command && archer && u.Action == Act.Draw);
                if (archer && u.Action == Act.Draw) drawRing.rectTransform.sizeDelta = new Vector2(120f * u.DrawPower, u.DrawPower >= 1f ? 8f : 5f);
                if (archer)
                {
                    float left = Mathf.Max(0f, u.FireReload);
                    fireFill.fillAmount = 1f - left / Unit.FireReloadTime;
                    fireText.text = left > 0f ? $"Lit arrow in {left:F0}s" : arrowFire ? "LIT ARROW READY  -  draw to light" : "Lit arrow ready  -  F to select";
                    fireFill.color = arrowFire && left <= 0f ? new Color(1f, 0.46f, 0.08f, 0.95f) : new Color(0.72f, 0.3f, 0.08f, 0.68f);
                }
            }

            msgLeft -= dt;
            msg.color = new Color(msg.color.r, msg.color.g, msg.color.b, Mathf.Clamp01(msgLeft));
            hitLeft -= dt;
            hitMarker.color = new Color(1, 0.18f, 0.08f, Mathf.Clamp01(hitLeft * 8f));
            painLevel = Mathf.MoveTowards(painLevel, 0f, dt * 0.9f);
            pain.color = new Color(0.42f, 0, 0, painLevel * 0.28f);

            for (int i = 0; i < lens.Count; i++)
            {
                var l = lens[i];
                if (l.Left <= 0f) continue;
                l.Left -= dt;
                float a = Mathf.Clamp01(l.Left / (l.Life * 0.55f));
                l.I.color = new Color(1, 1, 1, a * 0.72f);
                l.Start.y -= l.Slide * dt;
                l.I.rectTransform.anchoredPosition = l.Start;
                lens[i] = l;
            }

            for (int i = feedLines.Count - 1; i >= 0; i--)
            {
                var (text, left) = feedLines[i];
                left -= dt;
                if (left <= 0f) feedLines.RemoveAt(i); else feedLines[i] = (text, left);
            }
            System.Text.StringBuilder sb = new();
            foreach (var (text, _) in feedLines) sb.AppendLine(text);
            feed.text = sb.ToString();

            mapTimer -= dt;
            if (mapTimer <= 0f)
            {
                mapTimer = 0.2f;
                float k = 240f / (2f * Field.Half);
                for (int i = 0; i < b.All.Count; i++)
                {
                    var unit = b.All[i];
                    var d = dots[i];
                    bool show = unit.Alive;
                    if (d.gameObject.activeSelf != show) d.gameObject.SetActive(show);
                    if (!show) continue;
                    d.rectTransform.anchoredPosition = new Vector2((unit.Pos.x + Field.Half) * k, (unit.Pos.z + Field.Half) * k);
                    d.color = unit.IsPlayer ? Color.white : unit.Selected ? new Color(0.6f, 1f, 0.55f) : unit.Team == Unit.Blue ? new Color(0.35f, 0.55f, 1f) : new Color(1f, 0.25f, 0.2f);
                    d.rectTransform.sizeDelta = Vector2.one * (unit.IsPlayer ? 10 : unit.Type == UnitType.Cavalry ? 4 : 3);
                    if (unit.IsPlayer) d.transform.SetAsLastSibling();
                }
            }
        }
    }

    /// <summary>Small helpers to build uGUI from code, used by the menu and the HUD.</summary>
    public static class UiKit
    {
        static Font font;
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static Sprite white;
        public static Sprite WhiteSprite => white != null ? white : white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));

        public static Canvas MakeCanvas(string name, int order = 0)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(anchor.x == 1f ? 1f : anchor.x == 0f ? 0f : 0.5f, anchor.y == 1f ? 1f : anchor.y == 0f ? 0f : 0f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static void Fill(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        public static Image Img(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var i = Rect(parent, name, anchor, pos, size).gameObject.AddComponent<Image>();
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fontSize, TextAnchor align, Color color)
        {
            var t = Rect(parent, name, anchor, pos, size).gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = fontSize; t.alignment = align; t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = t.gameObject.AddComponent<Shadow>();
            o.effectColor = new Color(0, 0, 0, 0.8f);
            o.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Button MakeButton(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fontSize)
        {
            var r = Rect(parent, name, anchor, pos, size);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0.07f, 0.05f, 0.04f, 0.86f);
            var b = r.gameObject.AddComponent<Button>();
            var col = b.colors;
            col.normalColor = Color.white;
            col.highlightedColor = new Color(0.85f, 0.2f, 0.12f, 1f);
            col.selectedColor = new Color(0.85f, 0.2f, 0.12f, 1f);
            col.pressedColor = new Color(0.55f, 0.08f, 0.04f, 1f);
            col.fadeDuration = 0.08f;
            b.colors = col;
            var t = Label(r, "Text", new Vector2(.5f, .5f), Vector2.zero, size, text, fontSize, TextAnchor.MiddleCenter, new Color(0.94f, 0.88f, 0.76f));
            t.rectTransform.pivot = new Vector2(.5f, .5f);
            var outline = r.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.1f, 0.06f, 0.9f);
            outline.effectDistance = new Vector2(2, -2);
            return b;
        }
    }
}
