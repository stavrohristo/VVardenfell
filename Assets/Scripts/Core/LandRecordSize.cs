namespace VVardenfell.Core
{
    /// <summary>
    /// Shape constants for one Morrowind exterior cell. Kept separate from
    /// <c>LandRecord</c> (which lives in the importer assembly) so runtime code
    /// can reference the sizes without pulling in the ESM parser.
    /// </summary>
    public static class LandRecordSize
    {
        /// <summary>Edge length of one exterior cell, in Morrowind world units.</summary>
        public const int CellUnitsMw = 8192;

        /// <summary>Vertices per cell edge (the LAND heightmap is 65×65).</summary>
        public const int VertsPerEdge = 65;

        /// <summary>VTEX grid side — splatmap resolution per cell (16×16 quadrants).</summary>
        public const int VtexEdge = 16;
    }
}
