using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;

namespace SolutionRunner.ToolWindows
{
    public class WindowHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(IntPtr idAttach, IntPtr idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);


        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static void BringProcessToFront(Process process)
        {
            IntPtr hWnd = process.MainWindowHandle;

            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            // Restore if the window is minimized
            ShowWindow(hWnd, SW_RESTORE);

            // Attach threads to bring window to foreground
            IntPtr foregroundWindowThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            IntPtr appThread = GetWindowThreadProcessId(hWnd, out _);

            if (foregroundWindowThreadId != appThread)
            {
                AttachThreadInput(foregroundWindowThreadId, appThread, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(foregroundWindowThreadId, appThread, false);
            }
            else
            {
                SetForegroundWindow(hWnd);
            }

            // Resize the window to 2/3 of the screen width and height
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            int newWidth = (screenWidth * 2) / 3;
            int newHeight = (screenHeight * 2) / 3;

            // Center the window on the screen
            int x = (screenWidth - newWidth) / 2;
            int y = (screenHeight - newHeight) / 2;

            MoveWindow(hWnd, x, y, newWidth, newHeight, true);
        }


        public static int GetParentProcessId(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
                var projectManagementObject = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                return projectManagementObject != null ? Convert.ToInt32(projectManagementObject["ParentProcessId"]) : -1;
            }
            catch (Exception)
            {
                // Handle any exceptions that occur during the management query
            }

            return -1; // Return an invalid process ID if something goes wrong
        }

        public static void MinimizeAllProcessesWindows(IEnumerable<string> projectsNames = null)
        {
            foreach (var process in Process.GetProcessesByName("VsDebugConsole"))
            {
                MinimizeProcessWindow(process);
            }

            if (projectsNames != null)
            {
                foreach (var projectName in projectsNames)
                {
                    foreach (var process in Process.GetProcessesByName(projectName))
                        MinimizeProcessWindow(process);
                }
            }
        }

        private static void MinimizeProcessWindow(Process process)
        {
            var currentProcess = Process.GetCurrentProcess().Id;

            var parentProcessId = GetParentProcessId(process.Id);
            if (parentProcessId == currentProcess)
            {
                IntPtr hWnd = process.MainWindowHandle;
                if (hWnd == IntPtr.Zero) return;

                HideWindowFromTaskBarAndMinimize(hWnd);
            }
        }

        public static void HideWindowFromTaskBarAndMinimize(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
            {
                // Get the current extended window styles
                IntPtr extendedStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);

                // Remove the WS_EX_APPWINDOW style and add WS_EX_TOOLWINDOW to hide it from the task bar
                IntPtr newStyle = new((extendedStyle.ToInt64() & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
                SetWindowLongPtr(hWnd, GWL_EXSTYLE, newStyle);

                // Force the window to update its styles
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);

                // Minimize the window
                ShowWindow(hWnd, SW_MINIMIZE);
            }
        }

        public static void BringAllProcessesToFrontAndArrangeSideBySide()
        {
            var currentProcessId = Process.GetCurrentProcess().Id;
            var processes = Process.GetProcessesByName("VsDebugConsole")
                .Where(i => GetParentProcessId(i.Id) == currentProcessId)
                .ToList();

            if (processes.Count == 0)
                return;

            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            int numProcesses = processes.Count;

            int maxColumns, numRows;

            if (numProcesses <= 3)
            {
                maxColumns = 2;
                numRows = 2;
            }
            else if (numProcesses <= 5)
            {
                maxColumns = 3;
                numRows = 2;
            }
            else if (numProcesses <= 8)
            {
                maxColumns = 4;
                numRows = 2;
            }
            else
            {
                maxColumns = (int)Math.Ceiling(numProcesses / 3.0);
                numRows = 3;
            }

            int windowWidth = screenWidth / maxColumns;
            int windowHeight = screenHeight / numRows;

            // Position each window in the calculated grid
            for (int i = 0; i < numProcesses; i++)
            {
                var process = processes[i];
                IntPtr hWnd = process.MainWindowHandle;

                if (hWnd != IntPtr.Zero)
                {
                    int row = i / maxColumns;
                    int column = i % maxColumns;

                    int x = column * windowWidth;
                    int y = row * windowHeight;

                    ShowWindow(hWnd, SW_RESTORE);

                    SetWindowPos(hWnd, IntPtr.Zero, x, y, windowWidth, windowHeight,
                                 SWP_NOZORDER | SWP_NOACTIVATE);
                }
            }

            // Bring each window to the foreground
            for (int i = numProcesses - 1; i >= 0; i--)
            {
                var process = processes[i];
                IntPtr hWnd = process.MainWindowHandle;

                if (hWnd != IntPtr.Zero)
                {
                    SetForegroundWindow(hWnd);
                }
            }
        }

    }
}