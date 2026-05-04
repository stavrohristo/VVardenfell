namespace VVardenfell.Importer.Nif
{
    public static class NifObjectAnimationAnalysis
    {
        public static bool HasSupportedObjectAnimation(NifFile nif)
        {
            if (nif?.Records == null)
                return false;

            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is NiKeyframeController or NiVisController)
                    return true;
                if (nif.Records[i] is NiTextKeyExtraData textKeys && textKeys.Keys != null && textKeys.Keys.Length > 0)
                    return true;
            }

            return false;
        }

        public static bool HasUnsupportedObjectControllers(NifFile nif)
        {
            if (nif?.Records == null)
                return false;

            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is NiTimeController
                    and not NiKeyframeController
                    and not NiVisController
                    && !NifVfxEffectExtractor.IsVfxSupportedObjectController(nif.Records[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
