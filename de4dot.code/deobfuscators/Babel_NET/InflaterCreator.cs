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

using ICSharpCode.SharpZipLib.Zip.Compression;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class InflaterCreator {
		public static Inflater Create(MethodDef method, bool noHeader) => Create(FindInflaterType(method), noHeader);

		public static Inflater Create(TypeDef inflaterType, bool noHeader) {
			if (inflaterType == null)
				return CreateNormal(noHeader);
			var initHeaderMethod = FindInitHeaderMethod(inflaterType);
			if (initHeaderMethod == null)
				return CreateNormal(noHeader, "Could not find inflater init header method");
			var magic = GetMagic(initHeaderMethod);
			if (!magic.HasValue)
				return CreateNormal(noHeader);
			return new BabelInflater(noHeader, magic.Value);
		}

		static Inflater CreateNormal(bool noHeader) => CreateNormal(noHeader, null);

		static Inflater CreateNormal(bool noHeader, string errorMessage) {
			if (errorMessage != null)
				Logger.w("{0}", errorMessage);
			return new Inflater(noHeader);
		}

		static TypeDef FindInflaterType(MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic)
					continue;

				var type = calledMethod.DeclaringType;
				foreach (var nested in type.NestedTypes) {
					if (DeobUtils.HasInteger(nested.FindMethod(".ctor"), 0x8001))
						return type;
				}
			}

			return null;
		}

		static MethodDef FindInitHeaderMethod(TypeDef inflaterType) {
			foreach (var nested in inflaterType.NestedTypes) {
				var method = FindInitHeaderMethod2(nested);
				if (method != null)
					return method;
			}
			return null;
		}

		static MethodDef FindInitHeaderMethod2(TypeDef nested) {
			foreach (var method in nested.Methods) {
				if (method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Boolean", "()"))
					continue;

				return method;
			}

			return null;
		}

		static int? GetMagic(MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4() || ldci4_1.GetLdcI4Value() != 16)
					continue;

				var callvirt = instrs[i + 1];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;

				var ldci4_2 = instrs[i + 2];
				if (!ldci4_2.IsLdcI4())
					continue;

				if (instrs[i + 3].OpCode.Code != Code.Xor)
					continue;

				return ldci4_2.GetLdcI4Value();
			}

			return null;
		}
	}
}
