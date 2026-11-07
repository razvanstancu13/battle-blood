using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>
    /// The horse's tail swings: the chain of tail bones that tools/horse_tail_rig.py put down the generated tail (tail_hang1..N) follows
    /// the clips loosely, each bone's tip a point with momentum and gravity, sprung back towards where the clip holds it.
    /// Runs after the Animator each frame; a dead horse's tail settles and is then left alone.
    /// </summary>
    public class TailBones
    {
        readonly Transform[] bones;
        readonly Vector3[] localTip;
        readonly float[] length;
        readonly Vector3[] tip, prev;
        float settle;

        public static TailBones Find(Transform horse)
        {
            var list = new List<Transform>();
            for (int i = 1; i < 10; i++)
            {
                Transform found = null;
                foreach (var t in horse.GetComponentsInChildren<Transform>(true)) if (t.name == "tail_hang" + i) { found = t; break; }
                if (found == null) break;
                list.Add(found);
            }
            return list.Count > 0 ? new TailBones(list.ToArray()) : null;
        }

        TailBones(Transform[] chain)
        {
            bones = chain;
            int n = bones.Length;
            localTip = new Vector3[n]; length = new float[n]; tip = new Vector3[n]; prev = new Vector3[n];
            // Measured in the bind pose: where each bone's tip is, in its own space.
            for (int i = 0; i < n; i++)
            {
                Vector3 end = i + 1 < n ? bones[i + 1].position
                    : bones[i].position + (bones[i].position - (i > 0 ? bones[i - 1].position : bones[i].parent.position)).normalized * Vector3.Distance(bones[i].position, i > 0 ? bones[i - 1].position : bones[i].position + Vector3.down * 0.2f);
                localTip[i] = bones[i].InverseTransformPoint(end);
                length[i] = Vector3.Distance(bones[i].position, end);
                tip[i] = prev[i] = end;
            }
        }

        public void Tick(float dt, bool alive)
        {
            if (!alive) { settle += dt; if (settle > 5f) return; }
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 1f / 30f);
            Vector3 g = Vector3.down * 9.81f * dt * dt;
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                Vector3 want = b.TransformPoint(localTip[i]);            // where the clip has it
                Vector3 v = (tip[i] - prev[i]) * 0.86f;
                prev[i] = tip[i];
                tip[i] += v + g;
                tip[i] += (want - tip[i]) * (alive ? 0.16f : 0.05f);   // sprung back to the clip, gently
                Vector3 d = tip[i] - b.position;
                if (d.sqrMagnitude < 1e-8f) d = want - b.position;
                tip[i] = b.position + d.normalized * length[i];
                // Turn the bone to point at its tip; the bones below follow before their own turn.
                Vector3 now = want - b.position;
                b.rotation = Quaternion.FromToRotation(now, tip[i] - b.position) * b.rotation;
            }
        }
    }
}
