using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WheekKeys
{
    internal static class SogouInput
    {
        private static readonly Guid KeyboardCategory = new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");

        public static bool IsActive()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint thread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            if (thread == 0) return false;
            IntPtr layout = GetKeyboardLayout(thread);
            if (layout == IntPtr.Zero) return false;

            string legacyDescription = GetLegacyDescription(layout);
            bool active = legacyDescription != null && IsSogou(legacyDescription);
            int local;
            if (legacyDescription == null && SystemParametersInfo(0x104e, 0, out local, 0) && local == 0)
                active = IsSharedTsfSogou(layout);

            return active && GetForegroundWindow() == foreground && GetKeyboardLayout(thread) == layout;
        }

        private static string GetLegacyDescription(IntPtr layout)
        {
            if (!ImmIsIME(layout)) return null;
            StringBuilder description = new StringBuilder(256);
            return ImmGetDescription(layout, description, (uint)description.Capacity) != 0 ? description.ToString() : null;
        }

        private static bool IsSogou(string description)
        {
            return description.IndexOf("搜狗", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   description.IndexOf("sogou", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSharedTsfSogou(IntPtr layout)
        {
            object instance = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid("33c53a50-f456-4884-b049-85fd643ecfed"));
                instance = Activator.CreateInstance(type);
                ITfInputProcessorProfileMgr manager = (ITfInputProcessorProfileMgr)instance;
                TfInputProcessorProfile profile;
                Guid category = KeyboardCategory;
                if (manager.GetActiveProfile(ref category, out profile) != 0 || profile.ProfileType != 1 ||
                    profile.LanguageId != unchecked((ushort)(layout.ToInt64() & 0xffff)))
                    return false;
                string description;
                ITfInputProcessorProfiles profiles = (ITfInputProcessorProfiles)instance;
                return profiles.GetLanguageProfileDescription(ref profile.Clsid, profile.LanguageId,
                    ref profile.Profile, out description) == 0 && description != null && IsSogou(description);
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TfInputProcessorProfile
        {
            public uint ProfileType;
            public ushort LanguageId;
            public Guid Clsid;
            public Guid Profile;
            public Guid Category;
            public IntPtr SubstituteLayout;
            public uint Caps;
            public IntPtr Layout;
            public uint Flags;
        }

        [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfInputProcessorProfileMgr
        {
            void ActivateProfile();
            void DeactivateProfile();
            void GetProfile();
            void EnumProfiles();
            void ReleaseInputProcessor();
            void RegisterProfile();
            void UnregisterProfile();
            [PreserveSig] int GetActiveProfile(ref Guid category, out TfInputProcessorProfile profile);
        }

        [ComImport, Guid("1f02b6c5-7842-4ee6-8a0b-9a24183a95ca"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfInputProcessorProfiles
        {
            void Register();
            void Unregister();
            void AddLanguageProfile();
            void RemoveLanguageProfile();
            void EnumInputProcessorInfo();
            void GetDefaultLanguageProfile();
            void SetDefaultLanguageProfile();
            void ActivateLanguageProfile();
            void GetActiveLanguageProfile();
            [PreserveSig] int GetLanguageProfileDescription(ref Guid clsid, ushort languageId, ref Guid profile,
                [MarshalAs(UnmanagedType.BStr)] out string description);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmIsIME(IntPtr layout);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode, EntryPoint = "ImmGetDescriptionW")]
        private static extern uint ImmGetDescription(IntPtr layout, StringBuilder description, uint length);
    }
}
