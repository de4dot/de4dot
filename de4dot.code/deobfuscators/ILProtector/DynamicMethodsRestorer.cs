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
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using AssemblyData;
using de4dot.code.AssemblyClient;

namespace de4dot.code.deobfuscators.ILProtector {
	// Calls class to dynamically decrypt methods, then restores them.
	class DynamicMethodsRestorer : MethodsDecrypterBase {
		public DynamicMethodsRestorer(ModuleDefMD module, MainType mainType)
			: base(module, mainType) {
		}

		protected override void DecryptInternal() {
			CheckRuntimeFiles();
			IList<DecryptedMethodInfo> decryptedData;
			var serverVersion = NewProcessAssemblyClientFactory.GetServerClrVersion(module);
			using (var client = new NewProcessAssemblyClientFactory(serverVersion).Create(AssemblyServiceType.Generic)) {
				client.Connect();
				client.WaitConnected();

				client.GenericService.LoadUserService(typeof(DynamicMethodsDecrypterService), null);
				client.GenericService.LoadAssembly(module.Location);
				decryptedData = client.GenericService.SendMessage(DynamicMethodsDecrypterService.MSG_DECRYPT_METHODS, new object[] { GetMethodIds() }) as IList<DecryptedMethodInfo>;
				MethodReaderHasDelegateTypeFlag = (bool)client.GenericService.SendMessage(DynamicMethodsDecrypterService.MSG_HAS_DELEGATE_TYPE_FLAG, new object[0]);
			}

			if (decryptedData == null)
				throw new ApplicationException("Unknown return value from dynamic methods decrypter service");

			foreach (var info in decryptedData)
				methodInfos[info.id] = info;
		}

		void CheckRuntimeFiles() {
			foreach (var info in mainType.RuntimeFileInfos) {
				if (!File.Exists(info.PathName))
					Logger.w(string.Format("ILProtector runtime file '{0}' is missing.", info.PathName));
			}
		}

		IList<int> GetMethodIds() {
			var ids = new List<int>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					int? id = GetMethodId(method);
					if (id == null)
						continue;

					ids.Add(id.Value);
					ids.Add((int)method.Rid);
				}
			}

			return ids;
		}
	}
}
