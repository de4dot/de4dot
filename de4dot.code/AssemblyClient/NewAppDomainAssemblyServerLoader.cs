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
using System.Threading;
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	// Starts the server in a new app domain.
	public sealed class NewAppDomainAssemblyServerLoader : IpcAssemblyServerLoader {
		AppDomain appDomain;
		Thread thread;

		public NewAppDomainAssemblyServerLoader(AssemblyServiceType serviceType)
			: base(serviceType) {
		}

		public override void LoadServer(string filename) {
			if (appDomain != null)
				throw new ApplicationException("Server is already loaded");

			appDomain = AppDomain.CreateDomain(Utils.RandomName(15, 20));
			thread = new Thread(new ThreadStart(() => {
				try {
					appDomain.ExecuteAssembly(filename, null, new string[] {
						((int)serviceType).ToString(), ipcName, ipcUri
					});
				}
				catch (NullReferenceException) {
					// Here if appDomain was set to null by Dispose() before this thread started
				}
				catch (AppDomainUnloadedException) {
					// Here if it was unloaded by Dispose()
				}
				UnloadAppDomain(appDomain);
				appDomain = null;
			}));
			thread.Start();
		}

		public override void Dispose() {
			UnloadAppDomain(appDomain);
			if (thread != null) {
				try {
					if (!thread.Join(100))
						thread.Abort();
				}
				catch (ThreadStateException) {
					// Here if eg. the thread wasn't started
				}
				thread = null;
			}
			// It could still be loaded if the thread was aborted so do it again
			UnloadAppDomain(appDomain);
			appDomain = null;
		}

		static void UnloadAppDomain(AppDomain appDomain) {
			if (appDomain != null) {
				try {
					AppDomain.Unload(appDomain);
				}
				catch (AppDomainUnloadedException) {
				}
				catch (CannotUnloadAppDomainException) {
				}
			}
		}
	}
}
