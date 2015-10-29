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
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	class ArrayBlockDeobfuscator : BlockDeobfuscator {
		ArrayBlockState arrayBlockState;
		Dictionary<Local, ArrayBlockState.FieldInfo> localToInfo = new Dictionary<Local, ArrayBlockState.FieldInfo>();
		DsConstantsReader constantsReader;

		public ArrayBlockDeobfuscator(ArrayBlockState arrayBlockState) {
			this.arrayBlockState = arrayBlockState;
		}

		public override void DeobfuscateBegin(Blocks blocks) {
			base.DeobfuscateBegin(blocks);
			InitLocalToInfo();
		}

		void InitLocalToInfo() {
			localToInfo.Clear();

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldsfld = instrs[i];
					if (ldsfld.OpCode.Code != Code.Ldsfld)
						continue;
					var stloc = instrs[i + 1];
					if (!stloc.IsStloc())
						continue;

					var info = arrayBlockState.GetFieldInfo((IField)ldsfld.Operand);
					if (info == null)
						continue;
					var local = stloc.Instruction.GetLocal(blocks.Locals);
					if (local == null)
						continue;

					localToInfo[local] = info;
				}
			}
		}

		protected override bool Deobfuscate(Block block) {
			bool modified = false;

			constantsReader = null;
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				bool ch = Deobfuscate1(block, i);
				if (ch) {
					modified = true;
					continue;
				}

				ch = Deobfuscate2(block, i);
				if (ch) {
					modified = true;
					continue;
				}

				ch = Deobfuscate3(block, i);
				if (ch) {
					modified = true;
					continue;
				}
			}

			return modified;
		}

		static bool IsLdelem(ArrayBlockState.FieldInfo info, Code code) {
			switch (info.elementType) {
			case ElementType.Boolean:
			case ElementType.I1:
			case ElementType.U1:
				return code == Code.Ldelem_I1 || code == Code.Ldelem_U1;

			case ElementType.Char:
			case ElementType.I2:
			case ElementType.U2:
				return code == Code.Ldelem_I2 || code == Code.Ldelem_U2;

			case ElementType.I4:
			case ElementType.U4:
				return code == Code.Ldelem_I4 || code == Code.Ldelem_U4;

			default:
				return false;
			}
		}

		static bool IsStelem(ArrayBlockState.FieldInfo info, Code code) {
			switch (info.elementType) {
			case ElementType.Boolean:
			case ElementType.I1:
			case ElementType.U1:
				return code == Code.Stelem_I1;

			case ElementType.Char:
			case ElementType.I2:
			case ElementType.U2:
				return code == Code.Stelem_I2;

			case ElementType.I4:
			case ElementType.U4:
				return code == Code.Stelem_I4;

			default:
				return false;
			}
		}

		bool Deobfuscate1(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldloc = instrs[i];
			if (!ldloc.IsLdloc())
				return false;
			var local = ldloc.Instruction.GetLocal(blocks.Locals);
			if (local == null)
				return false;
			ArrayBlockState.FieldInfo info;
			if (!localToInfo.TryGetValue(local, out info))
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.IsLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (!IsLdelem(info, ldelem.OpCode.Code))
				return false;

			block.Remove(i, 3 - 1);
			instrs[i] = new Instr(Instruction.CreateLdcI4((int)info.ReadArrayElement(ldci4.GetLdcI4Value())));
			return true;
		}

		bool Deobfuscate2(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldsfld = instrs[i];
			if (ldsfld.OpCode.Code != Code.Ldsfld)
				return false;
			var info = arrayBlockState.GetFieldInfo(ldsfld.Operand as IField);
			if (info == null)
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.IsLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (!IsLdelem(info, ldelem.OpCode.Code))
				return false;

			block.Remove(i, 3 - 1);
			instrs[i] = new Instr(Instruction.CreateLdcI4((int)info.ReadArrayElement(ldci4.GetLdcI4Value())));
			return true;
		}

		bool Deobfuscate3(Block block, int i) {
			var instrs = block.Instructions;
			if (i + 1 >= instrs.Count)
				return false;

			int start = i;
			var ldsfld = instrs[i];
			if (ldsfld.OpCode.Code != Code.Ldsfld)
				return false;
			var info = arrayBlockState.GetFieldInfo(ldsfld.Operand as IField);
			if (info == null)
				return false;

			if (!instrs[i + 1].IsLdcI4())
				return false;

			var constants = GetConstantsReader(block);
			int value;
			i += 2;
			if (!constants.GetInt32(ref i, out value))
				return false;

			if (i >= instrs.Count)
				return false;
			var stelem = instrs[i];
			if (!IsStelem(info, stelem.OpCode.Code))
				return false;

			block.Remove(start, i - start + 1);
			return true;
		}

		DsConstantsReader GetConstantsReader(Block block) {
			if (constantsReader != null)
				return constantsReader;
			return constantsReader = new DsConstantsReader(block.Instructions);
		}
	}
}
