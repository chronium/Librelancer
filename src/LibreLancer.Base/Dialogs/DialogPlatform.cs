// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Diagnostics;
namespace LibreLancer.Dialogs
{
    static class DialogPlatform
    {
        public const int WINFORMS = 0;
        public const int ZENITY = 1;
        public const int KDIALOG = 2;
        public const int SDL = 3;
        public static int Backend;

        static DialogPlatform()
        {
            if(Platform.RunningOS == OS.Windows)
            {
                Backend = WINFORMS;
                return;
            }
            if (HasCommand("kdialog")) Backend = KDIALOG;
            else if (HasCommand("zenity")) Backend = ZENITY;
            else Backend = SDL;
        }
        
        static bool HasCommand(string cmd)
        {
            var startInfo = new ProcessStartInfo("/usr/bin/bash")
            {
                UseShellExecute = false,
                Arguments = $" -c \"command -v {cmd} >/dev/null 2>&1\""
            };
            var p = Process.Start(startInfo);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
    }
}
