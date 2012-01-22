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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	class MethodCallInliner : MethodCallInlinerBase {
		InstructionEmulator instructionEmulator = new InstructionEmulator();

		protected override bool deobfuscateInternal() {
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
			if (!canInline(method))
				return false;

			if (instrIndex < 2)
				return false;
			var ldci4_1st = block.Instructions[instrIndex - 2];
			var ldci4_2nd = block.Instructions[instrIndex - 1];
			if (!ldci4_1st.isLdcI4() || !ldci4_2nd.isLdcI4())
				return false;

			if (!inlineMethod(method, instrIndex, ldci4_1st.getLdcI4Value(), ldci4_2nd.getLdcI4Value()))
				return false;

			return true;
		}

		bool inlineMethod(MethodDefinition methodToInline, int instrIndex, int const1, int const2) {
			var parameters = DotNetUtils.getParameters(methodToInline);
			var arg1 = parameters[parameters.Count - 2];
			var arg2 = parameters[parameters.Count - 1];

			instructionEmulator.init(methodToInline.HasImplicitThis, false, methodToInline.Parameters, methodToInline.Body.Variables);
			instructionEmulator.setArg(arg1, new Int32Value(const1));
			instructionEmulator.setArg(arg2, new Int32Value(const2));

			Instruction instr;
			var instrs = methodToInline.Body.Instructions;
			int index = 0;
			int counter = 0;
			while (true) {
				if (counter++ >= 50)
					return false;
				if (index >= instrs.Count)
					return false;
				instr = instrs[index];
				switch (instr.OpCode.Code) {
				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
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
				case Code.Add:
				case Code.Sub:
				case Code.Xor:
				case Code.Or:
				case Code.Nop:
					instructionEmulator.emulate(instr);
					index++;
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					var arg = DotNetUtils.getParameter(parameters, instr);
					if (arg != arg1 && arg != arg2)
						goto checkInline;
					instructionEmulator.emulate(instr);
					index++;
					break;

				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
					goto checkInline;

				case Code.Switch:
					var value = instructionEmulator.pop() as Int32Value;
					if (value == null || !value.allBitsValid())
						return false;
					var targets = (Instruction[])instr.Operand;
					if (value.value >= 0 && value.value < targets.Length)
						index = instrs.IndexOf(targets[value.value]);
					else
						index++;
					break;

				case Code.Br:
				case Code.Br_S:
					index = instrs.IndexOf((Instruction)instr.Operand);
					break;

				default:
					return false;
				}
			}
checkInline:
			if (!inlineOtherMethod(instrIndex, methodToInline, instr, index + 1, 2))
				return false;

			block.insert(instrIndex, Instruction.Create(OpCodes.Pop));
			block.insert(instrIndex, Instruction.Create(OpCodes.Pop));
			return true;
		}

		public static bool canInline(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			if (method.Body.ExceptionHandlers.Count > 0)
				return false;
			var parameters = method.Parameters;
			int paramCount = parameters.Count;
			if (paramCount < 2)
				return false;
			if (parameters[paramCount - 1].ParameterType.FullName != "System.Int32")
				return false;
			if (parameters[paramCount - 2].ParameterType.FullName != "System.Int32")
				return false;

			if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static))
				return false;

			//TODO: Also check that it has a switch statement and an xor instruction

			return true;
		}
	}
}
