using System;
using System.Collections;
using UnityEngine;

namespace PSMcp.Mod
{
    // Hold gestures take real, unscaled time. A disappearing/occluded target cancels the gesture.
    internal static class NativeInput
    {
        private static MonoBehaviour? runner;
        private static int generation;
        public static bool Busy { get; private set; }
        public static void Initialize(MonoBehaviour value) { runner = value; Busy = false; }
        public static void Cancel() { generation++; Busy = false; }
        public static void Reset() { Cancel(); runner = null; }

        public static void Select(SelectableElement element)
        {
            if (element.isSelected) element.Deselect(); else element.Select();
        }

        public static void Hold(float seconds, Func<bool> valid, Action press, Action release, Action cancel)
        {
            if (Busy || runner == null) throw new InvalidOperationException("Native input is busy or unavailable.");
            Busy = true;
            runner.StartCoroutine(Gesture(++generation, Mathf.Clamp(seconds, 0.05f, 30f), valid, press, release, cancel));
        }

        private static IEnumerator Gesture(int ticket, float seconds, Func<bool> valid, Action press, Action release, Action cancel)
        {
            try
            {
                if (ticket != generation || !valid()) yield break;
                press();
                var started = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - started < seconds)
                {
                    if (ticket != generation || !valid()) yield break;
                    yield return null;
                }
                if (ticket == generation && valid()) release();
            }
            finally
            {
                try { cancel(); } finally { if (ticket == generation) Busy = false; }
            }
        }
    }
}
