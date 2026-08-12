using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BassesModManager
{
    public enum InjectionStatus
    {
        Success,
        AlreadyLoaded,
        GameNotRunning,
        DllNotFound,
        /// <summary>The file on disk is not the approved one and was not loaded.</summary>
        DllNotApproved,
        Failed
    }

    public struct InjectionResult
    {
        public InjectionStatus Status { get; }

        /// <summary>Technical detail for the <see cref="InjectionStatus.Failed"/> case; empty otherwise.</summary>
        public string Detail { get; }

        public InjectionResult(InjectionStatus status, string detail = "")
        {
            Status = status;
            Detail = detail;
        }
    }

    /// <summary>
    /// Loads a native DLL into an already running process. Kyber is a runtime library
    /// rather than patched game data, so unlike a .fbmod it cannot be applied on the way
    /// into the game - it has to be pushed into the process once the game is up.
    /// </summary>
    internal static class DllInjector
    {
        // Just enough rights to allocate, write and start a thread in the target
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint WAIT_OBJECT_0 = 0x0;

        // The remote LoadLibraryA call is a single file read; anything beyond this means
        // it is wedged, and we would rather report that than hang the UI thread forever
        private const uint RemoteCallTimeoutMs = 30000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        /// <summary>
        /// Injects <paramref name="dllPath"/> into the running <paramref name="processName"/>,
        /// but only if its contents hash to <paramref name="approvedSha256"/>. Safe to call
        /// when the game is not running or the DLL is already loaded - both come back as a
        /// status rather than an exception.
        /// </summary>
        public static InjectionResult Inject(string processName, string dllPath, string approvedSha256)
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                return new InjectionResult(InjectionStatus.DllNotFound);

            string fullPath = Path.GetFullPath(dllPath);
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length == 0)
                    return new InjectionResult(InjectionStatus.GameNotRunning);

                Process game = processes[0];

                // Held open across the whole check-and-inject. FileShare.Read still lets
                // the game map the file, but denies writes, renames and deletes - so the
                // DLL the game ends up loading is necessarily the one just hashed here,
                // with no window for a swapped file in between. Only the file path is
                // predictable, never its contents, which is what stops the app being used
                // to load something else that happens to be named Kyber.dll.
                using (FileStream approved = FileHash.OpenForReading(fullPath))
                {
                    if (!string.Equals(FileHash.Compute(approved), approvedSha256, StringComparison.OrdinalIgnoreCase))
                        return new InjectionResult(InjectionStatus.DllNotApproved);

                    if (IsModuleLoaded(game, fullPath))
                        return new InjectionResult(InjectionStatus.AlreadyLoaded);

                    return InjectInto(game.Id, fullPath);
                }
            }
            catch (Exception ex)
            {
                return new InjectionResult(InjectionStatus.Failed, ex.Message);
            }
            finally
            {
                foreach (Process p in processes)
                    p.Dispose();
            }
        }

        private static bool IsModuleLoaded(Process process, string dllPath)
        {
            string moduleName = Path.GetFileName(dllPath);
            try
            {
                return process.Modules.Cast<ProcessModule>()
                    .Any(m => string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Enumerating modules needs the same rights the injection itself does. If
                // it is refused here, let the injection attempt produce the real error
                // instead of guessing at one.
                return false;
            }
        }

        private static InjectionResult InjectInto(int processId, string dllPath)
        {
            // LoadLibraryA rather than LoadLibraryW: kernel32 sits at the same address in
            // every process of a session, so its address here is also its address in the
            // target, and the ANSI entry point keeps the argument a plain byte string.
            IntPtr loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            if (loadLibrary == IntPtr.Zero)
                return Fail("Could not locate LoadLibraryA");

            IntPtr process = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false, processId);
            if (process == IntPtr.Zero)
                return Fail("Could not open the game process");

            IntPtr remotePath = IntPtr.Zero;
            IntPtr remoteThread = IntPtr.Zero;
            try
            {
                byte[] pathBytes = Encoding.Default.GetBytes(dllPath + "\0");

                remotePath = VirtualAllocEx(process, IntPtr.Zero, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remotePath == IntPtr.Zero)
                    return Fail("Could not reserve memory in the game process");

                if (!WriteProcessMemory(process, remotePath, pathBytes, (uint)pathBytes.Length, out UIntPtr _))
                    return Fail("Could not write to the game process");

                remoteThread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remotePath, 0, IntPtr.Zero);
                if (remoteThread == IntPtr.Zero)
                    return Fail("Could not start the loader thread in the game process");

                if (WaitForSingleObject(remoteThread, RemoteCallTimeoutMs) != WAIT_OBJECT_0)
                    return new InjectionResult(InjectionStatus.Failed, "The game did not finish loading the file in time");

                // The thread returns whatever LoadLibraryA returned (truncated to 32 bits
                // by the thread exit code, which is fine - only zero/non-zero matters):
                // zero means the game refused to load the DLL.
                if (!GetExitCodeThread(remoteThread, out uint exitCode) || exitCode == 0)
                    return new InjectionResult(InjectionStatus.Failed, "The game rejected the file");

                return new InjectionResult(InjectionStatus.Success);
            }
            finally
            {
                if (remoteThread != IntPtr.Zero)
                    CloseHandle(remoteThread);
                if (remotePath != IntPtr.Zero)
                    VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
                CloseHandle(process);
            }
        }

        private static InjectionResult Fail(string what)
        {
            return new InjectionResult(InjectionStatus.Failed, $"{what} ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
        }
    }
}
