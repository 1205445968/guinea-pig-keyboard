using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace WheekKeys
{
    internal sealed class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "WheekKeys";
        internal static readonly StartupRegistration CurrentUser = new StartupRegistration(Registry.CurrentUser);
        private readonly RegistryKey userKey;
        private static readonly string ExecutablePath = Assembly.GetExecutingAssembly().Location;
        private static readonly string Command = "\"" + ExecutablePath + "\"";

        internal enum State { Off, On, DisabledByWindows, DifferentPath }

        internal StartupRegistration(RegistryKey userKey) { this.userKey = userKey; }

        internal State Read()
        {
            using (RegistryKey run = userKey.OpenSubKey(RunKey))
            {
                string registered = run == null ? null : run.GetValue(ValueName) as string;
                if (registered == null) return State.Off;
                if (!String.Equals(registered, Command, StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(registered, ExecutablePath, StringComparison.OrdinalIgnoreCase)) return State.DifferentPath;
            }
            using (RegistryKey approval = userKey.OpenSubKey(ApprovalKey))
            {
                byte[] value = approval == null ? null : approval.GetValue(ValueName) as byte[];
                // 任务管理器禁用启动项时保留 Run 命令，禁用标记存放在 StartupApproved。
                if (value != null && value.Length > 0 && (value[0] & 1) != 0) return State.DisabledByWindows;
            }
            return State.On;
        }

        internal void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (Command.Length > 260) throw new PathTooLongException("请将桌宠移到路径较短的文件夹后重试。");
                using (RegistryKey run = userKey.CreateSubKey(RunKey))
                    run.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                using (RegistryKey run = userKey.OpenSubKey(RunKey, true))
                    if (run != null) run.DeleteValue(ValueName, false);
            }
        }
    }
}
