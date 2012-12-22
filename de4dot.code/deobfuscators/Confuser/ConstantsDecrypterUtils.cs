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

using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Confuser {
	static class ConstantsDecrypterUtils {
		public static FieldDef findDictField(MethodDef method, TypeDef declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as IMethod;
				if (ctor == null || ctor.FullName != "System.Void System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>")
					continue;

				return field;
			}
			return null;
		}

		public static FieldDef findDataField(MethodDef method, TypeDef declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var callvirt = instrs[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as IMethod;
				if (calledMethod == null || calledMethod.FullName != "System.Byte[] System.IO.MemoryStream::ToArray()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Byte[]")
					continue;

				return field;
			}
			return null;
		}

		public static FieldDef findStreamField(MethodDef method, TypeDef declaringType) {
			return findStreamField(method, declaringType, "System.IO.Stream");
		}

		public static FieldDef findMemoryStreamField(MethodDef method, TypeDef declaringType) {
			return findStreamField(method, declaringType, "System.IO.MemoryStream");
		}

		public static FieldDef findStreamField(MethodDef method, TypeDef declaringType, string fieldTypeName) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as IMethod;
				if (ctor == null || ctor.FullName != "System.Void System.IO.MemoryStream::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != fieldTypeName)
					continue;

				return field;
			}
			return null;
		}
	}
}
