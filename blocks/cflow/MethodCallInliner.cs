/*
    Copyright (C) 2011 de4dot@gmail.com

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

namespace de4dot.blocks.cflow {
	class MethodCallInliner {
		Blocks blocks;
		Block block;

		public void init(Blocks blocks, Block block) {
			this.blocks = blocks;
			this.block = block;
		}

		public bool deobfuscate() {
			bool changed = false;
			var instructions = block.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i].Instruction;
				if (instr.OpCode.Code == Code.Call)
					changed |= inlineMethod(instr, i);
			}
			return changed;
		}

		bool inlineMethod(Instruction callInstr, int instrIndex) {
			var method = callInstr.Operand as MethodDefinition;
			if (method == null)
				return false;

			if (!method.IsStatic)
				return false;
			var body = method.Body;
			if (body == null)
				return false;
			if (!MemberReferenceHelper.compareTypes(method.DeclaringType, blocks.Method.DeclaringType))
				return false;

			int index = 0;
			var instr = getInstruction(body.Instructions, ref index);
			if (instr == null)
				return false;

			switch (instr.OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
			case Code.Call:
				return inlineOtherMethod(instrIndex, method, instr, index);

			case Code.Ldc_I4:
			case Code.Ldc_I4_0:
			case Code.Ldc_I4_1:
			case Code.Ldc_I4_2:
			case Code.Ldc_I4_3:
			case Code.Ldc_I4_4:
			case Code.Ldc_I4_5:
			case Code.Ldc_I4_6:
			case Code.Ldc_I4_7:
			case Code.Ldc_I4_8:
			case Code.Ldc_I4_M1:
			case Code.Ldc_I4_S:
			case Code.Ldc_I8:
			case Code.Ldc_R4:
			case Code.Ldc_R8:
			case Code.Ldftn:
			case Code.Ldnull:
			case Code.Ldstr:
			case Code.Ldtoken:
			case Code.Ldsfld:
			case Code.Ldsflda:
				return inlineLoadMethod(instrIndex, method, instr, index);

			default:
				return false;
			}
		}

		bool inlineLoadMethod(int patchIndex, MethodDefinition method, Instruction loadInstr, int instrIndex) {
			var instr = getInstruction(method.Body.Instructions, ref instrIndex);
			if (instr == null || instr.OpCode.Code != Code.Ret)
				return false;

			int methodArgsCount = DotNetUtils.getArgsCount(method);
			for (int i = 0; i < methodArgsCount; i++)
				block.insert(patchIndex++, Instruction.Create(OpCodes.Pop));

			block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(loadInstr));
			return true;
		}

		bool inlineOtherMethod(int patchIndex, MethodDefinition method, Instruction instr, int instrIndex) {
			int loadIndex = 0;
			int methodArgsCount = (method.HasThis ? 1 : 0) + method.Parameters.Count;
			while (instr != null && loadIndex < methodArgsCount) {
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (DotNetUtils.getArgIndex(method, instr) != loadIndex)
						return false;
					loadIndex++;
					instr = getInstruction(method.Body.Instructions, ref instrIndex);
					continue;
				}
				break;
			}
			if (instr == null || loadIndex != methodArgsCount)
				return false;

			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var callInstr = instr;
			var calledMethod = callInstr.Operand as MethodReference;
			if (calledMethod == null)
				return false;

			if (!isCompatibleType(calledMethod.MethodReturnType.ReturnType, method.MethodReturnType.ReturnType))
				return false;
			var methodArgs = DotNetUtils.getArgs(method);
			var calledMethodArgs = DotNetUtils.getArgs(calledMethod);
			if (methodArgs.Count != calledMethodArgs.Count)
				return false;
			for (int i = 0; i < methodArgs.Count; i++) {
				if (!isCompatibleType(calledMethodArgs[i], methodArgs[i]))
					return false;
			}

			instr = getInstruction(method.Body.Instructions, ref instrIndex);
			if (instr == null || instr.OpCode.Code != Code.Ret)
				return false;

			block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(callInstr));
			return true;
		}

		static bool isCompatibleType(TypeReference origType, TypeReference newType) {
			if (MemberReferenceHelper.compareTypes(origType, newType))
				return true;
			if (newType.IsValueType || origType.IsValueType)
				return false;
			return newType.FullName == "System.Object";
		}

		static Instruction getInstruction(IList<Instruction> instructions, ref int index) {
			for (int i = 0; i < 10; i++) {
				if (index < 0 || index >= instructions.Count)
					return null;
				var instr = instructions[index++];
				if (instr.OpCode.Code == Code.Nop)
					continue;
				if (instr == null || (instr.OpCode.Code != Code.Br && instr.OpCode.Code != Code.Br_S))
					return instr;
				instr = instr.Operand as Instruction;
				if (instr == null)
					return null;
				index = instructions.IndexOf(instr);
				if (index < 0)
					return null;
			}
			return null;
		}
	}
}
