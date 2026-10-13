namespace DragonShelterDragonNeedsV3
{
    internal static class FullValuePolicy
    {
        internal static bool TryMaximum(float min, float max, out float target)
        {
            target = max;
            return Finite(min) && Finite(max) && max > min;
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

