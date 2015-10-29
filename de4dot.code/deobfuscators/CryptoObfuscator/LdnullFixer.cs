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
using de4dot.blocks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class LdnullFixer {
		readonly ModuleDef module;
		readonly InlinedMethodTypes inlinedMethodTypes;

		public LdnullFixer(ModuleDef module, InlinedMethodTypes inlinedMethodTypes) {
			this.module = module;
			this.inlinedMethodTypes = inlinedMethodTypes;
		}

		public void Restore() {
			var fields = FindFieldTypes(FindFieldTypes());
			Restore(fields);
			foreach (var field in fields.Keys)
				inlinedMethodTypes.Add(field.DeclaringType);
		}

		FieldDefAndDeclaringTypeDict<FieldDef> FindFieldTypes() {
			var dict = new FieldDefAndDeclaringTypeDict<FieldDef>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					var body = method.Body;
					if (body == null)
						continue;
					foreach (var instr in body.Instructions) {
						if (instr.OpCode.Code != Code.Ldsfld)
							continue;
						var field = instr.Operand as FieldDef;
						if (field == null)
							continue;
						var declType = field.DeclaringType;
						if (declType == null)
							continue;
						if (!InlinedMethodTypes.IsValidFieldType(declType))
							continue;
						dict.Add(field, field);
					}
				}
			}

			return dict;
		}

		Dictionary<FieldDef, bool> FindFieldTypes(FieldDefAndDeclaringTypeDict<FieldDef> fields) {
			var validFields = new Dictionary<FieldDef, bool>(fields.Count);
			foreach (var field in fields.GetKeys())
				validFields.Add(field, false);

			foreach (var type in module.GetTypes()) {
				if (validFields.Count == 0)
					break;

				foreach (var method in type.Methods) {
					var body = method.Body;
					if (body == null)
						continue;
					foreach (var instr in body.Instructions) {
						if (instr.OpCode.Code == Code.Ldsfld)
							continue;
						var field = instr.Operand as IField;
						if (field == null)
							continue;

						var validType = fields.Find(field);
						if (validType == null)
							continue;

						validFields.Remove(validType);
					}
				}
			}

			return validFields;
		}

		int Restore(Dictionary<FieldDef, bool> nullFields) {
			int numRestored = 0;
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					var body = method.Body;
					if (body == null)
						continue;
					foreach (var instr in body.Instructions) {
						if (instr.OpCode.Code != Code.Ldsfld)
							continue;
						var field = instr.Operand as FieldDef;
						if (field == null)
							continue;
						if (!nullFields.ContainsKey(field))
							continue;

						instr.OpCode = OpCodes.Ldnull;
						instr.Operand = null;
						numRestored++;
					}
				}
			}
			return numRestored;
		}
	}
}
