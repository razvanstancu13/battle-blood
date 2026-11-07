using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattleBlood
{
    /// <summary>
    /// Command mode: Tab switches between leading one soldier and commanding the blue army from above.
    /// Select with clicks and boxes, then move, attack, attack-move or hold. Soldiers without orders keep fighting on their own.
    /// </summary>
    public class Rts : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static Rts I { get; private set; }

        Battle b;
        Camera cam;
        PlayerCam actionCam;
        Vector3 pivot;                  // the point on the ground the camera looks at
        float yaw, height = 60f, pitch = 58f;
        readonly List<Unit> selected = new();
        public IReadOnlyList<Unit> Selected => selected;
        public int OrdersGiven { get; private set; }

        // Input state.
        Vector2 dragStart; bool dragging, attackMove;
        float lastClick; Unit lastClicked;
        RectTransform box;
        Button startButton;
        Text panel, modeLabel;
        GameObject uiRoot;

        // Markers.
        Mesh quad; Material ringMat;
        readonly Matrix4x4[] mats = new Matrix4x4[1023];
        readonly Vector4[] tints = new Vector4[1023];
        readonly float[] fills = new float[1023];
        MaterialPropertyBlock block;
        Vector3 markerPos; float markerLeft; Color markerColor;

        public void Init(Battle battle, Camera camera, PlayerCam action)
        {
            I = this; b = battle; cam = camera; actionCam = action;
            quad = MakeQuad();
            ringMat = new Material(Art.Shader("BBRing")) { name = "m_ring", enableInstancing = true };
            ringMat.SetFloat("_Width", 0.24f);
            block = new MaterialPropertyBlock();
            BuildUi();
            Active = false;
        }

        /// <summary>The battle opens here: command mode over the blue army, the Start button showing.</summary>
        public void Open()
        {
            Enter();
            if (b.Player != null)
            {
                Vector3 c = Vector3.zero; int n = 0;
                foreach (var u in b.Army(Unit.Blue)) { c += u.Pos; n++; }
                if (n > 0) { c /= n; pivot = new Vector3(c.x, 0, c.z + 10f); yaw = 0f; height = 75f; }
            }
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "ring" };
            m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f) };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.5f, 1));
            return m;
        }

        void BuildUi()
        {
            var canvas = UiKit.MakeCanvas("Command", 12);
            uiRoot = canvas.gameObject;
            box = UiKit.Img(canvas.transform, "select box", Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.75f, 0.85f, 1f, 0.16f)).rectTransform;
            box.pivot = Vector2.zero;
            var o = box.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.75f, 0.85f, 1f, 0.8f); o.effectDistance = new Vector2(1, -1);
            box.gameObject.SetActive(false);
            var bar = UiKit.Img(canvas.transform, "command bar", new Vector2(.5f, 0), new Vector2(225, 22), new Vector2(1370, 92), new Color(0.04f, 0.035f, 0.03f, 0.8f));
            bar.rectTransform.pivot = new Vector2(.5f, 0);
            panel = UiKit.Label(bar.transform, "selection", new Vector2(0, 1), new Vector2(18, -14), new Vector2(0, 30), "", 26, TextAnchor.UpperLeft, new Color(0.94f, 0.88f, 0.76f));
            UiKit.Label(bar.transform, "keys", new Vector2(0, 0), new Vector2(18, 12), new Vector2(0, 24),
                "LMB select / drag box  ·  double-click: all of a type  ·  1 2 3 infantry archers cavalry  ·  Ctrl+A all  ·  RMB move, on an enemy attack  ·  A then LMB attack-move  ·  H hold  ·  Tab fight",
                18, TextAnchor.LowerLeft, new Color(0.86f, 0.82f, 0.72f, 0.85f));
            modeLabel = UiKit.Label(canvas.transform, "mode", new Vector2(.5f, 1), new Vector2(0, -86), Vector2.zero, "COMMAND", 30, TextAnchor.MiddleCenter, new Color(0.9f, 0.85f, 0.7f));
            modeLabel.rectTransform.pivot = new Vector2(.5f, .5f);
            startButton = UiKit.MakeButton(canvas.transform, "Start", new Vector2(1, 0), new Vector2(-40, 130), new Vector2(300, 84), "Start Battle", 34);
            startButton.GetComponent<RectTransform>().pivot = new Vector2(1, 0);
            startButton.onClick.AddListener(() => { GameAudio.Play2D("ui_click", 0.8f); b.StartFight(); });
            uiRoot.SetActive(false);
        }

        // ---- switching ----

        /// <summary>Centres the view on a point (the test uses it; so could a minimap click).</summary>
        public void LookAt(Vector3 p) { pivot = new Vector3(p.x, b.Field.Height(p.x, p.z), p.z); }

        public void Toggle()
        {
            if (Active) Leave(); else Enter();
        }

        void Enter()
        {
            Active = true;
            actionCam.enabled = false;
            var p = b.Player != null ? b.Player.Pos : Vector3.zero;
            pivot = new Vector3(p.x, 0, p.z) + Mathx.FromYaw(actionCam.Yaw) * 18f;
            yaw = actionCam.Yaw; height = 42f;
            uiRoot.SetActive(true);
            b.Hud.SetCommandMode(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            // His own soldier carries on by himself while he commands.
            if (b.Player != null) { b.Player.Wish = Vector3.zero; b.Player.WantBlock = false; }
        }

        void Leave()
        {
            Active = false;
            ClearSelection();
            uiRoot.SetActive(false);
            box.gameObject.SetActive(false);
            b.Hud.SetCommandMode(false);
            actionCam.enabled = true;
            if (b.Player != null) actionCam.SwitchTo(b.Player);     // fly down to him
        }

        // ---- per frame ----

        void Update()
        {
            if (b == null || !b.Ready) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (kb.tabKey.wasPressedThisFrame && !PlayerControl.Paused && b.Now != Battle.Phase.Over) Toggle();
            if (!Active) return;
            if (PlayerControl.Paused) return;
            if (kb.escapeKey.wasPressedThisFrame) { b.Hud.TogglePause(); return; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

            for (int i = selected.Count - 1; i >= 0; i--) if (!selected[i].Alive) { selected[i].Selected = false; selected.RemoveAt(i); }
            MoveCamera(kb, mouse);
            HandleSelection(kb, mouse);
            HandleOrders(kb, mouse);
            if (b.Now == Battle.Phase.Deploy && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) b.StartFight();
            startButton.gameObject.SetActive(b.Now == Battle.Phase.Deploy);
            UpdatePanel();
        }

        void MoveCamera(Keyboard kb, Mouse mouse)
        {
            float dt = Time.unscaledDeltaTime;
            float speed = Mathf.Lerp(18f, 90f, Mathf.InverseLerp(15f, 150f, height)) * (kb.leftShiftKey.isPressed ? 2.2f : 1f);
            Vector3 f = Mathx.FromYaw(yaw), r = Vector3.Cross(Vector3.up, f);
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += r;
            if (kb.aKey.isPressed && !attackMove && !kb.leftCtrlKey.isPressed) move -= r;
            if (kb.leftArrowKey.isPressed) move -= r;
            // Edge scrolling, only with the window in front.
            Vector2 m = mouse.position.ReadValue();
            if (Application.isFocused && !dragging)
            {
                const float edge = 6f;
                if (m.x < edge) move -= r; else if (m.x > Screen.width - edge) move += r;
                if (m.y < edge) move -= f; else if (m.y > Screen.height - edge) move += f;
            }
            pivot += move.normalized * speed * dt;
            pivot.x = Mathf.Clamp(pivot.x, -Field.Half, Field.Half);
            pivot.z = Mathf.Clamp(pivot.z, -Field.Half, Field.Half);
            if (kb.qKey.isPressed) yaw -= 70f * dt;
            if (kb.eKey.isPressed) yaw += 70f * dt;
            if (mouse.middleButton.isPressed) { var d = mouse.delta.ReadValue(); yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, 30f, 85f); }
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) height = Mathf.Clamp(height * (wheel > 0 ? 0.88f : 1.14f), 12f, 170f);

            float ground = b.Field.Height(pivot.x, pivot.z);
            pivot.y = Mathf.Lerp(pivot.y, ground, 1f - Mathf.Exp(-dt * 6f));
            var rot = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pos = pivot - rot * Vector3.forward * (height / Mathf.Sin(pitch * Mathf.Deg2Rad));
            float minY = b.Field.Height(pos.x, pos.z) + 4f;
            if (pos.y < minY) pos.y = minY;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pivot - pos));
            cam.fieldOfView = 50f;
            PostFx.Instance?.Follow(b.Player, 0f, 100f);
        }

        // ---- selection ----

        void HandleSelection(Keyboard kb, Mouse mouse)
        {
            bool add = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            Vector2 m = mouse.position.ReadValue();
            if (kb.digit1Key.wasPressedThisFrame) SelectType(UnitType.Infantry, add);
            if (kb.digit2Key.wasPressedThisFrame) SelectType(UnitType.Archer, add);
            if (kb.digit3Key.wasPressedThisFrame) SelectType(UnitType.Cavalry, add);
            if (kb.leftCtrlKey.isPressed && kb.aKey.wasPressedThisFrame) { ClearSelection(); foreach (var u in b.Army(Unit.Blue)) if (u.Alive) Select(u); }

            if (attackMove) return;      // the next left click is an order, not a selection
            if (mouse.leftButton.wasPressedThisFrame) { dragStart = m; dragging = true; }
            if (dragging)
            {
                Vector2 lo = Vector2.Min(dragStart, m), hi = Vector2.Max(dragStart, m);
                bool big = (hi - lo).magnitude > 8f;
                box.gameObject.SetActive(big);
                if (big)
                {
                    float k = 1080f / Screen.height;     // canvas units: the HUD scales to 1080 high
                    var scaler = box.GetComponentInParent<CanvasScaler>();
                    float s = scaler != null ? box.GetComponentInParent<Canvas>().scaleFactor : 1f;
                    box.anchoredPosition = lo / s; box.sizeDelta = (hi - lo) / s;
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    dragging = false;
                    box.gameObject.SetActive(false);
                    if (!add) ClearSelection();
                    if (big) SelectInRect(lo, hi);
                    else ClickSelect(m, add);
                }
            }
        }

        public void SelectInRect(Vector2 lo, Vector2 hi)
        {
            foreach (var u in b.Army(Unit.Blue))
            {
                if (!u.Alive) continue;
                Vector3 s = cam.WorldToScreenPoint(u.Pos + Vector3.up * 1f);
                if (s.z > 0 && s.x >= lo.x && s.x <= hi.x && s.y >= lo.y && s.y <= hi.y) Select(u);
            }
        }

        void ClickSelect(Vector2 m, bool add)
        {
            var u = UnitAtScreen(m, Unit.Blue);
            if (u == null) return;
            // A second click on the same soldier: every one of his kind on screen.
            if (u == lastClicked && Time.unscaledTime - lastClick < 0.35f)
            {
                foreach (var o in b.Army(Unit.Blue))
                {
                    if (!o.Alive || o.Type != u.Type) continue;
                    Vector3 s = cam.WorldToScreenPoint(o.Pos);
                    if (s.z > 0 && s.x >= 0 && s.x <= Screen.width && s.y >= 0 && s.y <= Screen.height) Select(o);
                }
            }
            else if (add && u.Selected) Deselect(u);
            else Select(u);
            lastClicked = u; lastClick = Time.unscaledTime;
        }

        Unit UnitAtScreen(Vector2 m, int team)
        {
            Unit best = null; float bestD = 34f * 34f;
            foreach (var u in b.Army(team))
            {
                if (!u.Alive) continue;
                Vector3 s = cam.WorldToScreenPoint(u.Pos + Vector3.up * (u.Type == UnitType.Cavalry ? 1.6f : 1f));
                if (s.z <= 0) continue;
                float d = ((Vector2)s - m).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        public void SelectType(UnitType t, bool add)
        {
            if (!add) ClearSelection();
            foreach (var u in b.Army(Unit.Blue)) if (u.Alive && u.Type == t) Select(u);
        }

        void Select(Unit u) { if (u.Selected) return; u.Selected = true; selected.Add(u); }
        void Deselect(Unit u) { u.Selected = false; selected.Remove(u); }
        public void ClearSelection() { foreach (var u in selected) u.Selected = false; selected.Clear(); }

        // ---- orders ----

        void HandleOrders(Keyboard kb, Mouse mouse)
        {
            Vector2 m = mouse.position.ReadValue();
            if (kb.aKey.wasPressedThisFrame && !kb.leftCtrlKey.isPressed && selected.Count > 0) attackMove = true;
            if (attackMove && mouse.leftButton.wasPressedThisFrame)
            {
                attackMove = false;
                if (GroundAtScreen(m, out var p)) Command(Unit.OrderKind.AttackMove, p, null);
                return;
            }
            if (kb.hKey.wasPressedThisFrame && b.Now != Battle.Phase.Deploy)
                foreach (var u in selected) u.GiveOrder(Unit.OrderKind.Hold, u.Pos, u.Forward);
            if (mouse.rightButton.wasPressedThisFrame && selected.Count > 0)
            {
                attackMove = false;
                var enemy = UnitAtScreen(m, Unit.Red);
                if (enemy != null) Command(Unit.OrderKind.Attack, enemy.Pos, enemy);
                else if (GroundAtScreen(m, out var p)) Command(Unit.OrderKind.Move, p, null);
            }
        }

        public bool GroundAtScreen(Vector2 m, out Vector3 p)
        {
            var ray = cam.ScreenPointToRay(m);
            for (float d = 2f; d < 900f; d += 1f)
            {
                p = ray.GetPoint(d);
                if (p.y < b.Field.Height(p.x, p.z)) { p.y = b.Field.Height(p.x, p.z); return Mathf.Abs(p.x) < Field.Limit && Mathf.Abs(p.z) < Field.Limit; }
            }
            p = default;
            return false;
        }

        /// <summary>Sends the selection: to an enemy all together, to a place in a block facing the way they go, infantry in front, archers behind, horse at the back.</summary>
        public void Command(Unit.OrderKind kind, Vector3 point, Unit enemy)
        {
            if (selected.Count == 0) return;
            if (b.Now == Battle.Phase.Deploy)
            {
                // Before the start only placing: on his own side of the line.
                if (enemy != null || kind != Unit.OrderKind.Move) { b.Hud.Message("ATTACKS WAIT FOR THE START", 2f); return; }
                point = Battle.InZone(Unit.Blue, point);
            }
            OrdersGiven++;
            markerPos = point; markerLeft = 1.2f;
            markerColor = kind == Unit.OrderKind.Move ? new Color(0.5f, 0.85f, 1f, 0.9f) : new Color(1f, 0.3f, 0.2f, 0.9f);
            GameAudio.Play2D("ui_click", 0.35f);
            if (enemy != null)
            {
                foreach (var u in selected) u.GiveOrder(Unit.OrderKind.Attack, enemy.Pos, enemy.Pos - u.Pos, enemy);
                return;
            }
            Vector3 centre = Vector3.zero;
            foreach (var u in selected) centre += u.Pos;
            centre /= selected.Count;
            Vector3 fwd = Mathx.Flat(point - centre);
            fwd = fwd.sqrMagnitude > 1f ? fwd.normalized : Mathx.FromYaw(yaw);
            // Placing before the battle: the block faces the enemy, not the way it walked.
            if (b.Now == Battle.Phase.Deploy) fwd = Vector3.forward;
            Formation(selected, kind, point, fwd, b.Field, b.Now == Battle.Phase.Deploy ? Unit.Blue : -1);
        }

        /// <summary>
        /// Sends men to a place in a block facing fwd: infantry in front, archers behind, horse at the back, nobody crossing the block.
        /// With a team, every place is kept on that team's deployment side (the start of the battle).
        /// </summary>
        public static void Formation(IReadOnlyList<Unit> units, Unit.OrderKind kind, Vector3 point, Vector3 fwd, Field field, int zoneTeam = -1)
        {
            if (units.Count == 0) return;
            Vector3 centre = Vector3.zero;
            foreach (var u in units) centre += u.Pos;
            centre /= units.Count;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var order = new List<Unit>(units);
            order.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
            int cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(order.Count * 2.5f)), 1, 30);
            float depth = 0f;
            for (int start = 0; start < order.Count; start += cols)
            {
                int n = Mathf.Min(cols, order.Count - start);
                // Within a row, the man furthest left goes to the leftmost place: nobody crosses the block.
                var row = order.GetRange(start, n);
                row.Sort((x, y) => Vector3.Dot(x.Pos - centre, right).CompareTo(Vector3.Dot(y.Pos - centre, right)));
                float spacing = 0f;
                foreach (var u in row) spacing = Mathf.Max(spacing, u.Type == UnitType.Cavalry ? 3.4f : 1.9f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 slot = point + right * ((i - (n - 1) * 0.5f) * spacing) - fwd * depth;
                    if (zoneTeam >= 0) slot = Battle.InZone(zoneTeam, slot);
                    slot.y = field.Height(slot.x, slot.z);
                    row[i].GiveOrder(kind, slot, fwd);
                }
                depth += spacing;
            }
        }

        static int Rank(Unit u) => u.Type == UnitType.Infantry ? 0 : u.Type == UnitType.Archer ? 1 : 2;

        void UpdatePanel()
        {
            int inf = 0, arc = 0, cav = 0;
            foreach (var u in selected) { if (u.Type == UnitType.Infantry) inf++; else if (u.Type == UnitType.Archer) arc++; else cav++; }
            panel.text = selected.Count == 0 ? (attackMove ? "Attack-move: click where to go" : "Nothing selected")
                : $"{selected.Count} selected:  {inf} infantry  ·  {arc} archers  ·  {cav} cavalry" + (attackMove ? "   -   attack-move: click where to go" : "");
            modeLabel.text = b.Now == Battle.Phase.Deploy ? "DEPLOYMENT  ·  place your troops behind the line, then Start (Enter)"
                : attackMove ? "COMMAND  ·  ATTACK-MOVE" : "COMMAND";
        }

        // ---- markers ----

        void LateUpdate()
        {
            if (!Active || b == null) return;
            int n = 0;
            foreach (var u in selected)
            {
                if (n >= mats.Length) break;
                // A ring readable from any height: it grows as the view pulls back.
                float size = (u.Type == UnitType.Cavalry ? 3.4f : 1.6f) * Mathf.Max(1f, height / 28f);
                mats[n] = Matrix4x4.TRS(u.Pos + Vector3.up * 0.08f, Quaternion.identity, new Vector3(size, 1, size));
                float hp = u.Hp / u.S.MaxHp;
                tints[n] = Color.Lerp(new Color(1f, 0.25f, 0.15f, 0.95f), new Color(0.45f, 1f, 0.45f, 0.95f), hp);
                fills[n] = hp;
                n++;
            }
            // Where the order was sent.
            markerLeft -= Time.unscaledDeltaTime;
            if (markerLeft > 0f && n < mats.Length)
            {
                float k = 1f + (1.2f - markerLeft) * 1.5f;
                mats[n] = Matrix4x4.TRS(markerPos + Vector3.up * 0.1f, Quaternion.identity, Vector3.one * 3f * k * Mathf.Max(1f, height / 28f));
                var c = markerColor; c.a *= Mathf.Clamp01(markerLeft);
                tints[n] = c; fills[n] = 1f;
                n++;
            }
            // Enemies being attacked by the selection.
            foreach (var u in selected)
            {
                if (n >= mats.Length) break;
                if (u.Order == Unit.OrderKind.Attack && u.OrderTarget != null && u.OrderTarget.Alive && u == FirstAttacker(u.OrderTarget))
                {
                    var t = u.OrderTarget;
                    float size = (t.Type == UnitType.Cavalry ? 3.6f : 1.9f) * Mathf.Max(1f, height / 28f);
                    mats[n] = Matrix4x4.TRS(t.Pos + Vector3.up * 0.08f, Quaternion.identity, new Vector3(size, 1, size));
                    tints[n] = new Color(1f, 0.2f, 0.12f, 0.9f); fills[n] = 1f;
                    n++;
                }
            }
            if (b.Now == Battle.Phase.Deploy)
                for (float x = -Field.Limit; x <= Field.Limit && n < mats.Length; x += 5f)
                {
                    var p = new Vector3(x, b.Field.Height(x, -Battle.DeployLine) + 0.1f, -Battle.DeployLine);
                    mats[n] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 1.4f * Mathf.Max(1f, height / 40f));
                    tints[n] = new Color(1f, 0.85f, 0.4f, 0.75f); fills[n] = 1f;
                    n++;
                }
            if (n == 0) return;
            block.SetVectorArray("_Tint", tints);
            block.SetFloatArray("_Fill", fills);
            Graphics.DrawMeshInstanced(quad, 0, ringMat, mats, n, block, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }

        Unit FirstAttacker(Unit target)
        {
            foreach (var u in selected) if (u.Order == Unit.OrderKind.Attack && u.OrderTarget == target) return u;
            return null;
        }

        void OnDestroy() { Active = false; }
    }
}
