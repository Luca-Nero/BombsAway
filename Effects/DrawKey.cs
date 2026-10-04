namespace BombsAway
{
    /// <summary>
    /// What a pixel texture shows, folded into one number: a texture is redrawn only when its key
    /// changes. Built every frame without allocating (strings go in by their hash, not by being
    /// joined into a new one). 64 bits, so two different states sharing a key is out of reach in
    /// practice; if it ever happened, the texture would only be a change late.
    /// </summary>
    internal struct DrawKey
    {
        private ulong _h;

        public long Value => (long)_h;

        public DrawKey Add(int v)
        {
            // splitmix64's finaliser over the running value: every input bit reaches every output bit.
            ulong h = _h + (uint)v + 0x9E3779B97F4A7C15UL;
            h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
            _h = h ^ (h >> 31);
            return this;
        }

        public DrawKey Add(bool v) => Add(v ? 1 : 0);

        public DrawKey Add(float v) => Add(v.GetHashCode());   // its bits

        public DrawKey Add(string s) => s == null ? Add(-1) : Add(s.Length).Add(s.GetHashCode());
    }
}
