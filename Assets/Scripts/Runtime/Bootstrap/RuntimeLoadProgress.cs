using System.Collections;
using System.Diagnostics;
using UnityDebug = UnityEngine.Debug;

namespace VVardenfell.Runtime.Bootstrap
{
    /// <summary>
    /// Lightweight runtime bootstrap progress model for the loading UI and staged
    /// bootstrap code paths. Tracks the active stage, per-stage item progress, and
    /// elapsed time for the current stage.
    /// </summary>
    public sealed class RuntimeLoadProgress
    {
        readonly Stopwatch _stageStopwatch = new();

        public string Stage { get; private set; } = "";
        public string Label { get; private set; } = "";
        public int Current { get; private set; }
        public int Total { get; private set; }
        public bool Done { get; private set; }
        public string Error { get; private set; }
        public long StageElapsedMs => _stageStopwatch.ElapsedMilliseconds;

        public float Fraction
        {
            get
            {
                if (Total <= 0)
                    return Done ? 1f : 0f;
                return Current <= 0 ? 0f : (float)Current / Total;
            }
        }

        public void Reset()
        {
            Stage = "";
            Label = "";
            Current = 0;
            Total = 0;
            Done = false;
            Error = null;
            _stageStopwatch.Reset();
        }

        public void BeginStage(string stage, string label, int total)
        {
            Stage = stage ?? "";
            Label = label ?? "";
            Current = 0;
            Total = total < 0 ? 0 : total;
            Done = false;
            Error = null;
            _stageStopwatch.Restart();
        }

        public void Report(string label, int current, int total = -1)
        {
            if (label != null)
                Label = label;
            Current = current < 0 ? 0 : current;
            if (total >= 0)
                Total = total;
        }

        public void CompleteStage(string label = null)
        {
            if (label != null)
                Label = label;
            if (Total > 0)
                Current = Total;
            _stageStopwatch.Stop();
            LogStageTiming("complete");
        }

        public void Complete(string stage = "Ready", string label = "Bootstrap complete")
        {
            Stage = stage ?? "";
            Label = label ?? "";
            if (Total <= 0)
            {
                Total = 1;
                Current = 1;
            }
            else
            {
                Current = Total;
            }

            Done = true;
            Error = null;
            _stageStopwatch.Stop();
            LogStageTiming("complete");
        }

        public void Fail(string error)
        {
            Error = error;
            Done = false;
            _stageStopwatch.Stop();
            LogStageTiming("fail");
        }

        void LogStageTiming(string result)
        {
            UnityDebug.Log(
                $"[VVardenfell][BootTiming] result={result} stage='{Stage}' label='{Label}' current={Current} total={Total} elapsedMs={_stageStopwatch.ElapsedMilliseconds}");
        }
    }

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
