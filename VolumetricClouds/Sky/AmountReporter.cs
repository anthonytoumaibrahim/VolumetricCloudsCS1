namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Says when a 0..1 amount (our rain, our fog) has moved far enough to be worth one
    /// ALWAYS-ON log line: when it starts, each quarter it travels, when it reaches the top and
    /// when it is gone. A slider dragged end to end is five lines, a steady sky is none.
    /// </summary>
    /// <remarks>
    /// The per-five-second weather lines are behind Detailed logging, and a Reset switches that
    /// off. The first report after one was "fog seemed to also have showed up, not sure if it's
    /// ours" -- and the log could not say, because nothing always-on recorded whether our fog or
    /// our rain was drawing at all. Pure, so it can be exercised offline.
    /// </remarks>
    public sealed class AmountReporter
    {
        private const float Start = 0.02f;
        private const float Gone = 0.005f;
        private const float Step = 0.25f;
        private const float Top = 0.995f;

        private float _reported;

        /// <summary>True when <paramref name="amount"/> should be logged now.</summary>
        public bool Moved(float amount)
        {
            if (amount <= Gone)
            {
                if (_reported <= 0f)
                    return false;

                _reported = 0f;
                return true;
            }

            bool started = _reported <= 0f && amount >= Start;
            bool stepped = _reported > 0f && System.Math.Abs(amount - _reported) >= Step;
            bool topped = _reported > 0f && amount >= Top && _reported < Top;

            if (!started && !stepped && !topped)
                return false;

            _reported = amount;
            return true;
        }

        /// <summary>A new city starts from nothing reported.</summary>
        public void Reset()
        {
            _reported = 0f;
        }
    }
}
