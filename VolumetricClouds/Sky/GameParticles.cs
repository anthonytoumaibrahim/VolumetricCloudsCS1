using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Draws the game's particle effects -- factory smoke above all -- AFTER our rain, fog and
    /// rainbow (1.3.2; reported: the fog "is making smoke (from industrial factories) disappear").
    /// </summary>
    /// <remarks>
    /// Particles are see-through and write no depth, so a pass drawn after them stops only at the
    /// ground behind them: all the fog between a plume and the ground was painted over the plume,
    /// and in a thick fog it was gone. The game's own particle shaders ('Custom/Particles/Alpha
    /// Blended' and 'Additive (Soft)', disassembled 2026-10-02) apply no fog at all, and its fog is
    /// an opaque image effect drawn before them: the game always draws its smoke over its fog. Ours
    /// now does the same. Every material of the game's particle effects that sits among the city's
    /// see-through objects (after the opaque range, before <see cref="CloudVolume.AirQueue"/>) goes
    /// into the queues between the air and the clouds' late draw, in the order they had, so from
    /// above the base the clouds still cover the smoke under them. The price: smoke deep inside a
    /// fog is not dimmed by the fog in front of it, as with the game's fog.
    /// Only the game's effects: ParticleEffect.CreateEffect instantiates each one under the
    /// "Particle Effects" object (ParticleEffect.GetEffectRoot, IL), Workshop assets' included.
    /// Checked again whenever that object gains children. Put back on unload, only where still as we
    /// left it; a queue something else changes meanwhile is theirs (invariant 6).
    /// </remarks>
    public class GameParticles : MonoBehaviour
    {
        private const string RootName = "Particle Effects";
        private const float CheckInterval = 5f;
        private const int NamesPerGroup = 8;

        private readonly Dictionary<Material, int> _original = new Dictionary<Material, int>();
        private readonly HashSet<Material> _leftAlone = new HashSet<Material>();
        private GameObject _root;
        private int _children = -1;
        private float _nextCheck;
        private bool _rootLogged;

        /// <summary>
        /// The queue a particle material moves to: 3000 -> AirQueue + 2, 3001 -> AirQueue + 3, earlier
        /// and later ones at either end, so their order among themselves and with the rain streaks
        /// (LateQueue + 10) is kept and all of them stay before the clouds' late draw.
        /// </summary>
        public static int QueueFor(int original)
        {
            return Mathf.Clamp(original - 3000 + CloudVolume.AirQueue + 2,
                               CloudVolume.AirQueue + 1, CloudVolume.LateQueue - 1);
        }

        private void LateUpdate()
        {
            if (Time.time < _nextCheck)
                return;

            _nextCheck = Time.time + CheckInterval;

            if (_root == null)
            {
                _root = GameObject.Find(RootName);
                if (_root == null)
                {
                    if (!_rootLogged)
                    {
                        _rootLogged = true;
                        Log.Msg("particles: no '" + RootName + "' object (yet); the game's smoke keeps its draw order");
                    }
                    return;
                }
            }

            int children = _root.transform.childCount;
            if (children == _children)
                return;

            _children = children;
            Scan();
        }

        private void Scan()
        {
            ParticleSystemRenderer[] renderers = _root.GetComponentsInChildren<ParticleSystemRenderer>(true);

            // Per original queue, for the log: how many moved and the first few names.
            var moved = new SortedDictionary<int, List<Material>>();
            var left = new SortedDictionary<int, List<Material>>();

            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] materials = renderers[r].sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    if (material == null || _original.ContainsKey(material) || _leftAlone.Contains(material))
                        continue;

                    int queue = material.renderQueue;
                    if (queue <= 2500 || queue >= CloudVolume.AirQueue)
                    {
                        // Opaque or alpha-tested (in the depth the fog stops at), or already after the air.
                        _leftAlone.Add(material);
                        Add(left, queue, material);
                        continue;
                    }

                    _original[material] = queue;
                    material.renderQueue = QueueFor(queue);
                    Add(moved, queue, material);
                }
            }

            if (moved.Count == 0 && left.Count == 0)
                return;

            var line = new StringBuilder("particles: ");
            line.Append(renderers.Length).Append(" particle systems of the game's effects; ");
            if (moved.Count > 0)
            {
                line.Append("drawn after the rain and fog, before the clouds:");
                foreach (KeyValuePair<int, List<Material>> group in moved)
                {
                    // Read back: the constant is only what was asked for.
                    line.Append(" queue ").Append(group.Key).Append(" -> ").Append(group.Value[0].renderQueue);
                    Describe(line, group.Value);
                }
            }
            else
            {
                line.Append("none new among the see-through objects");
            }

            if (left.Count > 0)
            {
                line.Append("; left alone:");
                foreach (KeyValuePair<int, List<Material>> group in left)
                {
                    line.Append(" queue ").Append(group.Key);
                    Describe(line, group.Value);
                }
            }

            Log.Msg(line.ToString());
        }

        private static void Add(SortedDictionary<int, List<Material>> groups, int queue, Material material)
        {
            List<Material> list;
            if (!groups.TryGetValue(queue, out list))
            {
                list = new List<Material>();
                groups[queue] = list;
            }
            list.Add(material);
        }

        private static void Describe(StringBuilder line, List<Material> materials)
        {
            line.Append(" x").Append(materials.Count).Append(" (");
            int shown = Mathf.Min(materials.Count, NamesPerGroup);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    line.Append(", ");
                Material material = materials[i];
                line.Append('\'').Append(material.name).Append("' ")
                    .Append(material.shader == null ? "null" : material.shader.name);
            }
            if (materials.Count > shown)
                line.Append(", +").Append(materials.Count - shown).Append(" more");
            line.Append(')');
        }

        private void OnDestroy()
        {
            int restored = 0, theirs = 0;
            foreach (KeyValuePair<Material, int> pair in _original)
            {
                Material material = pair.Key;
                if (material == null)
                    continue;

                if (material.renderQueue == QueueFor(pair.Value))
                {
                    material.renderQueue = pair.Value;
                    restored++;
                }
                else
                {
                    theirs++;
                }
            }

            if (_original.Count > 0)
                Log.Msg("particles: " + restored + " material(s) back to their own draw order" +
                        (theirs > 0 ? "; " + theirs + " changed by something else meanwhile, left as they are" : ""));
            _original.Clear();
            _leftAlone.Clear();
        }
    }
}
