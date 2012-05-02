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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	class ArrayBlockDeobfuscator : BlockDeobfuscator {
		ModuleDefinition module;
		FieldDefinitionAndDeclaringTypeDict<FieldInfo> fieldToInfo = new FieldDefinitionAndDeclaringTypeDict<FieldInfo>();
		Dictionary<VariableDefinition, FieldInfo> localToInfo = new Dictionary<VariableDefinition, FieldInfo>();
		DsConstantsReader constantsReader;

		class FieldInfo {
			public FieldDefinition field;
			public byte[] array;

			public FieldInfo(FieldDefinition field, byte[] array) {
				this.field = field;
				this.array = array;
			}
		}

		public bool Detected {
			get { return fieldToInfo.Count != 0; }
		}

		public ArrayBlockDeobfuscator(ModuleDefinition module) {
			this.module = module;
		}

		public void init() {
			initializeArrays(DotNetUtils.getModuleTypeCctor(module));
		}

		void initializeArrays(MethodDefinition method) {
			if (method == null || method.Body == null)
				return;

			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var ldci4 = instructions[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				i++;
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Newarr, OpCodes.Dup, OpCodes.Ldtoken, OpCodes.Call, OpCodes.Stsfld);
				if (instrs == null)
					continue;

				var arrayType = instrs[0].Operand as TypeReference;
				if (arrayType == null || arrayType.EType != ElementType.U1)
					continue;

				var arrayInitField = instrs[2].Operand as FieldDefinition;
				if (arrayInitField == null || arrayInitField.InitialValue == null || arrayInitField.InitialValue.Length == 0)
					continue;

				var calledMethod = instrs[3].Operand as MethodReference;
				if (calledMethod == null || calledMethod.FullName != "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
					continue;

				var targetField = instrs[4].Operand as FieldDefinition;
				if (targetField == null)
					continue;

				fieldToInfo.add(targetField, new FieldInfo(targetField, (byte[])arrayInitField.InitialValue.Clone()));
			}
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

					var info = fieldToInfo.find((FieldReference)ldsfld.Operand);
					if (info == null)
						continue;
					var local = DotNetUtils.getLocalVar(blocks.Locals, stloc.Instruction);
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

		bool deobfuscate1(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldloc = instrs[i];
			if (!ldloc.isLdloc())
				return false;
			var local = DotNetUtils.getLocalVar(blocks.Locals, ldloc.Instruction);
			if (local == null)
				return false;
			FieldInfo info;
			if (!localToInfo.TryGetValue(local, out info))
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.isLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (ldelem.OpCode.Code != Code.Ldelem_U1)
				return false;

			block.remove(i, 3 - 1);
			instrs[i] = new Instr(DotNetUtils.createLdci4(info.array[ldci4.getLdcI4Value()]));
			return true;
		}

		bool deobfuscate2(Block block, int i) {
			var instrs = block.Instructions;
			if (i >= instrs.Count - 2)
				return false;

			var ldsfld = instrs[i];
			if (ldsfld.OpCode.Code != Code.Ldsfld)
				return false;
			var info = fieldToInfo.find(ldsfld.Operand as FieldReference);
			if (info == null)
				return false;

			var ldci4 = instrs[i + 1];
			if (!ldci4.isLdcI4())
				return false;

			var ldelem = instrs[i + 2];
			if (ldelem.OpCode.Code != Code.Ldelem_U1)
				return false;

			block.remove(i, 3 - 1);
			instrs[i] = new Instr(DotNetUtils.createLdci4(info.array[ldci4.getLdcI4Value()]));
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
			var info = fieldToInfo.find(ldsfld.Operand as FieldReference);
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
			if (stelem.OpCode.Code != Code.Stelem_I1)
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
