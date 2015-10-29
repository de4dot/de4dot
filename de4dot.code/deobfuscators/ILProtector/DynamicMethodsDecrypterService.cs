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
using System.Reflection;
using System.Collections.Generic;
using dnlib.DotNet;
using AssemblyData;

namespace de4dot.code.deobfuscators.ILProtector {
	sealed class DynamicMethodsDecrypterService : IUserGenericService {
		public const int MSG_DECRYPT_METHODS = 0;
		public const int MSG_HAS_DELEGATE_TYPE_FLAG = 1;

		Module reflObfModule;
		ModuleDefMD obfModule;
		bool hasDelegateTypeFlag;

		[CreateUserGenericService]
		public static IUserGenericService Create() {
			return new DynamicMethodsDecrypterService();
		}

		public void Dispose() {
			if (obfModule != null)
				obfModule.Dispose();
			obfModule = null;
		}

		public void AssemblyLoaded(Assembly assembly) {
			this.reflObfModule = assembly.ManifestModule;
			this.obfModule = ModuleDefMD.Load(reflObfModule);
		}

		public object HandleMessage(int msg, object[] args) {
			switch (msg) {
			case MSG_DECRYPT_METHODS:
				return DecryptMethods(args[0] as IList<int>);

			case MSG_HAS_DELEGATE_TYPE_FLAG:
				return hasDelegateTypeFlag;

			default:
				throw new ApplicationException(string.Format("Invalid msg: {0:X8}", msg));
			}
		}

		IList<DecryptedMethodInfo> DecryptMethods(IList<int> methodIds) {
			using (var decrypter = new DynamicMethodsDecrypter(obfModule, reflObfModule)) {
				decrypter.Initialize();

				var infos = new List<DecryptedMethodInfo>();

				for (int i = 0; i < methodIds.Count; i += 2)
					infos.Add(decrypter.Decrypt(methodIds[i], (uint)methodIds[i + 1]));

				hasDelegateTypeFlag = decrypter.MethodReaderHasDelegateTypeFlag;

				return infos;
			}
		}
	}
}
