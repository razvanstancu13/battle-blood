using UnityEngine;
using UnityEngine.InputSystem;

namespace BattleBlood
{
    /// <summary>Over-the-shoulder camera: follows the player's unit, keeps above the ground, flies to the next body when he dies, shakes on blows.</summary>
    public class PlayerCam : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public float Yaw, Pitch = 8f;
        Unit target;
        Vector3 focus;
        float shake, transit, zoom, baseFov = 60f, fovKick;
        Vector3 transitFrom;
        Quaternion transitRot;
        public Vector3 AimPoint { get; private set; }

        public void Init(Camera cam, Unit t)
        {
            Cam = cam; target = t;
            Yaw = t.Yaw;
            focus = t.Pos + Vector3.up * 1.5f;
            // Opening shot: high over the field, coming down during the deployment.
            transform.position = t.Pos + new Vector3(0, 34f, t.Team == Unit.Blue ? -70f : 70f);
            transform.rotation = Quaternion.LookRotation(Vector3.forward);
            transitFrom = transform.position; transitRot = transform.rotation;
            transit = 0f; transitLen = 6.5f;
        }
        float transitLen = 1.2f;

        /// <summary>V: through his own eyes instead of over his shoulder.</summary>
        public bool FirstPerson;
        Transform hiddenHead;

        void ShowHead()
        {
            if (hiddenHead != null) hiddenHead.localScale = Vector3.one;
            hiddenHead = null;
        }

        public void SwitchTo(Unit t)
        {
            ShowHead();
            target = t;
            transitFrom = transform.position;
            transitRot = transform.rotation;
            transit = 0f; transitLen = 1.25f;
            Yaw = t.Yaw;
            Pitch = 8f;
        }

        public void Shake(float amount) { shake = Mathf.Min(0.7f, Mathf.Max(shake, amount)); }
        public float DistanceTo(Vector3 p) => Vector3.Distance(transform.position, p);
        public void Look(Vector2 delta)
        {
            Yaw += delta.x;
            Pitch = Mathf.Clamp(Pitch - delta.y, -28f, 62f);
        }
        public void SetZoom(float z) { zoom = z; }
        float punch;
        Unit shotOf; float shotLeft, shotLen, shotSide;

        /// <summary>A kill shot: for a moment the camera leaves his shoulder and swings slowly round the man he has just killed.</summary>
        public void KillShot(Unit victim, float seconds)
        {
            shotOf = victim; shotLeft = shotLen = seconds;
            shotSide = Random.value < 0.5f ? 1f : -1f;
        }
        /// <summary>A cinematic push in: the view narrows onto the kill, then eases back.</summary>
        public void Punch(float amount) { punch = Mathf.Max(punch, amount); }

        void LateUpdate()
        {
            if (target == null || Battle.I == null || !Battle.I.Ready || Rts.Active) { ShowHead(); return; }
            float dt = Time.unscaledDeltaTime;
            var field = Battle.I.Field;
            bool mounted = target.Type == UnitType.Cavalry;
            float bodyY = mounted ? 2.75f : 1.55f;
            Vector3 want = target.Pos + Vector3.up * bodyY;
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-dt * (mounted ? 11f : 15f)));

            float dist = (mounted ? 5.8f : 3.1f) * Mathf.Lerp(1f, 0.62f, zoom);
            float side = (mounted ? 0.75f : 0.62f) * Mathf.Lerp(1f, 0.85f, zoom);
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            Vector3 pos = focus + rot * new Vector3(side, 0.1f, -dist);
            float minY = field.Height(pos.x, pos.z) + 0.35f;
            if (pos.y < minY) pos.y = minY;

            if (shotLeft > 0f && shotOf != null)
            {
                shotLeft -= dt;
                float k = 1f - shotLeft / shotLen;
                Vector3 look = shotOf.ChestPos;
                Vector3 across = Mathx.Flat(look - target.Pos);
                if (across.sqrMagnitude < 0.01f) across = Mathx.FromYaw(Yaw);
                across.Normalize();
                // Side-on to the two of them, low, turning slowly.
                Vector3 orbit = Quaternion.Euler(0, shotSide * (75f + 35f * k), 0) * across;
                float back = shotOf.Type == UnitType.Cavalry ? 5.5f : 3.4f;
                Vector3 cpos = look + orbit * back + Vector3.up * (0.35f + 0.25f * k);
                float floor = field.Height(cpos.x, cpos.z) + 0.4f;
                if (cpos.y < floor) cpos.y = floor;
                transform.SetPositionAndRotation(cpos, Quaternion.LookRotation(look - cpos));
                Cam.fieldOfView = 42f;
                if (shotLeft <= 0f) { transitFrom = cpos; transitRot = transform.rotation; transit = 0f; transitLen = 0.45f; }
                return;
            }
            bool eyes = FirstPerson && target.Alive && transit >= 1f;
            if (eyes)
            {
                // At his eyes, a little in front of the face, so the helmet stays behind the lens; his head itself is shrunk away.
                var head = target.HeadBone;
                Vector3 eye = (head != null ? head.position : target.Pos + Vector3.up * (mounted ? 2.6f : 1.62f)) + Vector3.up * 0.05f;
                pos = eye + Quaternion.Euler(0, Yaw, 0) * Vector3.forward * 0.16f;
                if (head != null && hiddenHead != head) { ShowHead(); hiddenHead = head; }
                if (hiddenHead != null) hiddenHead.localScale = Vector3.one * 0.001f;
                Cam.nearClipPlane = 0.05f;
            }
            else { ShowHead(); Cam.nearClipPlane = 0.12f; }

            if (transit < 1f)
            {
                transit = Mathf.Min(1f, transit + dt / transitLen);
                float e = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, transit));
                pos = Vector3.Lerp(transitFrom, pos, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * Mathf.Min(14f, Vector3.Distance(transitFrom, pos) * 0.25f);
                rot = Quaternion.Slerp(transitRot, rot, e);
            }

            shake = Mathf.MoveTowards(shake, 0f, dt * 1.6f);
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 31f;
                float s = shake * shake;
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t, 3.1f) - 0.5f) * 9f * s, (Mathf.PerlinNoise(1.7f, t) - 0.5f) * 9f * s, (Mathf.PerlinNoise(t, t) - 0.5f) * 7f * s);
            }
            transform.SetPositionAndRotation(pos, rot);

            float speedKick = mounted ? Mathf.InverseLerp(8f, 14f, target.Speed) * 8f : target.Speed > 5f ? 3f : 0f;
            fovKick = Mathf.Lerp(fovKick, speedKick, dt * 3f);
            punch = Mathf.MoveTowards(punch, 0f, dt * 0.45f);
            Cam.fieldOfView = Mathf.Lerp(baseFov + fovKick + (eyes ? 12f : 0f), 40f, zoom) - punch * 18f;

            // What the crosshair is on: the first thing along the view ray, for aiming arrows and swords.
            AimPoint = RayToWorld(transform.position, transform.forward, 240f);
            PostFx.Instance?.Follow(target, zoom, Vector3.Distance(transform.position, AimPoint));
        }

        Vector3 RayToWorld(Vector3 from, Vector3 dir, float max)
        {
            var field = Battle.I.Field;
            for (float d = 4f; d < max; d += 1.5f)
            {
                Vector3 p = from + dir * d;
                if (p.y < field.Height(p.x, p.z)) return p;
            }
            return from + dir * max;
        }
    }

    /// <summary>Keyboard and mouse into the same intents the AI uses.</summary>
    public class PlayerControl : MonoBehaviour
    {
        public float Sensitivity = 0.11f;
        public static bool Paused;

        void Update()
        {
            var b = Battle.I;
            if (b == null || !b.Ready || Battle.AutoPlay != null || Rts.Active) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame && b.Now != Battle.Phase.Over) { b.Hud.TogglePause(); return; }
            if (kb.vKey.wasPressedThisFrame) { b.PlayerCam.FirstPerson = !b.PlayerCam.FirstPerson; b.Hud.Message(b.PlayerCam.FirstPerson ? "FIRST PERSON  ·  V" : "THIRD PERSON  ·  V", 1.2f); }
            if (Paused) return;
            if (b.Now == Battle.Phase.Over) return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            var cam = b.PlayerCam;
            cam.Look(mouse.delta.ReadValue() * Sensitivity);

            var u = b.Player;
            if (u == null || !u.Alive) return;
            if (b.Now == Battle.Phase.Deploy) return;
            u.Order = Unit.OrderKind.None;       // in his own hands, orders given from above no longer apply

            Vector3 f = Mathx.FromYaw(cam.Yaw), r = Vector3.Cross(Vector3.up, f);
            Vector3 wish = Vector3.zero;
            if (kb.wKey.isPressed) wish += f;
            if (kb.sKey.isPressed) wish -= f;
            if (kb.dKey.isPressed) wish += r;
            if (kb.aKey.isPressed) wish -= r;
            bool sprint = kb.leftShiftKey.isPressed;
            bool walk = kb.leftCtrlKey.isPressed;
            u.Wish = wish.sqrMagnitude > 1f ? wish.normalized : wish;
            u.WishSpeed = walk ? u.S.Walk : sprint ? u.S.Sprint : u.S.Run;
            if (u.Type == UnitType.Cavalry && u.ChargeLeft > 0f) { u.ChargeLeft -= Time.deltaTime; u.WishSpeed = u.S.Sprint * 1.12f; }
            else if (u.Type == UnitType.Cavalry) u.WishSpeed = sprint ? u.S.Sprint * 0.86f : u.S.Run;

            // The body points where the camera points; the bow also takes the view's pitch to what the crosshair is on.
            Vector3 aim = (cam.AimPoint - u.ChestPos).normalized;
            if (u.Type != UnitType.Archer) aim = new Vector3(aim.x, Mathf.Clamp(aim.y, -0.3f, 0.3f), aim.z);
            u.Aim = aim;

            switch (u.Type)
            {
                case UnitType.Infantry:
                    u.WantBlock = mouse.rightButton.isPressed;
                    if (mouse.leftButton.wasPressedThisFrame && !u.Blocking) u.AttackInput();
                    if (kb.qKey.wasPressedThisFrame || (mouse.leftButton.wasPressedThisFrame && u.Blocking)) u.BeginBash();
                    if (kb.spaceKey.wasPressedThisFrame) u.BeginRoll(wish);
                    cam.SetZoom(0f);
                    break;
                case UnitType.Archer:
                    bool aiming = mouse.rightButton.isPressed || u.Action == Act.Draw;
                    cam.SetZoom(Mathf.MoveTowards(zoomNow, aiming ? 1f : 0f, Time.unscaledDeltaTime * 4f));
                    Battle.AimSlow = Mathf.Lerp(1f, 0.5f, zoomNow);       // aiming the bow: time at half speed
                    zoomNow = Mathf.MoveTowards(zoomNow, aiming ? 1f : 0f, Time.unscaledDeltaTime * 4f);
                    if (kb.fKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame) wantFire = !wantFire;
                    if (mouse.leftButton.wasPressedThisFrame) u.BeginNock(wantFire && u.FireReload <= 0f);
                    if (mouse.leftButton.isPressed && u.Action == Act.None) u.BeginNock(wantFire && u.FireReload <= 0f);
                    if (mouse.leftButton.wasReleasedThisFrame) { if (u.FireNocked) wantFire = false; u.Release(aim); }
                    if (kb.spaceKey.wasPressedThisFrame) u.BeginRoll(wish);
                    b.Hud.SetArrowMode(wantFire, u.FireReload);
                    break;
                case UnitType.Cavalry:
                    if (mouse.leftButton.wasPressedThisFrame) u.AttackInput();
                    if (kb.spaceKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame) u.BeginCharge();
                    cam.SetZoom(0f);
                    break;
            }
        }
        float zoomNow;
        bool wantFire;
    }
}
