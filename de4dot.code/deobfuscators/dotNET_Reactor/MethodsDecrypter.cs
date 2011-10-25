/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class MethodsDecrypter {
		ModuleDefinition module;
		MethodDefinition methodsDecrypterMethod;

		public bool Detected {
			get { return methodsDecrypterMethod != null; }
		}

		public MethodsDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			var checkedMethods = new Dictionary<MethodReferenceAndDeclaringTypeKey, bool>();
			var callCounter = new CallCounter();
			int typesLeft = 30;
			foreach (var type in module.GetTypes()) {
				if (typesLeft-- <= 0)
					break;
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null || cctor.Body == null)
					continue;

				foreach (var info in DotNetUtils.getCalledMethods(module, cctor)) {
					var method = info.Item2;
					var key = new MethodReferenceAndDeclaringTypeKey(method);
					if (!checkedMethods.ContainsKey(key)) {
						checkedMethods[key] = true;
						if (!couldBeMethodsDecrypter(method))
							continue;
					}
					callCounter.add(method);
				}
			}

			methodsDecrypterMethod = (MethodDefinition)callCounter.most();
		}

		bool couldBeMethodsDecrypter(MethodDefinition method) {
			if (!method.IsStatic)
				return false;
			if (method.Body == null)
				return false;
			if (method.Body.Instructions.Count < 2000)
				return false;

			var localTypes = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach (var local in method.Body.Variables)
				localTypes[local.VariableType.FullName] = true;
			var requiredTypes = new string[] {
				"System.Byte[]",
				"System.Diagnostics.StackFrame",
				"System.IntPtr",
				"System.IO.BinaryReader",
				"System.IO.MemoryStream",
				"System.Reflection.Assembly",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.ICryptoTransform",
				"System.Security.Cryptography.RijndaelManaged",
			};
			foreach (var typeName in requiredTypes) {
				if (!localTypes.ContainsKey(typeName))
					return false;
			}

			if (!isResourceString(DotNetUtils.getCodeStrings(method)))
				return false;

			return true;
		}

		bool isResourceString(IList<string> strings) {
			foreach (var s in strings) {
				if (DotNetUtils.getResource(module, s) != null)
					return true;
			}
			return false;
		}
	}
}
