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

namespace de4dot.code.deobfuscators {
	class ConstantsReader {
		protected IInstructions instructions;
		protected IList<VariableDefinition> locals;
		protected Dictionary<VariableDefinition, int> localsValues = new Dictionary<VariableDefinition, int>();
		bool emulateConvInstrs;

		public interface IInstructions {
			int Count { get; }
			Instruction this[int index] { get; }
		}

		class ListInstructions : IInstructions {
			IList<Instruction> instrs;

			public int Count {
				get { return instrs.Count; }
			}

			public Instruction this[int index] {
				get { return instrs[index]; }
			}

			public ListInstructions(IList<Instruction> instrs) {
				this.instrs = instrs;
			}
		}

		class ListInstrs : IInstructions {
			IList<Instr> instrs;

			public int Count {
				get { return instrs.Count; }
			}

			public Instruction this[int index] {
				get { return instrs[index].Instruction; }
			}

			public ListInstrs(IList<Instr> instrs) {
				this.instrs = instrs;
			}
		}

		public bool EmulateConvInstructions {
			get { return emulateConvInstrs; }
			set { emulateConvInstrs = value; }
		}

		ConstantsReader(IInstructions instructions)
			: this(instructions, true) {
		}

		ConstantsReader(IInstructions instructions, bool emulateConvInstrs) {
			this.instructions = instructions;
			this.emulateConvInstrs = emulateConvInstrs;
		}

		public ConstantsReader(IList<Instruction> instrs)
			: this(new ListInstructions(instrs)) {
		}

		public ConstantsReader(IList<Instruction> instrs, bool emulateConvInstrs)
			: this(new ListInstructions(instrs), emulateConvInstrs) {
		}

		public ConstantsReader(IList<Instr> instrs)
			: this(new ListInstrs(instrs)) {
		}

		public ConstantsReader(IList<Instr> instrs, bool emulateConvInstrs)
			: this(new ListInstrs(instrs), emulateConvInstrs) {
		}

		public ConstantsReader(MethodDefinition method)
			: this(method.Body.Instructions) {
			this.locals = method.Body.Variables;
		}

		public ConstantsReader(IList<Instr> instrs, IList<VariableDefinition> locals)
			: this(instrs) {
			this.locals = locals;
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
			if (DotNetUtils.isLdloc(instr)) {
				int tmp;
				return getLocalConstant(instr, out tmp);
			}
			if (DotNetUtils.isLdarg(instr)) {
				int tmp;
				return getArgConstant(instr, out tmp);
			}
			return false;
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

		struct ConstantInfo {
			public int index;
			public int constant;
			public ConstantInfo(int index, int constant) {
				this.index = index;
				this.constant = constant;
			}
		}

		public bool getInt32(ref int index, out int val) {
			val = 0;
			if (index >= instructions.Count)
				return false;

			var stack = new Stack<ConstantInfo>();

			int op1;
			ConstantInfo info1, info2;
			for (; index < instructions.Count; index++) {
				var instr = instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Conv_I1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo(index, (sbyte)stack.Pop().constant));
					break;

				case Code.Conv_U1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo(index, (byte)stack.Pop().constant));
					break;

				case Code.Conv_I2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo(index, (short)stack.Pop().constant));
					break;

				case Code.Conv_U2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo(index, (ushort)stack.Pop().constant));
					break;

				case Code.Conv_I4:
				case Code.Conv_U4:
					if (!emulateConvInstrs)
						goto done;
					stack.Push(new ConstantInfo(index, stack.Pop().constant));
					break;

				case Code.Not:
					stack.Push(new ConstantInfo(index, ~stack.Pop().constant));
					break;

				case Code.Neg:
					stack.Push(new ConstantInfo(index, -stack.Pop().constant));
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					if (!getLocalConstant(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo(index, op1));
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (!getArgConstant(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo(index, op1));
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
					stack.Push(new ConstantInfo(index, DotNetUtils.getLdcI4Value(instr)));
					break;

				case Code.Add:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant + info2.constant));
					break;

				case Code.Sub:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant - info2.constant));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant ^ info2.constant));
					break;

				case Code.Or:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant | info2.constant));
					break;

				case Code.And:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant & info2.constant));
					break;

				case Code.Mul:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant * info2.constant));
					break;

				case Code.Div:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, info1.constant / info2.constant));
					break;

				case Code.Div_Un:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo(index, (int)((uint)info1.constant / (uint)info2.constant)));
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
			info1 = stack.Pop();
			index = info1.index + 1;
			val = info1.constant;
			return true;
		}

		protected virtual bool getLocalConstant(Instruction instr, out int value) {
			value = 0;
			if (locals == null)
				return false;
			var local = DotNetUtils.getLocalVar(locals, instr);
			if (local == null)
				return false;
			if (local.VariableType.EType != ElementType.I4)
				return false;
			return localsValues.TryGetValue(local, out value);
		}

		protected virtual bool getArgConstant(Instruction instr, out int value) {
			value = 0;
			return false;
		}
	}
}
