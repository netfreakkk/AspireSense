using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using Path = System.Windows.Shapes.Path;

namespace AspireSense
{
    // ==========================================
    // ZERO-HOOK DIRECTX / D3D REAL-TIME TELEMETRY ENGINE
    // ==========================================
    public static class GameTelemetryEngine
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct WNODE_HEADER
        {
            [FieldOffset(0)] public uint BufferSize;
            [FieldOffset(4)] public uint ProviderId;
            [FieldOffset(8)] public ulong HistoricalContext;
            [FieldOffset(16)] public long TimeStamp;
            [FieldOffset(24)] public Guid Guid;
            [FieldOffset(40)] public uint ClientContext;
            [FieldOffset(44)] public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_LOGFILEW
        {
            public IntPtr LogFileName;
            public IntPtr LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE
        {
            public EVENT_INSTANCE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_INSTANCE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Anonymous1;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public IntPtr ClientContext;
            public uint EventSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public uint StartBuffers;
            public uint Reserved;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TIME_ZONE_INFORMATION
        {
            public int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string StandardName;
            public SYSTEMTIME StandardDate;
            public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DaylightName;
            public SYSTEMTIME DaylightDate;
            public int DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public IntPtr ExtendedData;
            public IntPtr UserData;
            public IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Explicit, Size = 80)]
        public struct EVENT_HEADER
        {
            [FieldOffset(0x00)] public ushort Size;
            [FieldOffset(0x02)] public ushort HeaderType;
            [FieldOffset(0x04)] public ushort Flags;
            [FieldOffset(0x06)] public ushort EventProperty;
            [FieldOffset(0x08)] public uint ThreadId;
            [FieldOffset(0x0C)] public uint ProcessId;
            [FieldOffset(0x10)] public long TimeStamp;
            [FieldOffset(0x18)] public Guid ProviderId;
            [FieldOffset(0x28)] public ushort EventId;
            [FieldOffset(0x2A)] public byte Version;
            [FieldOffset(0x2B)] public byte Channel;
            [FieldOffset(0x2C)] public byte Level;
            [FieldOffset(0x2D)] public byte Opcode;
            [FieldOffset(0x2E)] public ushort Task;
            [FieldOffset(0x30)] public ulong Keyword;
            [FieldOffset(0x38)] public uint KernelTime;
            [FieldOffset(0x3C)] public uint UserTime;
            [FieldOffset(0x38)] public ulong ProcessorTime;
            [FieldOffset(0x40)] public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void EventRecordCallback([In] ref EVENT_RECORD EventRecord);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint StartTrace(out ulong TraceHandle, string InstanceName, IntPtr Properties);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint ControlTrace(ulong TraceHandle, string InstanceName, IntPtr Properties, uint ControlCode);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint EnableTraceEx2(
            ulong TraceHandle,
            ref Guid ProviderId,
            uint ControlCode,
            byte Level,
            ulong MatchAnyKeyword,
            ulong MatchAllKeyword,
            uint Timeout,
            IntPtr EnableParameters
        );

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenTraceW(IntPtr Logfile);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint ProcessTrace(IntPtr[] HandleArray, uint HandleCount, IntPtr StartTime, IntPtr EndTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint CloseTrace(IntPtr TraceHandle);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool CloseHandle(IntPtr hObject);

        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_CONTROL_CODE_STOP_TRACE = 1;
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;

        private const string SessionName = "AspireSenseEtwSession";
        private static readonly Guid DxgiProviderGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9ProviderGuid = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");

        public static double CurrentFps = 0.0;
        public static double AvgFps = 0.0;
        public static double OnePercentLowFps = 0.0;
        public static double CurrentFrameTimeMs = 0.0;
        public static string ActiveProcessName = "Desktop Window Manager";
        public static uint ActiveProcessId = 0;
        public static bool IsEtwActive = false;

        public static bool IsGameActive
        {
            get
            {
                return CurrentFps >= 10.0 && !string.IsNullOrEmpty(ActiveProcessName) && !IsSystemProcess(ActiveProcessName);
            }
        }

        private static volatile uint targetGamePid = 0;
        private static ulong sessionHandle = 0;
        private static IntPtr consumerHandle = IntPtr.Zero;
        private static IntPtr pProperties = IntPtr.Zero;
        private static IntPtr pLogFile = IntPtr.Zero;
        private static IntPtr pLoggerName = IntPtr.Zero;
        private static EventRecordCallback callbackDelegate;
        private static Thread traceThread;
        private static bool isRunning = false;

        private static readonly object frameLock = new object();

        // High-performance circular ring buffer for zero-allocation frame metrics
        private const int RING_BUFFER_SIZE = 512;
        private static readonly double[] frameTimeRing = new double[RING_BUFFER_SIZE];
        private static int ringHead = 0;
        private static int ringCount = 0;

        private static readonly Queue<double> rollingFpsHistory = new Queue<double>();
        private static long lastFrameTimestamp = 0;
        private static long lastFrameWallClock = 0;
        private static bool wasInLoadingScreen = false;
        private static int selfPid = 0;
        private static bool exitHooked = false;

        public static void Start()
        {
            if (isRunning) return;
            isRunning = true;
            try
            {
                selfPid = Process.GetCurrentProcess().Id;
            }
            catch { }

            if (!exitHooked)
            {
                exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
            }

            traceThread = new Thread(RunEtwTrace);
            traceThread.IsBackground = true;
            traceThread.Name = "AspireSense-ETW";
            traceThread.Start();
        }

        private static void RunEtwTrace()
        {
            try
            {
                int propSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                int nameBytes = (SessionName.Length + 1) * 2;
                int allocSize = propSize + nameBytes + 256;
                pProperties = Marshal.AllocHGlobal(allocSize);

                for (int i = 0; i < allocSize; i++) Marshal.WriteByte(pProperties, i, 0);

                EVENT_TRACE_PROPERTIES props = new EVENT_TRACE_PROPERTIES();
                props.Wnode.BufferSize = (uint)allocSize;
                props.Wnode.Flags = 0x00020000; // WNODE_FLAG_TRACED_GUID
                props.Wnode.ClientContext = 1;  // QPC
                props.Wnode.Guid = Guid.NewGuid();
                props.BufferSize = 64;
                props.MinimumBuffers = 2;
                props.MaximumBuffers = 8;
                props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
                props.LoggerNameOffset = (uint)propSize;

                Marshal.StructureToPtr(props, pProperties, false);
                pLoggerName = Marshal.StringToHGlobalUni(SessionName);
                Marshal.Copy(SessionName.ToCharArray(), 0, IntPtr.Add(pProperties, propSize), SessionName.Length);

                // Stop any leftover kernel session from previous instance or crash
                ControlTrace(0, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);
                Thread.Sleep(20);

                uint status = StartTrace(out sessionHandle, SessionName, pProperties);
                if (status != 0)
                {
                    ControlTrace(0, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);
                    Thread.Sleep(50);
                    status = StartTrace(out sessionHandle, SessionName, pProperties);
                }

                if (status == 0 && sessionHandle != 0)
                {
                    IsEtwActive = true;

                    // Enable DXGI provider (Informational level, Present events)
                    Guid dxgi = DxgiProviderGuid;
                    EnableTraceEx2(sessionHandle, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);

                    // Enable D3D9 provider (Informational level, Present events)
                    Guid d3d9 = D3D9ProviderGuid;
                    EnableTraceEx2(sessionHandle, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);

                    // Allocate unmanaged memory for EVENT_TRACE_LOGFILEW so it persists during ProcessTrace
                    int logFileSize = Marshal.SizeOf(typeof(EVENT_TRACE_LOGFILEW));
                    pLogFile = Marshal.AllocHGlobal(logFileSize);
                    for (int i = 0; i < logFileSize; i++) Marshal.WriteByte(pLogFile, i, 0);

                    EVENT_TRACE_LOGFILEW logFile = new EVENT_TRACE_LOGFILEW();
                    logFile.LoggerName = pLoggerName;
                    logFile.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;

                    callbackDelegate = new EventRecordCallback(OnEventRecord);
                    logFile.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(callbackDelegate);

                    Marshal.StructureToPtr(logFile, pLogFile, false);

                    consumerHandle = OpenTraceW(pLogFile);
                    if (consumerHandle != IntPtr.Zero && consumerHandle != (IntPtr)(-1))
                    {
                        IntPtr[] handles = new IntPtr[] { consumerHandle };
                        ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else
                {
                    IsEtwActive = false;
                }
            }
            catch
            {
                IsEtwActive = false;
            }
        }

        private static void OnEventRecord([In] ref EVENT_RECORD record)
        {
            try
            {
                uint pid = record.EventHeader.ProcessId;
                if (pid == 0 || pid == (uint)selfPid) return;

                // Ultra fast-path filter: immediately drop events from non-target processes before doing any work
                uint currentTarget = targetGamePid;
                if (currentTarget != 0 && pid != currentTarget) return;

                ushort evId = record.EventHeader.EventId;
                Guid prov = record.EventHeader.ProviderId;

                bool isPresent = false;
                if (prov == DxgiProviderGuid)
                {
                    // ONLY listen to Present completion (Stop) events:
                    // 42 = DXGI Present_Stop
                    // 40 = PresentMultiplaneOverlay_Stop
                    // 44 = DXGITokenPresent_Stop
                    if (evId == 42 || evId == 40 || evId == 44)
                    {
                        isPresent = true;
                    }
                }
                else if (prov == D3D9ProviderGuid)
                {
                    // 2 = D3D9 Present_Stop
                    if (evId == 2)
                    {
                        isPresent = true;
                    }
                }

                if (isPresent)
                {
                    if (currentTarget == 0)
                    {
                        string autoName = GetProcessNameSafe((int)pid);
                        if (!IsSystemProcess(autoName))
                        {
                            targetGamePid = pid;
                            ActiveProcessId = pid;
                            ActiveProcessName = autoName;
                            currentTarget = pid;
                        }
                    }

                    if (currentTarget != 0 && pid == currentTarget)
                    {
                        long qpcTime = record.EventHeader.TimeStamp;
                        if (qpcTime <= 0)
                        {
                            qpcTime = Stopwatch.GetTimestamp();
                        }

                        lock (frameLock)
                        {
                            if (lastFrameTimestamp > 0)
                            {
                                long deltaTicks = qpcTime - lastFrameTimestamp;
                                double deltaMs = deltaTicks * 1000.0 / Stopwatch.Frequency;

                                // Deduplicate duplicate events for same frame within 0.5ms (e.g. swapchain + MPO overlay)
                                if (deltaMs >= -0.5 && deltaMs < 0.5)
                                {
                                    return;
                                }

                                if (deltaMs >= 0.5 && deltaMs <= 200.0)
                                {
                                    // Insert into circular ring buffer: O(1) time, 0 heap allocations
                                    frameTimeRing[ringHead] = deltaMs;
                                    ringHead = (ringHead + 1) % RING_BUFFER_SIZE;
                                    if (ringCount < RING_BUFFER_SIZE) ringCount++;
                                }
                            }

                            lastFrameTimestamp = qpcTime;
                            lastFrameWallClock = Stopwatch.GetTimestamp();
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetProcessNameSafe(int pid)
        {
            try
            {
                IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                        {
                            return System.IO.Path.GetFileName(sb.ToString());
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch { }

            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch { }

            return "Application (" + pid + ")";
        }

        private static bool IsSystemProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string low = name.ToLowerInvariant();
            return low.Contains("explorer") ||
                   low.Contains("taskmgr") ||
                   low.Contains("aspiresense") ||
                   low.Contains("firefox") ||
                   low.Contains("chrome") ||
                   low.Contains("msedge") ||
                   low.Contains("brave") ||
                   low.Contains("opera") ||
                   low.Contains("compattel") ||
                   low.Contains("wmiprvse") ||
                   low.Contains("devenv") ||
                   low.Contains("cmd.exe") ||
                   low.Contains("powershell") ||
                   low.Contains("conhost") ||
                   low.Contains("searchhost") ||
                   low.Contains("textinputhost") ||
                   low.Contains("shellexperiencehost") ||
                   low.Contains("startmenuexperiencehost") ||
                   low.Contains("applicationframehost") ||
                   low.Contains("gamebar") ||
                   low.Contains("gamebarftserver") ||
                   low.Contains("xbox") ||
                   low.Contains("notification") ||
                   low.Contains("actioncenter") ||
                   low.Contains("lockapp") ||
                   low.Contains("dwm");
        }

        public static void UpdateMetrics()
        {
            try
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid != 0 && pid != (uint)selfPid)
                    {
                        string pName = GetProcessNameSafe((int)pid);
                        // Only switch active game target if foreground is an actual application/game,
                        // NOT a system process, game bar, or AspireSense window
                        if (!IsSystemProcess(pName))
                        {
                            if (targetGamePid != pid)
                            {
                                lock (frameLock)
                                {
                                    ringHead = 0;
                                    ringCount = 0;
                                    rollingFpsHistory.Clear();
                                    lastFrameTimestamp = 0;
                                    lastFrameWallClock = 0;
                                    wasInLoadingScreen = false;
                                }
                            }
                            targetGamePid = pid;
                            ActiveProcessId = pid;
                            ActiveProcessName = pName;
                        }
                    }
                }
            }
            catch { }

            long now = Stopwatch.GetTimestamp();
            long timeoutLimit = (long)(Stopwatch.Frequency * 1.5);

            lock (frameLock)
            {
                // If no frame has arrived for 1.5 seconds, game is idle/paused/closed
                if (lastFrameWallClock == 0 || (now - lastFrameWallClock) > timeoutLimit)
                {
                    CurrentFps = 0.0;
                    AvgFps = 0.0;
                    OnePercentLowFps = 0.0;
                    CurrentFrameTimeMs = 0.0;
                    rollingFpsHistory.Clear();
                    wasInLoadingScreen = true;
                }
                else
                {
                    // 1. Current FPS: 1.0-second rolling window across recent frames in ring buffer
                    if (ringCount >= 4)
                    {
                        double sumMs = 0;
                        int sampleCount = 0;
                        for (int i = 0; i < ringCount; i++)
                        {
                            int idx = (ringHead - 1 - i + RING_BUFFER_SIZE) % RING_BUFFER_SIZE;
                            double frameMs = frameTimeRing[idx];
                            sumMs += frameMs;
                            sampleCount++;
                            if (sumMs >= 1000.0) // 1.0 second window of rendered frames
                            {
                                break;
                            }
                        }

                        double measuredFps = (sumMs > 0 && sampleCount > 0) ? (sampleCount * 1000.0 / sumMs) : 0.0;
                        CurrentFps = Math.Round(measuredFps, 0);
                        CurrentFrameTimeMs = (sampleCount > 0) ? Math.Round(sumMs / sampleCount, 1) : 0.0;

                        // 2. 1% Low FPS: Lowest 1% of FPS in a 4.0-second time window
                        List<double> windowFpsList = new List<double>(128);
                        double windowSumMs = 0;
                        for (int i = 0; i < ringCount; i++)
                        {
                            int idx = (ringHead - 1 - i + RING_BUFFER_SIZE) % RING_BUFFER_SIZE;
                            double frameMs = frameTimeRing[idx];
                            windowSumMs += frameMs;
                            if (frameMs >= 2.5 && frameMs <= 150.0) // legitimate gameplay frame times
                            {
                                windowFpsList.Add(1000.0 / frameMs);
                            }
                            if (windowSumMs >= 4000.0) // 4.0 second time window
                            {
                                break;
                            }
                        }

                        if (windowFpsList.Count >= 8)
                        {
                            windowFpsList.Sort(); // Ascending: lowest FPS first
                            int lowCount = Math.Max(1, (int)Math.Ceiling(windowFpsList.Count * 0.01));
                            double lowFpsSum = 0;
                            for (int i = 0; i < lowCount; i++)
                            {
                                lowFpsSum += windowFpsList[i];
                            }
                            double rawOneLow = Math.Round(lowFpsSum / lowCount, 0);

                            // 1% Low cannot realistically exceed Current FPS
                            rawOneLow = Math.Min(CurrentFps, rawOneLow);

                            // Smooth 1% Low with exponential moving average to prevent strobe jitter
                            if (OnePercentLowFps <= 0)
                            {
                                OnePercentLowFps = rawOneLow;
                            }
                            else
                            {
                                OnePercentLowFps = Math.Round(OnePercentLowFps * 0.65 + rawOneLow * 0.35, 0);
                            }
                        }
                        else
                        {
                            OnePercentLowFps = Math.Round(CurrentFps * 0.9, 0);
                        }

                        // 3. Average FPS: Protected against static loading screen spikes (> 260 FPS)
                        bool isLoadingScreen = (CurrentFps > 260.0);
                        if (isLoadingScreen)
                        {
                            wasInLoadingScreen = true;
                            AvgFps = CurrentFps;
                        }
                        else
                        {
                            if (wasInLoadingScreen)
                            {
                                // Clean transition from loading screen into gameplay!
                                rollingFpsHistory.Clear();
                                wasInLoadingScreen = false;
                            }

                            if (CurrentFps > 0)
                            {
                                rollingFpsHistory.Enqueue(CurrentFps);
                                if (rollingFpsHistory.Count > 60) // ~12 seconds rolling average
                                {
                                    rollingFpsHistory.Dequeue();
                                }
                                double avgSum = 0;
                                foreach (double s in rollingFpsHistory) avgSum += s;
                                AvgFps = Math.Round(avgSum / rollingFpsHistory.Count, 0);
                            }
                        }
                    }
                    else if (ringCount > 0)
                    {
                        int lastIdx = (ringHead - 1 + RING_BUFFER_SIZE) % RING_BUFFER_SIZE;
                        double lastMs = frameTimeRing[lastIdx];
                        CurrentFps = (lastMs > 0) ? Math.Round(1000.0 / lastMs, 0) : 0.0;
                        CurrentFrameTimeMs = Math.Round(lastMs, 1);
                        OnePercentLowFps = Math.Round(CurrentFps * 0.9, 0);
                        AvgFps = CurrentFps;
                    }
                }
            }
        }

        public static void Stop()
        {
            isRunning = false;
            try
            {
                if (consumerHandle != IntPtr.Zero && consumerHandle != (IntPtr)(-1))
                {
                    CloseTrace(consumerHandle);
                    consumerHandle = IntPtr.Zero;
                }
                if (sessionHandle != 0 && pProperties != IntPtr.Zero)
                {
                    ControlTrace(sessionHandle, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);
                    sessionHandle = 0;
                }
                if (pLogFile != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pLogFile);
                    pLogFile = IntPtr.Zero;
                }
                if (pLoggerName != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pLoggerName);
                    pLoggerName = IntPtr.Zero;
                }
                if (pProperties != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pProperties);
                    pProperties = IntPtr.Zero;
                }
            }
            catch { }
        }
    }
}
