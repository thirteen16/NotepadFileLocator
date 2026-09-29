using System;

namespace NotepadFileLocator
{
    /// <summary>
    /// Recognizes two physical Escape presses in the same eligible window.
    /// The caller supplies a monotonic millisecond clock and suppresses a
    /// KeyDown only when this class returns true.
    /// </summary>
    internal sealed class EscapeGesture
    {
        private const int Escape = 0x1B;
        private readonly int intervalMilliseconds;
        private bool escapeIsDown;
        private bool hasFirstPress;
        private long firstPressMilliseconds;
        private long firstWindowId;

        public EscapeGesture(int intervalMilliseconds = 450)
        {
            if (intervalMilliseconds <= 0)
                throw new ArgumentOutOfRangeException("intervalMilliseconds");
            this.intervalMilliseconds = intervalMilliseconds;
        }

        public bool KeyDown(int virtualKey, long nowMs, long windowId,
                            bool eligible, bool modified, bool injected)
        {
            if (injected)
                return false;

            if (virtualKey != Escape)
            {
                ClearPendingPress();
                return false;
            }

            bool wasDown = escapeIsDown;
            escapeIsDown = true;

            if (!eligible || modified || windowId == 0)
            {
                ClearPendingPress();
                return false;
            }

            // A repeat must never count as a second physical press. It can
            // still reveal that the user has moved to a different window.
            if (wasDown)
            {
                if (hasFirstPress && windowId != firstWindowId)
                    ClearPendingPress();
                return false;
            }

            long elapsed = unchecked(nowMs - firstPressMilliseconds);
            if (hasFirstPress && windowId == firstWindowId &&
                nowMs >= firstPressMilliseconds && elapsed >= 0 &&
                elapsed <= intervalMilliseconds)
            {
                ClearPendingPress();
                return true;
            }

            // An expired press, changed window, or restarted clock begins a
            // fresh pair. A separate flag makes timestamp zero a valid input.
            hasFirstPress = true;
            firstPressMilliseconds = nowMs;
            firstWindowId = windowId;
            return false;
        }

        public void KeyUp(int virtualKey, bool injected)
        {
            if (injected)
                return;

            if (virtualKey == Escape)
                escapeIsDown = false;
            else
                ClearPendingPress();
        }

        public void Reset()
        {
            escapeIsDown = false;
            ClearPendingPress();
        }

        /// <summary>
        /// Cancels a pending pair after a foreground change or completed
        /// action, preserving physical key state so repeats remain ignored.
        /// </summary>
        public void CancelPending()
        {
            ClearPendingPress();
        }

        private void ClearPendingPress()
        {
            hasFirstPress = false;
            firstPressMilliseconds = 0;
            firstWindowId = 0;
        }
    }
}
