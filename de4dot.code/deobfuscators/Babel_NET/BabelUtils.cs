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

using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	static class BabelUtils {
		public static EmbeddedResource findEmbeddedResource(ModuleDefinition module, TypeDefinition decrypterType) {
			return findEmbeddedResource(module, decrypterType, (method) => { });
		}

		public static EmbeddedResource findEmbeddedResource(ModuleDefinition module, TypeDefinition decrypterType, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			return findEmbeddedResource(module, decrypterType, (method) => {
				simpleDeobfuscator.deobfuscate(method);
				simpleDeobfuscator.decryptStrings(method, deob);
			});
		}

		public static EmbeddedResource findEmbeddedResource(ModuleDefinition module, TypeDefinition decrypterType, Action<MethodDefinition> fixMethod) {
			foreach (var method in decrypterType.Methods) {
				if (!DotNetUtils.isMethod(method, "System.String", "()"))
					continue;
				if (!method.IsStatic)
					continue;
				fixMethod(method);
				foreach (var s in DotNetUtils.getCodeStrings(method)) {
					var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
		}

		public static bool findRegisterMethod(TypeDefinition type, out MethodDefinition regMethod, out MethodDefinition handler) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (method.Body.ExceptionHandlers.Count != 1)
					continue;

				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Ldftn)
						continue;
					var handlerRef = instr.Operand as MethodReference;
					if (handlerRef == null)
						continue;
					if (!DotNetUtils.isMethod(handlerRef, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
						continue;
					if (!MemberReferenceHelper.compareTypes(type, handlerRef.DeclaringType))
						continue;
					handler = DotNetUtils.getMethod(type, handlerRef);
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
