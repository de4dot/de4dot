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

using ICSharpCode.SharpZipLib.Zip.Compression;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class InflaterCreator {
		public static Inflater create(MethodDefinition method, bool noHeader) {
			return create(findInflaterType(method), noHeader);
		}

		public static Inflater create(TypeDefinition inflaterType, bool noHeader) {
			if (inflaterType == null)
				return createNormal(noHeader);
			var initHeaderMethod = findInitHeaderMethod(inflaterType);
			if (initHeaderMethod == null)
				return createNormal(noHeader, "Could not find inflater init header method");
			var magic = getMagic(initHeaderMethod);
			if (!magic.HasValue)
				return createNormal(noHeader);
			return new BabelInflater(noHeader, magic.Value);
		}

		static Inflater createNormal(bool noHeader) {
			return createNormal(noHeader, null);
		}

		static Inflater createNormal(bool noHeader, string errorMessage) {
			if (errorMessage != null)
				Log.w("{0}", errorMessage);
			return new Inflater(noHeader);
		}

		static TypeDefinition findInflaterType(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null || !calledMethod.IsStatic)
					continue;

				var type = calledMethod.DeclaringType;
				foreach (var nested in type.NestedTypes) {
					if (DeobUtils.hasInteger(DotNetUtils.getMethod(nested, ".ctor"), 0x8001))
						return type;
				}
			}

			return null;
		}

		static MethodDefinition findInitHeaderMethod(TypeDefinition inflaterType) {
			foreach (var nested in inflaterType.NestedTypes) {
				var method = findInitHeaderMethod2(nested);
				if (method != null)
					return method;
			}
			return null;
		}

		static MethodDefinition findInitHeaderMethod2(TypeDefinition nested) {
			foreach (var method in nested.Methods) {
				if (method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Boolean", "()"))
					continue;

				return method;
			}

			return null;
		}

		static int? getMagic(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1) || DotNetUtils.getLdcI4Value(ldci4_1) != 16)
					continue;

				var callvirt = instrs[i + 1];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;

				var ldci4_2 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;

				if (instrs[i + 3].OpCode.Code != Code.Xor)
					continue;

				return DotNetUtils.getLdcI4Value(ldci4_2);
			}

			return null;
		}
	}
}
