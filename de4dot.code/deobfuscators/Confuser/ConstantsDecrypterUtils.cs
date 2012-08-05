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

namespace de4dot.code.deobfuscators.Confuser {
	static class ConstantsDecrypterUtils {
		public static FieldDefinition findDictField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Void System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>")
					continue;

				return field;
			}
			return null;
		}

		public static FieldDefinition findDataField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var callvirt = instrs[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null || calledMethod.FullName != "System.Byte[] System.IO.MemoryStream::ToArray()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Byte[]")
					continue;

				return field;
			}
			return null;
		}

		public static FieldDefinition findStreamField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Void System.IO.MemoryStream::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.IO.MemoryStream")
					continue;

				return field;
			}
			return null;
		}
	}
}
