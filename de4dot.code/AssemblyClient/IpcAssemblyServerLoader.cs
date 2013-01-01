/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
	enum ServerClrVersion {
		CLR_ANY_ANYCPU,
		CLR_ANY_x86,
		CLR_ANY_x64,
		CLR_v20_x86,
		CLR_v20_x64,
		CLR_v40_x86,
		CLR_v40_x64,
	}

	abstract class IpcAssemblyServerLoader : IAssemblyServerLoader {
		readonly string assemblyServerFilename;
		protected string ipcName;
		protected string ipcUri;
		string url;

		protected IpcAssemblyServerLoader()
			: this(ServerClrVersion.CLR_ANY_ANYCPU) {
		}

		protected IpcAssemblyServerLoader(ServerClrVersion serverVersion) {
			assemblyServerFilename = getServerName(serverVersion);
			ipcName = Utils.randomName(15, 20);
			ipcUri = Utils.randomName(15, 20);
			url = string.Format("ipc://{0}/{1}", ipcName, ipcUri);
		}

		static string getServerName(ServerClrVersion serverVersion) {
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

		public void loadServer() {
			loadServer(Utils.getPathOfOurFile(assemblyServerFilename));
		}

		public abstract void loadServer(string filename);

		public IAssemblyService createService() {
			return (IAssemblyService)Activator.GetObject(typeof(AssemblyService), url);
		}

		public abstract void Dispose();
	}
}
