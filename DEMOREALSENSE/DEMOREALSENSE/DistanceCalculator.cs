using System.Collections.Generic;

namespace DEMOREALSENSE
{
    public static class DistanceCalculator
    {
        // Médiane (raw) dans un carré (ex: 5x5 si radius=2)
        public static ushort MedianDepthRaw(ushort[] depth, int w, int h, int cx, int cy, int radius)
        {
            var vals = new List<ushort>((2 * radius + 1) * (2 * radius + 1));

            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;

                int row = y * w;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= w) continue;

                    ushort raw = depth[row + x];
                    if (raw != 0) vals.Add(raw);
                }
            }

            if (vals.Count == 0) return 0;
            vals.Sort();
            return vals[vals.Count / 2];
        }

        // Conversion raw depth -> (m, cm)
        public static (float meters, float cm) RawToMetersCm(ushort raw, float depthUnits)
        {
            float meters = raw * depthUnits;
            float cm = meters * 100f;
            return (meters, cm);
        }
    }
}