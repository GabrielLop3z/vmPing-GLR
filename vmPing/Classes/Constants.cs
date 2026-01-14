using System.Windows.Input;

namespace vmPing.Classes
{
    class Constants
    {
        // Default probe background colors.
        public const string Color_Probe_Background_Inactive = "#F3F4F6";      // Gray 100
        public const string Color_Probe_Background_Up = "#10B981";            // Emerald 500
        public const string Color_Probe_Background_Down = "#EF4444";          // Red 500
        public const string Color_Probe_Background_Indeterminate = "#F59E0B";   // Amber 500
        public const string Color_Probe_Background_Error = "#F97316";           // Orange 500
        public const string Color_Probe_Background_Scanner = "#374151";         // Gray 700

        // Default probe foreground colors.
        public const string Color_Probe_Foreground_Inactive = "#4B5563";      // Gray 600
        public const string Color_Probe_Foreground_Up = "#FFFFFF";
        public const string Color_Probe_Foreground_Down = "#FFFFFF";
        public const string Color_Probe_Foreground_Indeterminate = "#FFFFFF";
        public const string Color_Probe_Foreground_Error = "#FFFFFF";
        public const string Color_Probe_Foreground_Scanner = "#F9FAFB";

        // Default statistics foreground colors.
        public const string Color_Statistics_Foreground_Inactive = "#6B7280";
        public const string Color_Statistics_Foreground_Up = "#ECFDF5";
        public const string Color_Statistics_Foreground_Down = "#FEF2F2";
        public const string Color_Statistics_Foreground_Indeterminate = "#FFFBEB";
        public const string Color_Statistics_Foreground_Error = "#FFFFFF";

        // Default alias / probe title colors.
        public const string Color_Alias_Foreground_Inactive = "#1F2937";      // Gray 800
        public const string Color_Alias_Foreground_Up = "#FFFFFF";
        public const string Color_Alias_Foreground_Down = "#FFFFFF";
        public const string Color_Alias_Foreground_Indeterminate = "#FFFFFF";
        public const string Color_Alias_Foreground_Error = "#FFFFFF";
        public const string Color_Alias_Foreground_Scanner = "#FFFFFF";

        // Default probe options.
        public const string DefaultIcmpData = "https://github.com/R-Smith/vmPing";
        public const int DefaultTimeout = 2000;       // In miliseconds.
        public const int DefaultTTL = 64;
        public const int DefaultInterval = 2000;      // In miliseconds.

        // Default audio alert file paths.
        public const string DefaultAudioDownFilePath = @"%WINDIR%\Media\Windows Notify Email.wav";
        public const string DefaultAudioUpFilePath = @"%WINDIR%\Media\Windows Unlock.wav";

        // Key bindings.
        public const Key StatusHistoryKeyBinding = Key.F12;
        public const Key HelpKeyBinding = Key.F1;
    }
}
