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

using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class StringDecrypter : DecrypterBase {
		IType delegateReturnType;
		FieldDef stringStructField;

		public TypeDef StringStruct => Detected && stringStructField != null ? stringStructField.DeclaringType : null;
		public StringDecrypter(ModuleDefMD module) : base(module) { }

		protected override bool CheckDecrypterType(TypeDef type) {
			var fields = type.Fields;
			if (fields.Count != 2)
				return false;

			if (fields[0].FieldType.FullName != "System.Byte[]")
				return false;

			var dict = fields[1].FieldType.ToGenericInstSig();
			if (dict == null || dict.GenericArguments.Count != 2)
				return false;
			if (dict.GenericType.GetFullName() != "System.Collections.Generic.Dictionary`2")
				return false;

			if (dict.GenericArguments[0].FullName != "System.Int32")
				return false;

			var garg = dict.GenericArguments[1];
			if (garg.FullName != "System.String") {
				if (!garg.IsValueType)
					return false;
				var gargType = DotNetUtils.GetType(module, garg);
				if (gargType == null || !gargType.IsClass)
					return false;
				if (gargType.Fields.Count != 1)
					return false;
				var field = gargType.Fields[0];
				if (field.FieldType.FullName != "System.String")
					return false;
				delegateReturnType = gargType;
				stringStructField = field;
			}
			else {
				delegateReturnType = garg;
				stringStructField = null;
			}

			return true;
		}

		protected override bool CheckDelegateInvokeMethod(MethodDef invokeMethod) =>
			DotNetUtils.IsMethod(invokeMethod, delegateReturnType.FullName, "(System.Int32)");

		public string Decrypt(MethodDef method) {
			var info = GetInfo(method);
			decryptedReader.BaseStream.Position = info.offset;
			int len = decryptedReader.ReadInt32();
			return Encoding.UTF8.GetString(decryptedReader.ReadBytes(len));
		}

		public void Deobfuscate(Blocks blocks) {
			if (!Detected)
				return;
			if (stringStructField == null)
				return;

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldstr = instrs[i];
					if (ldstr.OpCode.Code != Code.Ldstr)
						continue;
					var ldfld = instrs[i + 1];
					if (ldfld.OpCode.Code != Code.Ldfld)
						continue;
					if (!FieldEqualityComparer.CompareDeclaringTypes.Equals(stringStructField, ldfld.Operand as IField))
						continue;
					block.Remove(i + 1, 1);
				}
			}
		}
	}
}
