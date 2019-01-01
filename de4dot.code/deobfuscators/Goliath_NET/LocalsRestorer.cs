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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class LocalsRestorer {
		ModuleDefMD module;
		TypeDefDict<Info> typeToInfo = new TypeDefDict<Info>();

		class Info {
			public TypeDef type;
			public TypeSig localType;
			public bool referenced = false;
			public Info(TypeDef type, TypeSig localType) {
				this.type = type;
				this.localType = localType;
			}
		}

		public List<TypeDef> Types {
			get {
				var list = new List<TypeDef>(typeToInfo.Count);
				foreach (var info in typeToInfo.GetValues()) {
					if (info.referenced)
						list.Add(info.type);
				}
				return list;
			}
		}

		public LocalsRestorer(ModuleDefMD module) => this.module = module;

		public void Find() {
			foreach (var type in module.GetTypes())
				Initialize(type);
		}

		void Initialize(TypeDef type) {
			if (type.HasEvents || type.HasProperties)
				return;

			if (!type.IsValueType)
				return;
			if (type.Methods.Count != 1)
				return;
			var ctor = type.Methods[0];
			if (ctor.Name != ".ctor" || ctor.Body == null || ctor.IsStatic)
				return;
			var sig = ctor.MethodSig;
			if (sig == null || sig.Params.Count != 1)
				return;
			var ctorParam = sig.Params[0];

			if (type.Fields.Count != 1)
				return;
			var typeField = type.Fields[0];
			if (typeField.IsStatic)
				return;
			if (!new SigComparer().Equals(ctorParam, typeField.FieldType))
				return;

			typeToInfo.Add(ctor.DeclaringType, new Info(ctor.DeclaringType, typeField.FieldType));
		}

		public void Deobfuscate(Blocks blocks) {
			var instrsToRemove = new List<int>();
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				instrsToRemove.Clear();
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					int indexToRemove;
					ITypeDefOrRef type;
					Local local = null;

					if (instr.OpCode.Code == Code.Newobj) {
						if (i + 1 >= instrs.Count)
							continue;
						var ctor = instr.Operand as IMethod;
						if (ctor == null || ctor.DeclaringType == null)
							continue;
						if (ctor.Name != ".ctor")
							continue;

						var next = instrs[i + 1];
						if (!next.IsStloc() && !next.IsLeave() && next.OpCode.Code != Code.Pop)
							continue;

						indexToRemove = i;
						type = ctor.DeclaringType;
						if (next.IsStloc())
							local = Instr.GetLocalVar(blocks.Locals, next);
					}
					else if (instr.OpCode.Code == Code.Ldfld) {
						if (i == 0)
							continue;
						var ldloc = instrs[i - 1];
						if (!ldloc.IsLdloc())
							continue;

						var field = instr.Operand as IField;
						if (field == null || field.DeclaringType == null)
							continue;

						indexToRemove = i;
						type = field.DeclaringType;
						local = Instr.GetLocalVar(blocks.Locals, ldloc);
					}
					else
						continue;

					if (type == null)
						continue;
					var info = typeToInfo.Find(type);
					if (info == null)
						continue;

					info.referenced = true;
					instrsToRemove.Add(indexToRemove);
					if (local != null)
						local.Type = info.localType;
				}
				if (instrsToRemove.Count > 0)
					block.Remove(instrsToRemove);
			}
		}
	}
}
