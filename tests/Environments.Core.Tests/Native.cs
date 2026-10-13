// Fail closed if a test accidentally attempts to inspect or modify real OS state.
// WindowScanner and the planner are production sources; only their native boundary is replaced.
namespace Environments.Core;
internal static class Native
{
    public const uint TH32CS_SNAPPROCESS = 2;
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint th32ProcessID = 0, th32ParentProcessID = 0;
        public string? szExeFile = null;
        public PROCESSENTRY32() { }
    }
    public static Dictionary<uint, string> TestPaths { get; } = new();
    public static string? GetProcessPath(uint pid) => TestPaths.GetValueOrDefault(pid);
    public static bool ProcessIdToSessionId(uint pid, out uint session) { session = 0; return true; }
    private static Exception Unexpected() => new InvalidOperationException("Real process inspection is forbidden in planner tests");
    public static IntPtr CreateToolhelp32Snapshot(uint flags, uint pid) => throw Unexpected();
    public static bool Process32FirstW(IntPtr h, ref PROCESSENTRY32 p) => throw Unexpected();
    public static bool Process32NextW(IntPtr h, ref PROCESSENTRY32 p) => throw Unexpected();
    public static bool CloseHandle(IntPtr h) => throw Unexpected();
    public static bool EnumWindows(Func<IntPtr, IntPtr, bool> callback, IntPtr arg) => throw Unexpected();
    public static bool EnumChildWindows(IntPtr h, Func<IntPtr, IntPtr, bool> callback, IntPtr arg) => throw Unexpected();
    public static bool IsWindowVisible(IntPtr h) => throw Unexpected();
    public static IntPtr GetWindow(IntPtr h, uint cmd) => throw Unexpected();
    public static IntPtr GetWindowLongPtr(IntPtr h, int index) => throw Unexpected();
    public static bool IsCloaked(IntPtr h) => throw Unexpected();
    public static int GetWindowTextLength(IntPtr h) => throw Unexpected();
    public static uint GetWindowThreadProcessId(IntPtr h, out uint pid) { pid = 0; throw Unexpected(); }
    public static string GetTitle(IntPtr h) => throw Unexpected();
}
