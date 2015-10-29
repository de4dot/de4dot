/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Diagnostics;
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	// Starts the server in a new process
	public class NewProcessAssemblyServerLoader : IpcAssemblyServerLoader {
		Process process;

		public NewProcessAssemblyServerLoader(AssemblyServiceType serviceType)
			: base(serviceType) {
		}

		public NewProcessAssemblyServerLoader(AssemblyServiceType serviceType, ServerClrVersion version)
			: base(serviceType, version) {
		}

		public override void LoadServer(string filename) {
			if (process != null)
				throw new ApplicationException("Server is already loaded");

			var psi = new ProcessStartInfo {
				Arguments = string.Format("{0} {1} {2}", (int)serviceType,
							Utils.ShellEscape(ipcName), Utils.ShellEscape(ipcUri)),
				CreateNoWindow = true,
				ErrorDialog = false,
				FileName = filename,
				LoadUserProfile = false,
				UseShellExecute = false,
				WorkingDirectory = Utils.GetOurBaseDir(),
			};
			process = Process.Start(psi);
			if (process == null)
				throw new ApplicationException("Could not start process");
		}

		public override void Dispose() {
			if (process != null) {
				if (!process.WaitForExit(300)) {
					try {
						process.Kill();
					}
					catch (InvalidOperationException) {
						// Here if process has already exited.
					}
				}
				process = null;
			}
		}
	}
}
