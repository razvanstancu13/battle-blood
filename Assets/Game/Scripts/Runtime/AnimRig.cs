using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BattleBlood
{
    /// <summary>
    /// Plays a model's LAGP clips through a small Playables graph: a locomotion blend by ground speed, full-body actions over it,
    /// and upper-body actions over that. No Animator Controller asset: all three soldier types and the horse share this code.
    /// </summary>
    public class AnimRig
    {
        static readonly Dictionary<string, Dictionary<string, AnimationClip>> Library = new();

        public static Dictionary<string, AnimationClip> ClipsOf(string model)
        {
            if (Library.TryGetValue(model, out var d)) return d;
            d = new Dictionary<string, AnimationClip>();
            foreach (var c in Resources.LoadAll<AnimationClip>("Art/Models/" + model))
                if (!c.name.StartsWith("__preview")) d[c.name] = c;
            return Library[model] = d;
        }

        PlayableGraph graph;
        AnimationLayerMixerPlayable layers;
        AnimationMixerPlayable locoMix, actionMix, upperMix;
        readonly List<Loco> loco = new();
        readonly Dictionary<string, int> actionIndex = new(), upperIndex = new();
        AnimationClipPlayable[] actionClips, upperClips;
        float phase;

        int actionSlot = -1, upperSlot = -1;
        float actionWeight, upperWeight, actionFade = 0.12f, upperFade = 0.1f;
        float actionLeft, upperLeft;
        bool actionHold, actionParked;

        struct Loco { public AnimationClipPlayable P; public float Pace, Length; public bool Moving; }

        public bool Valid => graph.IsValid();
        public bool ActionBusy => actionSlot >= 0 && (actionHold || actionLeft > 0f);

        /// <param name="locos">Clip name and its ground pace in m/s, slowest first (0 = standing still).</param>
        public AnimRig(string model, Animator animator, (string clip, float pace)[] locos, string[] actions, string[] upper, AvatarMask upperMask)
        {
            var clips = ClipsOf(model);
            graph = PlayableGraph.Create("rig " + model);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "out", animator);

            int locoCount = 0;
            foreach (var l in locos) if (clips.ContainsKey(l.clip)) locoCount++;
            locoMix = AnimationMixerPlayable.Create(graph, Mathf.Max(1, locoCount));
            int i = 0;
            foreach (var l in locos)
            {
                if (!clips.TryGetValue(l.clip, out var c)) { Debug.LogWarning($"[ANIM] {model}: no clip {l.clip}"); continue; }
                var p = AnimationClipPlayable.Create(graph, c);
                p.SetApplyFootIK(false);
                p.SetSpeed(l.pace > 0f ? 0.0 : 1.0);    // moving clips follow our phase, so their feet stay in step when blended
                graph.Connect(p, 0, locoMix, i);
                loco.Add(new Loco { P = p, Pace = l.pace, Length = c.length, Moving = l.pace > 0f });
                i++;
            }

            actionClips = Slots(clips, actions, actionIndex, out actionMix, model);
            upperClips = Slots(clips, upper, upperIndex, out upperMix, model);

            layers = AnimationLayerMixerPlayable.Create(graph, 3);
            graph.Connect(locoMix, 0, layers, 0);
            graph.Connect(actionMix, 0, layers, 1);
            graph.Connect(upperMix, 0, layers, 2);
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, 0f);
            layers.SetInputWeight(2, 0f);
            if (upperMask != null) layers.SetLayerMaskFromAvatarMask(2, upperMask);
            output.SetSourcePlayable(layers);
            graph.Play();
        }

        AnimationClipPlayable[] Slots(Dictionary<string, AnimationClip> clips, string[] names, Dictionary<string, int> index, out AnimationMixerPlayable mix, string model)
        {
            var list = new List<AnimationClipPlayable>();
            mix = AnimationMixerPlayable.Create(graph, Mathf.Max(1, names.Length));
            foreach (var n in names)
            {
                if (!clips.TryGetValue(n, out var c)) { Debug.LogWarning($"[ANIM] {model}: no clip {n}"); continue; }
                var p = AnimationClipPlayable.Create(graph, c);
                p.SetApplyFootIK(false);
                graph.Connect(p, 0, mix, list.Count);
                mix.SetInputWeight(list.Count, 0f);
                p.Pause();
                index[n] = list.Count;
                list.Add(p);
            }
            return list.ToArray();
        }

        /// <summary>Blend by ground speed.</summary>
        public void SetSpeed(float speed, float dt)
        {
            if (loco.Count == 0) return;
            // The pair of clips the speed falls between.
            int a = 0, b = 0;
            for (int i = 0; i < loco.Count; i++)
                if (speed >= loco[i].Pace) { a = i; b = Mathf.Min(i + 1, loco.Count - 1); }
            float pa = loco[a].Pace, pb = loco[b].Pace;
            float t = b == a ? 0f : Mathf.Clamp01((speed - pa) / Mathf.Max(0.01f, pb - pa));
            for (int i = 0; i < loco.Count; i++) locoMix.SetInputWeight(i, i == a ? 1f - t : i == b ? t : 0f);

            // One shared phase: each stride takes the same part of its clip, whichever clips are mixed.
            float pace = Mathf.Max(0.5f, Mathf.Lerp(pa, pb, t));
            float len = Mathf.Max(0.05f, Mathf.Lerp(loco[a].Length, loco[b].Length, t));
            phase = Mathf.Repeat(phase + dt * (speed / pace) / len, 1f);
            for (int i = 0; i < loco.Count; i++)
                if (loco[i].Moving && (i == a || i == b)) loco[i].P.SetTime(phase * loco[i].Length);
        }

        public float Play(string name, float speed = 1f, float fade = 0.12f, bool hold = false)
        {
            if (!actionIndex.TryGetValue(name, out int slot)) return 0f;
            if (actionSlot >= 0 && actionSlot != slot) actionMix.SetInputWeight(actionSlot, 0f);
            var p = actionClips[slot];
            p.SetTime(0); p.SetSpeed(speed); p.Play();
            actionSlot = slot; actionFade = fade; actionHold = hold; actionParked = false;
            float length = p.GetAnimationClip().length / Mathf.Max(0.01f, speed);
            actionLeft = length;
            actionMix.SetInputWeight(slot, 1f);
            return length;
        }

        public void StopAction(float fade = 0.15f) { actionHold = false; actionLeft = 0f; actionFade = fade; }

        bool upperHold;
        string upperName;
        public string UpperPlaying => upperSlot >= 0 && (upperHold || upperLeft > upperFade) ? upperName : null;
        public bool HasUpper(string name) => upperIndex.ContainsKey(name);

        /// <summary>Stops a held (or any) upper-body clip, fading out.</summary>
        public void StopUpper(float fade = 0.15f) { upperHold = false; upperLeft = 0f; upperFade = fade; }

        public float PlayUpper(string name, float speed = 1f, float fade = 0.08f, bool hold = false)
        {
            if (!upperIndex.TryGetValue(name, out int slot)) return 0f;
            upperHold = hold; upperName = name;
            if (upperSlot >= 0 && upperSlot != slot) upperMix.SetInputWeight(upperSlot, 0f);
            var p = upperClips[slot];
            p.SetTime(0); p.SetSpeed(speed); p.Play();
            upperSlot = slot; upperFade = fade;
            float length = p.GetAnimationClip().length / Mathf.Max(0.01f, speed);
            upperLeft = length;
            upperMix.SetInputWeight(slot, 1f);
            return length;
        }

        public void Tick(float dt)
        {
            if (actionSlot >= 0)
            {
                actionLeft -= dt;
                // A held action (death) stays on its last frame for good.
                if (actionHold && actionLeft <= 0f && !actionParked)
                {
                    var held = actionClips[actionSlot];
                    held.SetTime(held.GetAnimationClip().length - 0.001);
                    held.Pause();
                    actionParked = true;
                }
                bool on = actionHold || actionLeft > actionFade;
                actionWeight = Mathf.MoveTowards(actionWeight, on ? 1f : 0f, dt / Mathf.Max(0.01f, actionFade));
                layers.SetInputWeight(1, actionWeight);
                if (!on && actionWeight <= 0f) { actionClips[actionSlot].Pause(); actionMix.SetInputWeight(actionSlot, 0f); actionSlot = -1; }
            }
            if (upperSlot >= 0)
            {
                upperLeft -= dt;
                bool on = upperHold || upperLeft > upperFade;
                upperWeight = Mathf.MoveTowards(upperWeight, on ? 1f : 0f, dt / Mathf.Max(0.01f, upperFade));
                layers.SetInputWeight(2, upperWeight);
                if (!on && upperWeight <= 0f) { upperClips[upperSlot].Pause(); upperMix.SetInputWeight(upperSlot, 0f); upperSlot = -1; }
            }
        }

        /// <summary>Puts an action on its last frame at full weight and applies it now, whatever was playing.</summary>
        public void SnapToEnd(string name)
        {
            if (!actionIndex.TryGetValue(name, out int slot) || !graph.IsValid()) return;
            for (int i = 0; i < actionClips.Length; i++) actionMix.SetInputWeight(i, i == slot ? 1f : 0f);
            var p = actionClips[slot];
            p.SetTime(p.GetAnimationClip().length - 0.001); p.Pause();
            actionSlot = slot; actionHold = true; actionParked = true; actionWeight = 1f;
            layers.SetInputWeight(1, 1f); layers.SetInputWeight(2, 0f);
            graph.Evaluate(0f);
        }

        /// <summary>Freezes the pose where it is: a corpse costs nothing from then on.</summary>
        public void Freeze()
        {
            if (graph.IsValid()) graph.Stop();
        }

        public void Destroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        /// <summary>A mask for the bones from the chest up, by path: generic rigs need the transforms named one by one.</summary>
        public static AvatarMask UpperMask(Transform modelRoot, Transform spine)
        {
            var mask = new AvatarMask();
            var all = modelRoot.GetComponentsInChildren<Transform>(true);
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, PathOf(modelRoot, all[i]));
                mask.SetTransformActive(i, all[i].IsChildOf(spine));
            }
            return mask;
        }

        static string PathOf(Transform root, Transform t)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (var c = t; c != root; c = c.parent) parts.Insert(0, c.name);
            return string.Join("/", parts);
        }
    }
}
