using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Verse;

namespace TotallyWorking
{
    // Swaps the game window's title and icon while the overlay is on (Windows only), and restores them when it turns off.
    // Works on the window through Win32 from inside the game process; the game files are not touched.
    public static class WindowDisguise
    {
        private const uint WM_GETICON = 0x007F;
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const uint GW_OWNER = 4;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr hWnd, string text);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

        private static IntPtr window;
        private static string originalTitle;
        private static IntPtr originalBig, originalSmall;
        private static IntPtr disguiseBig, disguiseSmall;
        private static string appliedTitle;
        private static bool failed;

        public static bool Applied => appliedTitle != null;

        public static void Apply(string title)
        {
            if (failed || !IsWindows || appliedTitle == title)
                return;
            try
            {
                if (!Applied)
                {
                    if (window == IntPtr.Zero)
                        window = FindGameWindow();
                    if (window == IntPtr.Zero)
                        return;
                    var sb = new StringBuilder(256);
                    GetWindowTextW(window, sb, sb.Capacity);
                    originalTitle = sb.ToString();
                    originalBig = SendMessageW(window, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
                    originalSmall = SendMessageW(window, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
                    if (disguiseBig == IntPtr.Zero)
                        LoadNotepadIcon();
                    if (disguiseBig != IntPtr.Zero)
                    {
                        SendMessageW(window, WM_SETICON, (IntPtr)ICON_BIG, disguiseBig);
                        SendMessageW(window, WM_SETICON, (IntPtr)ICON_SMALL, disguiseSmall != IntPtr.Zero ? disguiseSmall : disguiseBig);
                    }
                }
                SetWindowTextW(window, title);
                appliedTitle = title;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public static void Restore()
        {
            if (!Applied)
                return;
            try
            {
                SetWindowTextW(window, originalTitle ?? "RimWorld");
                // A zero icon falls back to the window class icon, which is what the game started with.
                SendMessageW(window, WM_SETICON, (IntPtr)ICON_BIG, originalBig);
                SendMessageW(window, WM_SETICON, (IntPtr)ICON_SMALL, originalSmall);
            }
            catch (Exception e)
            {
                Fail(e);
            }
            appliedTitle = null;
        }

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static IntPtr FindGameWindow()
        {
            uint pid = (uint)Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint owner);
                if (owner != pid || !IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static void LoadNotepadIcon()
        {
            foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
            {
                var path = Path.Combine(dir, "notepad.exe");
                if (File.Exists(path) && ExtractIconExW(path, 0, out disguiseBig, out disguiseSmall, 1) > 0 && disguiseBig != IntPtr.Zero)
                    return;
            }
        }

        private static void Fail(Exception e)
        {
            failed = true;
            Log.Warning("[TotallyWorking] Window title/icon change disabled: " + e.Message);
        }
    }
}
