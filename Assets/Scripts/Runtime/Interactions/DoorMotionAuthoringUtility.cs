using System;
using System.Globalization;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Interactions
{
    static class DoorMotionAuthoringUtility
    {
        const float OpenMwDoorRangeDegrees = 90f;
        const float OpenMwDoorSpeedDegreesPerSecond = 90f;
        const byte OpenMwDoorAxis = 2;

        public static DoorMotionState BuildOpenMwDoorMotion()
        {
            return new DoorMotionState
            {
                Progress = 0f,
                TargetProgress = 0f,
                RangeRadians = math.radians(OpenMwDoorRangeDegrees),
                SpeedRadiansPerSecond = math.radians(OpenMwDoorSpeedDegreesPerSecond),
                Axis = OpenMwDoorAxis,
            };
        }

        public static bool TryBuild(RuntimeContentDatabase contentDb, string scriptId, out DoorMotionState state)
        {
            state = default;
            if (contentDb == null || string.IsNullOrWhiteSpace(scriptId))
                return false;

            if (!contentDb.TryGetScriptHandle(scriptId, out GenericRecordDefHandle scriptHandle) || !scriptHandle.IsValid)
                return false;

            string text = contentDb.GetScript(scriptHandle).Text ?? string.Empty;
            if (text.IndexOf("onactivate", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (!TryFindReversibleRotate(text, out byte axis, out float speedDegrees, out float durationSeconds))
                return false;

            float rangeDegrees = MathF.Abs(speedDegrees) * durationSeconds;
            if (rangeDegrees <= 0f)
                return false;

            state = new DoorMotionState
            {
                Progress = 0f,
                TargetProgress = 0f,
                RangeRadians = math.radians(rangeDegrees),
                SpeedRadiansPerSecond = math.radians(MathF.Abs(speedDegrees)),
                Axis = axis,
            };
            return true;
        }

        static bool TryFindReversibleRotate(string text, out byte axis, out float speedDegrees, out float durationSeconds)
        {
            axis = 0;
            speedDegrees = 0f;
            durationSeconds = 0f;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            float lastTimerLessThan = 0f;
            bool hasTimerLimit = false;
            bool foundPositive = false;
            bool foundNegative = false;
            byte positiveAxis = 0;
            float positiveSpeed = 0f;
            float positiveDuration = 0f;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0)
                    continue;

                if (TryParseTimerLessThan(line, out float timerLimit))
                {
                    lastTimerLessThan = timerLimit;
                    hasTimerLimit = true;
                }

                if (!TryParseRotate(line, out byte rotateAxis, out float rotateSpeed))
                    continue;

                if (rotateSpeed > 0f && hasTimerLimit)
                {
                    foundPositive = true;
                    positiveAxis = rotateAxis;
                    positiveSpeed = rotateSpeed;
                    positiveDuration = lastTimerLessThan;
                    continue;
                }

                if (rotateSpeed < 0f && foundPositive && rotateAxis == positiveAxis)
                    foundNegative = true;
            }

            if (!foundPositive || !foundNegative)
                return false;

            axis = positiveAxis;
            speedDegrees = positiveSpeed;
            durationSeconds = positiveDuration;
            return durationSeconds > 0f;
        }

        static bool TryParseTimerLessThan(string line, out float value)
        {
            value = 0f;
            int timerIndex = line.IndexOf("timer", StringComparison.OrdinalIgnoreCase);
            if (timerIndex < 0)
                return false;

            int lessIndex = line.IndexOf('<', timerIndex);
            if (lessIndex < 0)
                return false;

            string number = ReadNumber(line, lessIndex + 1);
            return number.Length > 0 && float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static bool TryParseRotate(string line, out byte axis, out float speed)
        {
            axis = 0;
            speed = 0f;
            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3 || !tokens[0].Equals("rotate", StringComparison.OrdinalIgnoreCase))
                return false;

            string axisText = tokens[1].Trim().Trim(',');
            if (!TryMapAxis(axisText, out axis))
                return false;

            string speedText = tokens[2].Trim().Trim(',');
            return float.TryParse(speedText, NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
        }

        static bool TryMapAxis(string text, out byte axis)
        {
            if (text.Equals("x", StringComparison.OrdinalIgnoreCase))
            {
                axis = 0;
                return true;
            }

            if (text.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                axis = 1;
                return true;
            }

            if (text.Equals("z", StringComparison.OrdinalIgnoreCase))
            {
                axis = 2;
                return true;
            }

            axis = 0;
            return false;
        }

        static string StripComment(string line)
        {
            int comment = line.IndexOf(';');
            return comment < 0 ? line : line.Substring(0, comment);
        }

        static string ReadNumber(string text, int start)
        {
            while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] == '='))
                start++;

            int end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-' || text[end] == '+'))
                end++;

            return end <= start ? string.Empty : text.Substring(start, end - start);
        }
    }
}
