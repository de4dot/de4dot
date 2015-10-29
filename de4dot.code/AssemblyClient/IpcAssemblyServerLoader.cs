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
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	public enum ServerClrVersion {
		CLR_ANY_ANYCPU,
		CLR_ANY_x86,
		CLR_ANY_x64,
		CLR_v20_x86,
		CLR_v20_x64,
		CLR_v40_x86,
		CLR_v40_x64,
	}

	public abstract class IpcAssemblyServerLoader : IAssemblyServerLoader {
		readonly string assemblyServerFilename;
		protected string ipcName;
		protected string ipcUri;
		protected AssemblyServiceType serviceType;
		string url;

		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType)
			: this(serviceType, ServerClrVersion.CLR_ANY_ANYCPU) {
		}

		protected IpcAssemblyServerLoader(AssemblyServiceType serviceType, ServerClrVersion serverVersion) {
			this.serviceType = serviceType;
			assemblyServerFilename = GetServerName(serverVersion);
			ipcName = Utils.RandomName(15, 20);
			ipcUri = Utils.RandomName(15, 20);
			url = string.Format("ipc://{0}/{1}", ipcName, ipcUri);
		}

		static string GetServerName(ServerClrVersion serverVersion) {
			if (serverVersion == ServerClrVersion.CLR_ANY_ANYCPU)
				serverVersion = IntPtr.Size == 4 ? ServerClrVersion.CLR_ANY_x86 : ServerClrVersion.CLR_ANY_x64;
			switch (serverVersion) {
			case ServerClrVersion.CLR_ANY_x86: return "AssemblyServer.exe";
			case ServerClrVersion.CLR_ANY_x64: return "AssemblyServer-x64.exe";
			case ServerClrVersion.CLR_v20_x86: return "AssemblyServer-CLR20.exe";
			case ServerClrVersion.CLR_v20_x64: return "AssemblyServer-CLR20-x64.exe";
			case ServerClrVersion.CLR_v40_x86: return "AssemblyServer-CLR40.exe";
			case ServerClrVersion.CLR_v40_x64: return "AssemblyServer-CLR40-x64.exe";
			default: throw new ArgumentException(string.Format("Invalid server version: {0}", serverVersion));
			}
		}

		public void LoadServer() {
			LoadServer(Utils.GetPathOfOurFile(assemblyServerFilename));
		}

		public abstract void LoadServer(string filename);

		public IAssemblyService CreateService() {
			return (IAssemblyService)Activator.GetObject(AssemblyService.GetType(serviceType), url);
		}

		public abstract void Dispose();
	}
}
