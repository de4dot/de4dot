/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
	abstract class IpcAssemblyServerLoader : IAssemblyServerLoader {
		const string ASSEMBLY_SERVER_FILENAME_X86 = "AssemblyServer.exe";
		const string ASSEMBLY_SERVER_FILENAME_X64 = "AssemblyServer-x64.exe";
		readonly string assemblyServerFilename;
		protected string ipcName;
		protected string ipcUri;
		string url;

		protected IpcAssemblyServerLoader() {
			assemblyServerFilename = getServerName();
			ipcName = Utils.randomName(15, 20);
			ipcUri = Utils.randomName(15, 20);
			url = string.Format("ipc://{0}/{1}", ipcName, ipcUri);
		}

		static string getServerName() {
			return IntPtr.Size == 4 ? ASSEMBLY_SERVER_FILENAME_X86 : ASSEMBLY_SERVER_FILENAME_X64;
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
