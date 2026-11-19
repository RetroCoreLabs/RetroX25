//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RetroX25.Host
{
    /// <summary>
    /// A cap on the memory and CPU of the whole x25host process (<c>--max-memory-mb</c>, <c>--max-cpu-percent</c>),
    /// through a Windows job object. One process runs many machines, so the cap is for all of them together; the
    /// script limits inside each call (memory, time, statements) are separate and stay as they are.
    /// </summary>
    /// <remarks>
    /// Win32 (learn.microsoft.com, "Job Objects"): <c>CreateJobObjectW</c>; <c>SetInformationJobObject</c> with class 9,
    /// JobObjectExtendedLimitInformation, and <c>JOB_OBJECT_LIMIT_PROCESS_MEMORY</c> (0x100) with
    /// <c>ProcessMemoryLimit</c> in bytes: an allocation past it fails, which .NET reports as OutOfMemoryException;
    /// class 15, JobObjectCpuRateControlInformation, with <c>JOB_OBJECT_CPU_RATE_CONTROL_ENABLE</c> (1) and
    /// <c>HARD_CAP</c> (4), <c>CpuRate</c> in 1/100 of a percent of the whole machine. Then
    /// <c>AssignProcessToJobObject</c> on this process. Windows only: on another system the options are refused.
    /// </remarks>
    internal sealed class ProcessLimits : IDisposable
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const int JobObjectCpuRateControlInformation = 15;
        private const uint JobObjectLimitProcessMemory = 0x100;
        private const uint CpuRateControlEnable = 0x1;
        private const uint CpuRateControlHardCap = 0x4;

        private readonly SafeFileHandle _job;

        /// <summary>Makes a job with the limits; nothing is in it yet.</summary>
        /// <param name="maxMemoryMb">The most memory the process may commit, in MB; 0 for no cap.</param>
        /// <param name="maxCpuPercent">The most CPU, in percent of the whole machine (1 to 100); 0 for no cap.</param>
        /// <exception cref="PlatformNotSupportedException">Not Windows.</exception>
        /// <exception cref="Win32Exception">Windows refused the job or a limit.</exception>
        public ProcessLimits(int maxMemoryMb, int maxCpuPercent)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("--max-memory-mb and --max-cpu-percent need Windows (a job object)");
            }

            if (maxMemoryMb < 0 || maxCpuPercent < 0 || maxCpuPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCpuPercent), "memory 0 or more MB, CPU 0 to 100 percent");
            }

            _job = CreateJobObjectW(IntPtr.Zero, null);
            if (_job.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
            }

            MaxMemoryBytes = (ulong)maxMemoryMb * 1024UL * 1024UL;
            CpuRate = (uint)maxCpuPercent * 100U;
            if (MaxMemoryBytes > 0)
            {
                ExtendedLimit info = default;
                info.Basic.LimitFlags = JobObjectLimitProcessMemory;
                info.ProcessMemoryLimit = (UIntPtr)MaxMemoryBytes;
                if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<ExtendedLimit>()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject (memory)");
                }
            }

            if (CpuRate > 0)
            {
                CpuRateControl cpu = new CpuRateControl { ControlFlags = CpuRateControlEnable | CpuRateControlHardCap, CpuRate = CpuRate };
                if (!SetInformationJobObject(_job, JobObjectCpuRateControlInformation, ref cpu, Marshal.SizeOf<CpuRateControl>()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject (CPU)");
                }
            }
        }

        /// <summary>The memory cap in bytes; 0 for none.</summary>
        public ulong MaxMemoryBytes { get; }

        /// <summary>The CPU cap in 1/100 of a percent; 0 for none.</summary>
        public uint CpuRate { get; }

        /// <summary>Puts this process into the job; from then on the caps hold, until the process ends.</summary>
        /// <exception cref="Win32Exception">Windows refused.</exception>
        public void ApplyToThisProcess()
        {
            if (!AssignProcessToJobObject(_job, System.Diagnostics.Process.GetCurrentProcess().Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
            }
        }

        /// <summary>Reads the limits back from Windows (for the test, and the log at start).</summary>
        /// <returns>The memory cap in bytes (0 when off) and the CPU rate (0 when off).</returns>
        public (ulong MemoryBytes, uint CpuRate) Query()
        {
            ExtendedLimit info = default;
            ulong memory = QueryInformationJobObject(_job, JobObjectExtendedLimitInformation, out info, Marshal.SizeOf<ExtendedLimit>(), out _)
                && (info.Basic.LimitFlags & JobObjectLimitProcessMemory) != 0 ? (ulong)info.ProcessMemoryLimit : 0;
            CpuRateControl cpu = default;
            uint rate = QueryInformationJobObject(_job, JobObjectCpuRateControlInformation, out cpu, Marshal.SizeOf<CpuRateControl>(), out _)
                && (cpu.ControlFlags & CpuRateControlEnable) != 0 ? cpu.CpuRate : 0;
            return (memory, rate);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Closing the handle does not take the process out of the job; the caps hold until it ends.
            _job.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimit
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimit
        {
            public BasicLimit Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        // JOBOBJECT_CPU_RATE_CONTROL_INFORMATION: ControlFlags, then a union whose first member is CpuRate.
        [StructLayout(LayoutKind.Sequential)]
        private struct CpuRateControl
        {
            public uint ControlFlags;
            public uint CpuRate;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimit info, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref CpuRateControl info, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out ExtendedLimit info, int length, out int returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out CpuRateControl info, int length, out int returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    }
}
