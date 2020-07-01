// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Microsoft.PowerShell.UnixCompleters
{
    internal class CommandInvoker
    {
        private readonly string _shellPath;
        private readonly ShellType _shellType;

        private Process _shellDaemon;

        private CommandInvoker(ShellType shellType, string shellPath)
        {
            _shellType = shellType;
            _shellPath = shellPath;
        }

        private void Init()
        {
            var startInfo = new ProcessStartInfo() {
                FileName = _shellPath,
                Arguments = "-s",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };

            _shellDaemon = Process.Start(startInfo);
        }
    }
}
