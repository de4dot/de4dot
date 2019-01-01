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
using System.Runtime.Remoting;
using dnlib.DotNet;
using AssemblyData;
using de4dot.code.AssemblyClient;
using de4dot.blocks;
using de4dot.mdecrypt;

namespace de4dot.code.deobfuscators {
	public static class MethodsDecrypter {
		public static DumpedMethods Decrypt(ModuleDef module, byte[] moduleCctorBytes) =>
			Decrypt(NewProcessAssemblyClientFactory.GetServerClrVersion(module), module.Location, moduleCctorBytes);

		public static DumpedMethods Decrypt(ServerClrVersion serverVersion, string filename, byte[] moduleCctorBytes) {
			Exception lastEx = null;
			for (int i = 0; i < 5; i++) {
				try {
					return Decrypt2(serverVersion, filename, moduleCctorBytes);
				}
				catch (RemotingException ex) {
					lastEx = ex;
					continue;
				}
			}
			throw lastEx;
		}

		static DumpedMethods Decrypt2(ServerClrVersion serverVersion, string filename, byte[] moduleCctorBytes) {
			using (var client = new NewProcessAssemblyClientFactory(serverVersion).Create(AssemblyServiceType.MethodDecrypter)) {
				client.Connect();
				client.WaitConnected();
				var info = new DecryptMethodsInfo();
				info.moduleCctorBytes = moduleCctorBytes;
				client.MethodDecrypterService.InstallCompileMethod(info);
				client.MethodDecrypterService.LoadObfuscator(filename);
				return client.MethodDecrypterService.DecryptMethods();
			}
		}
	}
}
