using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

namespace SampleApp
{
    internal static class Program
    {
        private const int MaxAttemptsForProcessPidResolving = 10;
        private const int MaxProcessIndexToTry = 40;
        private const int PidResolvingAttemptSpan = 15;
        private const string CategoryName = "Process";
        private const string PidCounterName = "ID Process";
        private const string PidResolvingCategoryName = "Process";

        public static void Main()
        {
            try
            {
                Console.WriteLine("Enter Process ID: ");

                var pidStr = Console.ReadLine();

                var pid = int.Parse(pidStr);

                var instanceName = ResolveInstanceName(pid);

                var counters = new[] {"ID Process", "% Processor Time", "Private Bytes"}
                    .Select(c => new PerformanceCounter("Process", c, instanceName))
                    .ToArray();

                byte stop = 0;

                Console.WriteLine("Press any key to stop...");
                new Thread(_ => ReadCounters(counters, ref stop)).Start();
                Console.ReadKey();
                Thread.VolatileWrite(ref stop, 1);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.GetType() + ": " + e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        private static void ReadCounters(PerformanceCounter[] counters, ref byte stop)
        {
            while (Thread.VolatileRead(ref stop) == 0)
            {
                foreach (var c in counters)
                {
                    try
                    {
                        var nextSample = c.NextSample();
                        Console.WriteLine(c.CounterName + ": " + nextSample.RawValue);
                    }
                    catch
                    {

                    }
                }
                Thread.Sleep(1000);
            }
        }

        private static string GetInstanceNameForProcess(int instanceCount, string instanceNameWithoutNumber, int pid)
        {
            var instanceName = instanceNameWithoutNumber;
            if (instanceCount > 0)
                instanceName += "#" + instanceCount;

            return GetInstanceNameForProcess(instanceName, pid);
        }

        private static string GetInstanceNameForProcess(string instanceName, int pid)
        {
            // Reader .NET CLR Memory Process ID for the given instance to check if
            // it does match our target process
            try
            {
                using (var counter = new PerformanceCounter(PidResolvingCategoryName, PidCounterName, instanceName,
                    true))
                {
                    long currentPid = 0;

                    for (var i = 0; i < MaxAttemptsForProcessPidResolving; ++i)
                    {
                        var sample = counter.NextSample();
                        currentPid = sample.RawValue;

                        // for some reason it takes quite a while until the counter is
                        // updated with the correct data
                        if (currentPid > 0)
                            break;

                        Thread.Sleep(PidResolvingAttemptSpan);
                    }
                    return (currentPid == pid) ? instanceName : null;
                }
            }
            catch (Exception) // swallow exceptions from non existing instances we tried to read
            {
                return null;
            }
        }

        private static string Get64BitProcessExecutablePathFrom32(Process process)
        {
            var query = "SELECT ExecutablePath, ProcessID FROM Win32_Process";
            var searcher = new ManagementObjectSearcher(query);
            var pidStr = process.Id.ToString();

            foreach (var item in searcher.Get())
            {
                var id = item["ProcessID"];
                var path = item["ExecutablePath"];

                if (path != null && id.ToString() == pidStr)
                    return path.ToString();
            }

            return string.Empty;
        }

        private static string ProcessExecutablePath(int pid)
        {
            var process = Process.GetProcessById(pid);

            try
            {
                // if we are 32 and process is 64 will throw "can not list modules of 64 process from 32 bit application"
                return process.MainModule.FileName;
            }
            catch
            {
                return Get64BitProcessExecutablePathFrom32(process);
            }
        }

        private static string ResolveInstanceName(int pid)
        {
            string instanceName = null;
            try
            {
                const string namePidFormatString = "{0}_{1}";

                var executablePath = ProcessExecutablePath(pid);
                var processName = Path.GetFileNameWithoutExtension(executablePath) ?? string.Empty;

                instanceName = GetInstanceNameForProcess(processName, pid);
                if (instanceName != null)
                    return instanceName;

                instanceName = GetInstanceNameForProcess(string.Format(namePidFormatString, processName, pid), pid);
                if (instanceName != null)
                    return instanceName;

                // for some unknown reason instance names got truncated to 64 symbols.
                var instanceNameWithoutIndex = processName.Length > 64 ? processName.Substring(0, 64) : processName;
                instanceName = GetInstanceNameForProcess(
                    string.Format(namePidFormatString, instanceNameWithoutIndex, pid), pid);
                if (instanceName != null)
                    return instanceName;

                for (var i = 0; i < MaxProcessIndexToTry; i++)
                {
                    instanceName = GetInstanceNameForProcess(i, instanceNameWithoutIndex, pid);
                    if (instanceName != null)
                        return instanceName;
                }
            }
            catch (Exception oldException)
            {
                var ex = new InvalidOperationException("Can not resolve instance name", oldException);
                throw ex;
            }
            finally
            {
                if (instanceName != null)
                    Console.WriteLine(
                        "[SingleCategoryPerformanceCountersFactoryBase] Successfully resolved instance name for pid: " +
                        pid +
                        " and category " + CategoryName + ". Instance name: " + instanceName);
            }

            // this code is called if there is no "return" from try - instanceName = null
            Console.WriteLine(
                "[SingleCategoryPerformanceCountersFactoryBase] Failed to resolve instance name for pid: " + pid +
                " and category " + CategoryName);
            var exception = new InvalidOperationException("Can not resolve instance name");
            throw exception;
        }

    }
}