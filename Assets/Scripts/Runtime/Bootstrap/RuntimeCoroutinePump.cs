using System.Collections;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class RuntimeCoroutinePump
    {
        public static void RunToCompletion(IEnumerator routine)
        {
            if (routine == null)
                return;

            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                    RunToCompletion(nested);
            }
        }
    }
}
