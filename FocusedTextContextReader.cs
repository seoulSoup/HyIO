using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace HyIO
{
    internal sealed class SlashCommandContext
    {
        public string RawText { get; init; } = string.Empty;
        public string SlashText { get; init; } = string.Empty;
        public string CommandText { get; init; } = string.Empty;
        public Point AnchorScreenPoint { get; init; }
        public IntPtr TargetWindowHandle { get; init; }
    }

    internal static class FocusedTextContextReader
    {
        private const int MaxCommandLength = 32;
        private const int ReadTimeoutMilliseconds = 120;

        private static readonly Regex SlashCommandRegex =
            new(@"(?:^|\s)/(?<command>[^\s/]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryReadSlashCommand(out SlashCommandContext context)
        {
            context = null;

            SlashCommandContext resolvedContext = null;
            Exception capturedException = null;
            using var completed = new ManualResetEventSlim(false);

            var worker = new Thread(() =>
            {
                try
                {
                    if (TryReadSlashCommandCore(out var innerContext))
                    {
                        resolvedContext = innerContext;
                    }
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    completed.Set();
                }
            });

            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();

            if (!completed.Wait(ReadTimeoutMilliseconds))
            {
                return false;
            }

            if (capturedException != null)
            {
                return false;
            }

            context = resolvedContext;
            return context != null;
        }

        private static bool TryReadSlashCommandCore(out SlashCommandContext context)
        {
            context = null;

            try
            {
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement == null)
                    return false;

                var textBeforeCaret = TryGetTextBeforeCaret(focusedElement);
                if (string.IsNullOrWhiteSpace(textBeforeCaret))
                    return false;

                var match = SlashCommandRegex.Match(textBeforeCaret);
                if (!match.Success)
                    return false;

                var commandText = match.Groups["command"].Value.Trim();
                if (commandText.Length > MaxCommandLength)
                {
                    commandText = commandText.Substring(0, MaxCommandLength);
                }

                var targetWindowHandle = GetForegroundWindow();

                if (!TryGetCaretAnchorPoint(out var anchorPoint))
                {
                    var fallbackRect = focusedElement.Current.BoundingRectangle;
                    if (fallbackRect.IsEmpty)
                        return false;

                    anchorPoint = new Point(
                        fallbackRect.Left + Math.Min(48, fallbackRect.Width / 2),
                        fallbackRect.Top + Math.Min(24, fallbackRect.Height / 2));
                }

                context = new SlashCommandContext
                {
                    RawText = match.Value,
                    SlashText = "/" + match.Groups["command"].Value,
                    CommandText = commandText,
                    AnchorScreenPoint = anchorPoint,
                    TargetWindowHandle = targetWindowHandle
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string TryGetTextBeforeCaret(AutomationElement element)
        {
            if (element == null)
                return string.Empty;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                try
                {
                    return textPattern.DocumentRange.GetText(-1) ?? string.Empty;
                }
                catch
                {
                }
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
                valuePatternObject is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool TryGetCaretAnchorPoint(out Point anchorPoint)
        {
            anchorPoint = default;

            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return false;

            var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
            if (threadId == 0)
                return false;

            var guiInfo = new GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            if (!GetGUIThreadInfo(threadId, ref guiInfo))
                return false;

            if (guiInfo.hwndCaret == IntPtr.Zero)
                return false;

            var caretTopLeft = new POINT
            {
                X = guiInfo.rcCaret.Left,
                Y = guiInfo.rcCaret.Top
            };

            var caretBottomRight = new POINT
            {
                X = guiInfo.rcCaret.Right,
                Y = guiInfo.rcCaret.Bottom
            };

            if (!ClientToScreen(guiInfo.hwndCaret, ref caretTopLeft))
                return false;

            if (!ClientToScreen(guiInfo.hwndCaret, ref caretBottomRight))
                return false;

            anchorPoint = new Point(
                caretTopLeft.X + Math.Max(8, (caretBottomRight.X - caretTopLeft.X) / 2.0),
                caretTopLeft.Y);

            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }
    }
}
