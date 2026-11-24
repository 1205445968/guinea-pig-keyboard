using System;

namespace WheekKeys
{
    internal sealed class MouthAnimation
    {
        private static readonly int[] Episode = { 0, 1, 2, 3, 2, 1, 0, 0, 1, 2, 3, 2, 1, 0 };
        private readonly Random random = new Random(Guid.NewGuid().GetHashCode());
        private double nextAt;
        private double episodeAt;
        private bool active;

        public int Frame { get; private set; }

        public void Reset(double now)
        {
            Frame = 0;
            active = false;
            nextAt = now + 3500 + random.NextDouble() * 3000;
        }

        public void Trigger(double now)
        {
            Frame = 0;
            active = true;
            episodeAt = now;
        }

        public bool Advance(double now, bool blocked)
        {
            int before = Frame;
            if (blocked)
            {
                Frame = 0;
                active = false;
                nextAt = Math.Max(nextAt, now + 3000);
            }
            else
            {
                if (!active && now >= nextAt) Trigger(now);
                if (active)
                {
                    int index = (int)((now - episodeAt) / 70);
                    if (index >= Episode.Length)
                    {
                        Frame = 0;
                        active = false;
                        nextAt = now + 6000 + random.NextDouble() * 5000;
                    }
                    else if (index >= 0) Frame = Episode[index];
                }
            }
            return Frame != before;
        }
    }
}
