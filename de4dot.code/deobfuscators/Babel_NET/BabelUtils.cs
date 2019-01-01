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

using System.Collections.Generic;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using System;

namespace de4dot.code.deobfuscators.Babel_NET {
	static class BabelUtils {
		public static EmbeddedResource FindEmbeddedResource(ModuleDefMD module, TypeDef decrypterType) =>
			FindEmbeddedResource(module, decrypterType, (method) => { });

		public static EmbeddedResource FindEmbeddedResource(ModuleDefMD module, TypeDef decrypterType, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) =>
			FindEmbeddedResource(module, decrypterType, (method) => {
				simpleDeobfuscator.Deobfuscate(method);
				simpleDeobfuscator.DecryptStrings(method, deob);
			});

		public static EmbeddedResource FindEmbeddedResource(ModuleDefMD module, TypeDef decrypterType, Action<MethodDef> fixMethod) {
			foreach (var method in decrypterType.Methods) {
				if (!DotNetUtils.IsMethod(method, "System.String", "()"))
					continue;
				if (!method.IsStatic)
					continue;
				fixMethod(method);
				var resource = FindEmbeddedResource1(module, method) ?? FindEmbeddedResource2(module, method);
				if (resource != null)
					return resource;
			}
			return null;
		}

		static EmbeddedResource FindEmbeddedResource1(ModuleDefMD module, MethodDef method) {
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				if (DotNetUtils.GetResource(module, s) is EmbeddedResource resource)
					return resource;
			}
			return null;
		}

		static EmbeddedResource FindEmbeddedResource2(ModuleDefMD module, MethodDef method) {
			var strings = new List<string>(DotNetUtils.GetCodeStrings(method));
			if (strings.Count != 1)
				return null;
			var encryptedString = strings[0];

			if (!GetXorKey2(method, out int xorKey))
				return null;

			var sb = new StringBuilder(encryptedString.Length);
			foreach (var c in encryptedString)
				sb.Append((char)(c ^ xorKey));
			return DotNetUtils.GetResource(module, sb.ToString()) as EmbeddedResource;
		}

		static bool GetXorKey2(MethodDef method, out int xorKey) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldelem = instrs[i];
				if (ldelem.OpCode.Code != Code.Ldelem_U2)
					continue;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;

				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				xorKey = ldci4.GetLdcI4Value();
				return true;
			}

			xorKey = 0;
			return false;
		}

		public static bool FindRegisterMethod(TypeDef type, out MethodDef regMethod, out MethodDef handler) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (method.Body.ExceptionHandlers.Count != 1)
					continue;

				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Ldftn)
						continue;
					var handlerRef = instr.Operand as IMethod;
					if (handlerRef == null)
						continue;
					if (!DotNetUtils.IsMethod(handlerRef, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
						continue;
					if (!new SigComparer().Equals(type, handlerRef.DeclaringType))
						continue;
					handler = DotNetUtils.GetMethod(type, handlerRef);
					if (handler == null)
						continue;
					if (handler.Body == null || handler.Body.ExceptionHandlers.Count != 1)
						continue;

					regMethod = method;
					return true;
				}
			}

			regMethod = null;
			handler = null;
			return false;
		}
	}
}
