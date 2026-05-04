using System;

namespace VVardenfell.Importer.Nif
{
    public static class NifEffectControllerAnalysis
    {
        public static float ResolveMaxControllerStopTime(NifFile nif)
        {
            if (nif?.Records == null)
                return 0f;

            float maxStopTime = 0f;
            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is not NiTimeController controller)
                    continue;

                float stopTime = controller.TimeStop;
                if (float.IsNaN(stopTime) || float.IsInfinity(stopTime))
                    throw new InvalidOperationException($"NIF '{nif.Path}' controller {i} has non-finite stop time {stopTime}.");

                if (stopTime > maxStopTime)
                    maxStopTime = stopTime;
            }

            return maxStopTime;
        }
    }
}
