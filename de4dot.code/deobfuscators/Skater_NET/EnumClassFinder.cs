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

using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Skater_NET {
	class EnumClassFinder {
		ModuleDefMD module;
		FieldDef enumField;

		public EnumClassFinder(ModuleDefMD module) {
			this.module = module;
			Find();
		}

		void Find() {
			foreach (var type in module.Types) {
				if (type.HasEvents || type.HasProperties)
					continue;
				if (type.Methods.Count != 1)
					continue;
				if (type.Fields.Count != 1)
					continue;
				var method = type.Methods[0];
				if (method.Name != ".ctor")
					continue;
				var field = type.Fields[0];
				var fieldType = DotNetUtils.GetType(module, field.FieldSig.GetFieldType());
				if (fieldType == null)
					continue;
				if (!fieldType.IsEnum)
					continue;
				enumField = field;
				return;
			}
		}

		public void Deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var ldsfld = instrs[i];
					if (ldsfld.OpCode.Code != Code.Ldsfld)
						continue;

					var ldci4 = instrs[i + 1];
					if (!ldci4.IsLdcI4())
						continue;

					var stfld = instrs[i + 2];
					if (stfld.OpCode.Code != Code.Stfld)
						continue;

					var field = stfld.Operand as IField;
					if (!FieldEqualityComparer.CompareDeclaringTypes.Equals(enumField, field))
						continue;
					block.Remove(i, 3);
					i--;
				}
			}
		}
	}
}
