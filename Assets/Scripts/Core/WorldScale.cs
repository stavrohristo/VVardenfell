namespace VVardenfell.Core
{
    /// <summary>
    /// Conversion between Morrowind world units and Unity meters.
    /// Bethesda's unit in TES3 is ~1/70 of a meter (close to an inch).
    /// </summary>
    public static class WorldScale
    {
        public const float MwUnitsToMeters = 1f / 70f;
    }
}
