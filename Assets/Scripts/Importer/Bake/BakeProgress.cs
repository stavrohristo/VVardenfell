namespace VVardenfell.Importer.Bake
{
    /// <summary>Mutable progress report shared between the coordinator coroutine and the UI.</summary>
    public sealed class BakeProgress
    {
        public string Stage = "";
        public string Label = "";
        public int Current;
        public int Total;
        public bool Done;
        public string Error;

        public float Fraction => Total <= 0 ? 0f : UnityEngine.Mathf.Clamp01(Current / (float)Total);
    }
}
