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

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class ConstantsReader {
		IList<Instruction> instructions;
		IList<VariableDefinition> locals;
		Dictionary<VariableDefinition, int> localsValues = new Dictionary<VariableDefinition, int>();

		public ConstantsReader(MethodDefinition method) {
			instructions = method.Body.Instructions;
			locals = method.Body.Variables;
			initialize();
		}

		void initialize() {
			findConstants();
		}

		void findConstants() {
			for (int index = 0; index < instructions.Count; ) {
				int value;
				if (!getInt32(ref index, out value))
					break;
				var stloc = instructions[index];
				if (!DotNetUtils.isStloc(stloc))
					break;
				var local = DotNetUtils.getLocalVar(locals, stloc);
				if (local == null || local.VariableType.EType != ElementType.I4)
					break;
				localsValues[local] = value;
				index++;
			}

			if (localsValues.Count != 2)
				localsValues.Clear();
		}

		public bool getNextInt32(ref int index, out int val) {
			for (; index < instructions.Count; index++) {
				var instr = instructions[index];
				if (!isLoadConstant(instr))
					continue;

				return getInt32(ref index, out val);
			}

			val = 0;
			return false;
		}

		public bool isLoadConstant(Instruction instr) {
			if (DotNetUtils.isLdcI4(instr))
				return true;
			if (!DotNetUtils.isLdloc(instr))
				return false;
			int tmp;
			return getLocalConstant(instr, out tmp);
		}

		public bool getInt16(ref int index, out short val) {
			int tmp;
			if (!getInt32(ref index, out tmp)) {
				val = 0;
				return false;
			}

			val = (short)tmp;
			return true;
		}

		public bool getInt32(ref int index, out int val) {
			val = 0;
			if (index >= instructions.Count)
				return false;

			var stack = new Stack<int>();

			int op1;
			for (; index < instructions.Count; index++) {
				var instr = instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Conv_I1:
					if (stack.Count < 1)
						goto done;
					stack.Push((sbyte)stack.Pop());
					break;

				case Code.Conv_U1:
					if (stack.Count < 1)
						goto done;
					stack.Push((byte)stack.Pop());
					break;

				case Code.Conv_I2:
					if (stack.Count < 1)
						goto done;
					stack.Push((short)stack.Pop());
					break;

				case Code.Conv_U2:
					if (stack.Count < 1)
						goto done;
					stack.Push((ushort)stack.Pop());
					break;

				case Code.Conv_I4:
				case Code.Conv_U4:
					break;

				case Code.Not:
					stack.Push(~stack.Pop());
					break;

				case Code.Neg:
					stack.Push(-stack.Pop());
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					if (!getLocalConstant(instr, out op1))
						goto done;
					stack.Push(op1);
					break;

				case Code.Ldc_I4:
				case Code.Ldc_I4_S:
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
					stack.Push(DotNetUtils.getLdcI4Value(instr));
					break;

				case Code.Add:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() + stack.Pop());
					break;

				case Code.Sub:
					if (stack.Count < 2)
						goto done;
					stack.Push(-(stack.Pop() - stack.Pop()));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() ^ stack.Pop());
					break;

				case Code.Or:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() | stack.Pop());
					break;

				case Code.And:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() & stack.Pop());
					break;

				default:
					goto done;
				}
			}
done:
			if (stack.Count == 0)
				return false;
			while (stack.Count > 1)
				stack.Pop();
			val = stack.Pop();
			return true;
		}

		bool getLocalConstant(Instruction instr, out int value) {
			value = 0;
			var local = DotNetUtils.getLocalVar(locals, instr);
			if (local == null)
				return false;
			if (local.VariableType.EType != ElementType.I4)
				return false;
			return localsValues.TryGetValue(local, out value);
		}
	}
}
