using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Feeds the halo patches their per-frame inputs and reports what they're doing.
    /// </summary>
    public class HaloController : MonoBehaviour
    {
        private const float LogInterval = 5f;
        private const float ProbeDelay = 4f;

        private float _nextLogTime;
        private float _probeTime;
        private bool _probed;

        private void Start()
        {
            _probeTime = Time.time + ProbeDelay;
        }

        private void LateUpdate()
        {
            if (!_probed && Time.time >= _probeTime)
            {
                _probed = true;
                LightingProbe.Dump();
            }

            if (Time.time < _nextLogTime)
                return;

            _nextLogTime = Time.time + LogInterval;

            if (!Log.Detailed)
            {
                // The counters are still reset, so the next detailed line covers five seconds
                // rather than however long the switch was off.
                HaloAdjuster.ResetCounters();
                return;
            }

            string ranges = HaloAdjuster.LongCalls == 0
                ? "none"
                : HaloAdjuster.ObservedMinRange.ToString("F1") + ".." + HaloAdjuster.ObservedMaxRange.ToString("F1") +
                  " maxIntensity=" + HaloAdjuster.ObservedMaxIntensity.ToString("F2");

            Log.Detail("dynamic lights: patched=" + Patcher.IsPatched +
                    " | calls=" + HaloAdjuster.LongCalls +
                    // Reported HERE, by the component that resets the counters just below:
                    // HaloOverride logs in the same frame, after this one, and used to print
                    // this count freshly zeroed -- a tripwire that could never fire.
                    " lampsTagged=" + HaloAdjuster.LampsTagged + (HaloAdjuster.TagLamps ? "" : " (tagging off)") +
                    " volumeTrue=" + HaloAdjuster.VolumeTrue +
                    " | observedRange=" + ranges);

            HaloAdjuster.ResetCounters();
        }
    }
}
