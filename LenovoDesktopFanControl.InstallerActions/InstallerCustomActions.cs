using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace LenovoDesktopFanControl.InstallerActions;

public static class InstallerCustomActions
{
    private const string StopApplicationEventName =
        @"Local\LenovoDesktopFanControl.StopApplication";
    private const string ApplicationMutexName =
        @"Local\LenovoDesktopFanControl";
    private const string StopLightingEventName =
        @"Local\LenovoDesktopFanControl.StopLightingBackground";
    private const string LightingHostMutexName =
        @"Local\LenovoDesktopFanControl.LightingBackground";
    private const string LegacyTaskName = "Lenovo Desktop Fan Control";
    private const string LegacyRunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValue = "LenovoDesktopFanControl";
    private static readonly TimeSpan ApplicationStopTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(10);

    [CustomAction]
    public static ActionResult StopApplicationAndLightingHost(Session session)
    {
        try
        {
            SignalAndWaitForMutex(
                StopApplicationEventName,
                ApplicationMutexName,
                ApplicationStopTimeout);
            SignalAndWaitForMutex(
                StopLightingEventName,
                LightingHostMutexName,
                HostStopTimeout);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Unable to stop the application or lighting host: {ex}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult RemoveLightingStartup(Session session)
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? session["UserSID"];
            DeleteTaskIfPresent(GetCurrentTaskName(sid));
            DeleteTaskIfPresent(LegacyTaskName);
            using var runKey = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            runKey?.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Unable to remove lighting startup: {ex}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult StartLightingHost(Session session)
    {
        try
        {
            var taskName = GetCurrentTaskName(session["UserSID"]);
            if (!RunTaskIfPresent(taskName))
                RunTaskIfPresent(LegacyTaskName);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"Unable to start lighting background host: {ex}");
            return ActionResult.Failure;
        }
    }

    private static void SignalAndWaitForMutex(
        string eventName,
        string mutexName,
        TimeSpan timeout)
    {
        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(eventName);
            stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The corresponding process is not running.
        }

        try
        {
            using var mutex = Mutex.OpenExisting(mutexName);
            try
            {
                if (!mutex.WaitOne(timeout))
                    throw new TimeoutException($"Timed out waiting for '{mutexName}' to be released.");
                mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                // The process terminated without releasing the mutex.
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // It stopped between the event signal and mutex lookup.
        }
    }

    private static string GetCurrentTaskName(string sid) =>
        $"Lenovo Desktop Fan Control - {sid}";

    private static bool RunTaskIfPresent(string taskName)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        try
        {
            dynamic task = folder.GetTask(taskName);
            task.Run(null);
            return true;
        }
        catch (COMException ex) when (IsFileNotFound(ex))
        {
            // StartWithWindows is disabled, so there is no task to run.
            return false;
        }
    }

    private static void DeleteTaskIfPresent(string taskName)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        try
        {
            dynamic task = folder.GetTask(taskName);
            try
            {
                task.Stop(0);
            }
            catch (COMException)
            {
                // The task is registered but currently not running.
            }

            folder.DeleteTask(taskName, 0);
        }
        catch (COMException ex) when (IsFileNotFound(ex))
        {
            // Already absent.
        }
    }

    private static dynamic CreateTaskService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
        dynamic service = Activator.CreateInstance(serviceType);
        service.Connect();
        return service;
    }

    private static bool IsFileNotFound(COMException exception) =>
        unchecked((uint)exception.HResult) == 0x80070002;
}
