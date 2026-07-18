using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace LenovoDesktopFanControl.Services;

public interface IAutoStartService
{
    bool IsEnabled();
    void Enable(string executablePath);
    void Disable();
}

public class AutoStartService : IAutoStartService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LenovoDesktopFanControl";
    private const string LegacyTaskName = "Lenovo Desktop Fan Control";
    internal const string TaskExecutionTimeLimit = "PT0S";
#if DEBUG
    private static string TaskName => $"Lenovo Desktop Fan Control Debug - {CurrentUserSid}";
    private static string ReleaseTaskName => $"Lenovo Desktop Fan Control - {CurrentUserSid}";
#else
    private static string TaskName => $"Lenovo Desktop Fan Control - {CurrentUserSid}";
#endif
    private static string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value ?? "UnknownUser";

    public bool IsEnabled()
    {
        try
        {
            if (TaskExists(TaskName))
                return true;

#if !DEBUG
            if (TaskExists(LegacyTaskName))
                return true;

            // Recognize older releases that used the Run key so the checkbox
            // remains accurate until the entry is migrated.
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
#else
            return false;
#endif
        }
        catch
        {
            return false;
        }
    }

    public void Enable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        CreateOrUpdateTask(
            TaskName,
            WindowsIdentity.GetCurrent().Name,
            executablePath,
            ApplicationLaunch.BuildTaskArguments(
                executablePath,
                LightingBackgroundHost.BackgroundArgument));
#if DEBUG
        DeleteReleaseTaskIfItTargets(executablePath);
#else
        DeleteTaskIfPresent(LegacyTaskName);
        DeleteLegacyRunEntry();
#endif
    }

    public void Disable()
    {
        DeleteTaskIfPresent(TaskName);
#if !DEBUG
        DeleteTaskIfPresent(LegacyTaskName);
        DeleteLegacyRunEntry();
#endif
    }

    private static void CreateOrUpdateTask(
        string taskName,
        string runAsUser,
        string executablePath,
        string arguments)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description =
            "Keeps Lenovo desktop lighting active for the signed-in user.";
        definition.Principal.UserId = runAsUser;
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        definition.Principal.RunLevel = 1; // TASK_RUNLEVEL_HIGHEST
        definition.Settings.Enabled = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = TaskExecutionTimeLimit;
        definition.Settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW
        definition.Settings.RestartCount = 3;
        definition.Settings.RestartInterval = "PT1M";

        dynamic trigger = definition.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.UserId = runAsUser;
        dynamic action = definition.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = executablePath;
        action.Arguments = arguments;

        // The task must be registered from this elevated process because it
        // launches with TASK_RUNLEVEL_HIGHEST. Keep Administrators as owner
        // and grant the user only read/delete rights. Granting task write
        // access would let an unelevated process replace the elevated action.
        var taskSecurityDescriptor = BuildTaskSecurityDescriptor(CurrentUserSid);
        folder.RegisterTaskDefinition(
            taskName,
            definition,
            6, // TASK_CREATE_OR_UPDATE
            runAsUser,
            null,
            3, // TASK_LOGON_INTERACTIVE_TOKEN
            taskSecurityDescriptor);
    }

    internal static string BuildTaskSecurityDescriptor(string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        return $"O:BAG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;SDGR;;;{userSid})";
    }

    private static bool TaskExists(string taskName)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        try
        {
            _ = folder.GetTask(taskName);
            return true;
        }
        catch (COMException ex) when (IsFileNotFound(ex))
        {
            return false;
        }
    }

#if DEBUG
    private static void DeleteReleaseTaskIfItTargets(string executablePath)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        try
        {
            dynamic task = folder.GetTask(ReleaseTaskName);
            dynamic actions = task.Definition.Actions;
            if (actions.Count != 1)
                return;

            dynamic action = actions.Item(1);
            if (string.Equals(
                    (string)action.Path,
                    executablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                folder.DeleteTask(ReleaseTaskName, 0);
            }
        }
        catch (COMException ex) when (IsFileNotFound(ex))
        {
            // No release task was accidentally pointed at this Debug build.
        }
    }
#endif

    private static void DeleteTaskIfPresent(string taskName)
    {
        dynamic service = CreateTaskService();
        dynamic folder = service.GetFolder("\\");
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (COMException ex) when (IsFileNotFound(ex))
        {
            // Already absent.
        }
    }

    private static dynamic CreateTaskService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable");
        dynamic service = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Unable to create the Task Scheduler service");
        service.Connect();
        return service;
    }

    private static void DeleteLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, false);
    }

    private static bool IsFileNotFound(COMException exception) =>
        unchecked((uint)exception.HResult) == 0x80070002;
}
