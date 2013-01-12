/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

		public override void deobfuscateBegin(Blocks blocks) {
			base.deobfuscateBegin(blocks);
			initLocalToInfo();
		}

		void initLocalToInfo() {
			localToInfo.Clear();

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldsfld = instrs[i];
					if (ldsfld.OpCode.Code != Code.Ldsfld)
						continue;
					var stloc = instrs[i + 1];
					if (!stloc.isStloc())
						continue;

					var info = arrayBlockState.getFieldInfo((IField)ldsfld.Operand);
					if (info == null)
						continue;
					var local = stloc.Instruction.GetLocal(blocks.Locals);
					if (local == null)
						continue;

					localToInfo[local] = info;
				}
			}
		}

		protected override bool deobfuscate(Block block) {
			bool changed = false;

			constantsReader = null;
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				bool ch = deobfuscate1(block, i);
				if (ch) {
					changed = true;
					continue;
				}

				ch = deobfuscate2(block, i);
				if (ch) {
					changed = true;
					continue;
				}

				ch = deobfuscate3(block, i);
				if (ch) {
					changed = true;
					continue;
				}
			}

			return changed;
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

		bool deobfuscate1(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldloc = instrs[i];
			if (!ldloc.isLdloc())
				return false;
			var local = ldloc.Instruction.GetLocal(blocks.Locals);
			if (local == null)
				return false;
			ArrayBlockState.FieldInfo info;
			if (!localToInfo.TryGetValue(local, out info))
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.isLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (!IsLdelem(info, ldelem.OpCode.Code))
				return false;

			block.remove(i, 3 - 1);
			instrs[i] = new Instr(Instruction.CreateLdcI4((int)info.readArrayElement(ldci4.getLdcI4Value())));
			return true;
		}

		bool deobfuscate2(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldsfld = instrs[i];
			if (ldsfld.OpCode.Code != Code.Ldsfld)
				return false;
			var info = arrayBlockState.getFieldInfo(ldsfld.Operand as IField);
			if (info == null)
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.isLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (!IsLdelem(info, ldelem.OpCode.Code))
				return false;

			block.remove(i, 3 - 1);
			instrs[i] = new Instr(Instruction.CreateLdcI4((int)info.readArrayElement(ldci4.getLdcI4Value())));
			return true;
		}

		bool deobfuscate3(Block block, int i) {
			var instrs = block.Instructions;
			if (i + 1 >= instrs.Count)
				return false;

			int start = i;
			var ldsfld = instrs[i];
			if (ldsfld.OpCode.Code != Code.Ldsfld)
				return false;
			var info = arrayBlockState.getFieldInfo(ldsfld.Operand as IField);
			if (info == null)
				return false;

			if (!instrs[i + 1].isLdcI4())
				return false;

			var constants = getConstantsReader(block);
			int value;
			i += 2;
			if (!constants.getInt32(ref i, out value))
				return false;

			if (i >= instrs.Count)
				return false;
			var stelem = instrs[i];
			if (!IsStelem(info, stelem.OpCode.Code))
				return false;

			block.remove(start, i - start + 1);
			return true;
		}

		DsConstantsReader getConstantsReader(Block block) {
			if (constantsReader != null)
				return constantsReader;
			return constantsReader = new DsConstantsReader(block.Instructions);
		}
	}
}
