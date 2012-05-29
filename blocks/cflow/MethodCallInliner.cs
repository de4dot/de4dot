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

namespace de4dot.blocks.cflow {
	public class MethodCallInliner : MethodCallInlinerBase {
		protected readonly bool inlineInstanceMethods;

		public MethodCallInliner(bool inlineInstanceMethods) {
			this.inlineInstanceMethods = inlineInstanceMethods;
		}

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

		protected virtual bool canInline(MethodDefinition method) {
			if (method.GenericParameters.Count > 0)
				return false;
			if (method == blocks.Method)
				return false;
			if (!MemberReferenceHelper.compareTypes(method.DeclaringType, blocks.Method.DeclaringType))
				return false;

			if (method.IsStatic)
				return true;
			if (method.IsVirtual)
				return false;
			return inlineInstanceMethods;
		}

		bool inlineMethod(Instruction callInstr, int instrIndex) {
			var methodToInline = callInstr.Operand as MethodDefinition;
			if (methodToInline == null)
				return false;

			if (!canInline(methodToInline))
				return false;
			var body = methodToInline.Body;
			if (body == null)
				return false;

			int index = 0;
			var instr = DotNetUtils.getInstruction(body.Instructions, ref index);
			if (instr == null)
				return false;

			switch (instr.OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
			case Code.Ldarga:
			case Code.Ldarga_S:
			case Code.Call:
			case Code.Callvirt:
			case Code.Newobj:
				return inlineOtherMethod(instrIndex, methodToInline, instr, index);

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
				return inlineLoadMethod(instrIndex, methodToInline, instr, index);

			default:
				return false;
			}
		}

		protected override bool isCompatibleType(int paramIndex, TypeReference origType, TypeReference newType) {
			if (MemberReferenceHelper.compareTypes(origType, newType))
				return true;
			if (newType.IsValueType || origType.IsValueType)
				return false;
			return newType.FullName == "System.Object";
		}
	}
}
