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

namespace de4dot.code.deobfuscators {
	public class ConstantsReader {
		protected IInstructions instructions;
		protected IList<Local> locals;
		protected Dictionary<Local, int> localsValuesInt32 = new Dictionary<Local, int>();
		protected Dictionary<Local, long> localsValuesInt64 = new Dictionary<Local, long>();
		protected Dictionary<Local, double> localsValuesDouble = new Dictionary<Local, double>();
		bool emulateConvInstrs;

		public IEnumerable<KeyValuePair<Local, int>> Locals32 {
			get { return localsValuesInt32; }
		}

		public IEnumerable<KeyValuePair<Local, long>> Locals64 {
			get { return localsValuesInt64; }
		}

		public IEnumerable<KeyValuePair<Local, double>> LocalsDouble {
			get { return localsValuesDouble; }
		}

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

		public ConstantsReader(MethodDef method)
			: this(method.Body.Instructions) {
			this.locals = method.Body.Variables;
		}

		public ConstantsReader(IList<Instr> instrs, IList<Local> locals)
			: this(instrs) {
			this.locals = locals;
		}

		public void SetConstantInt32(Local local, int value) {
			localsValuesInt32[local] = value;
		}

		public void SetConstantInt32(Local local, uint value) {
			SetConstantInt32(local, (int)value);
		}

		public void SetConstantInt64(Local local, long value) {
			localsValuesInt64[local] = value;
		}

		public void SetConstantInt64(Local local, ulong value) {
			SetConstantInt64(local, (long)value);
		}

		public void SetConstantDouble(Local local, double value) {
			localsValuesDouble[local] = value;
		}

		public bool GetNextInt32(ref int index, out int val) {
			for (; index < instructions.Count; index++) {
				var instr = instructions[index];
				if (!IsLoadConstantInt32(instr))
					continue;

				return GetInt32(ref index, out val);
			}

			val = 0;
			return false;
		}

		public bool IsLoadConstantInt32(Instruction instr) {
			if (instr.IsLdcI4())
				return true;
			if (instr.IsLdloc()) {
				int tmp;
				return GetLocalConstantInt32(instr, out tmp);
			}
			if (instr.IsLdarg()) {
				int tmp;
				return GetArgConstantInt32(instr, out tmp);
			}
			return false;
		}

		public bool IsLoadConstantInt64(Instruction instr) {
			if (instr.OpCode.Code == Code.Ldc_I8)
				return true;
			if (instr.IsLdloc()) {
				long tmp;
				return GetLocalConstantInt64(instr, out tmp);
			}
			if (instr.IsLdarg()) {
				long tmp;
				return GetArgConstantInt64(instr, out tmp);
			}
			return false;
		}

		public bool IsLoadConstantDouble(Instruction instr) {
			if (instr.OpCode.Code == Code.Ldc_R8)
				return true;
			if (instr.IsLdloc()) {
				double tmp;
				return GetLocalConstantDouble(instr, out tmp);
			}
			if (instr.IsLdarg()) {
				double tmp;
				return GetArgConstantDouble(instr, out tmp);
			}
			return false;
		}

		public bool GetInt16(ref int index, out short val) {
			int tmp;
			if (!GetInt32(ref index, out tmp)) {
				val = 0;
				return false;
			}

			val = (short)tmp;
			return true;
		}

		protected struct ConstantInfo<T> {
			public int index;
			public T constant;
			public ConstantInfo(int index, T constant) {
				this.index = index;
				this.constant = constant;
			}
		}

		protected virtual bool ProcessInstructionInt32(ref int index, Stack<ConstantInfo<int>> stack) {
			return false;
		}

		protected virtual bool ProcessInstructionInt64(ref int index, Stack<ConstantInfo<long>> stack) {
			return false;
		}

		protected virtual bool ProcessInstructionDouble(ref int index, Stack<ConstantInfo<double>> stack) {
			return false;
		}

		public bool GetInt32(ref int index, out int val) {
			val = 0;
			if (index >= instructions.Count)
				return false;

			var stack = new Stack<ConstantInfo<int>>();

			int op1;
			ConstantInfo<int> info1, info2;
			for (; index < instructions.Count; index++) {
				if (ProcessInstructionInt32(ref index, stack)) {
					index--;
					continue;
				}
				var instr = instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Conv_I1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, (sbyte)stack.Pop().constant));
					break;

				case Code.Conv_U1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, (byte)stack.Pop().constant));
					break;

				case Code.Conv_I2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, (short)stack.Pop().constant));
					break;

				case Code.Conv_U2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, (ushort)stack.Pop().constant));
					break;

				case Code.Conv_I4:
				case Code.Conv_U4:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, stack.Pop().constant));
					break;

				case Code.Not:
					if (stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, ~stack.Pop().constant));
					break;

				case Code.Neg:
					if (stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<int>(index, -stack.Pop().constant));
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					if (!GetLocalConstantInt32(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<int>(index, op1));
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (!GetArgConstantInt32(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<int>(index, op1));
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
					stack.Push(new ConstantInfo<int>(index, instr.GetLdcI4Value()));
					break;

				case Code.Add:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant + info2.constant));
					break;

				case Code.Sub:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant - info2.constant));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant ^ info2.constant));
					break;

				case Code.Or:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant | info2.constant));
					break;

				case Code.And:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant & info2.constant));
					break;

				case Code.Mul:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant * info2.constant));
					break;

				case Code.Div:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					if (info2.constant == 0)
						goto done;
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, info1.constant / info2.constant));
					break;

				case Code.Div_Un:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					if (info2.constant == 0)
						goto done;
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<int>(index, (int)((uint)info1.constant / (uint)info2.constant)));
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

		public bool GetInt64(ref int index, out long val) {
			val = 0;
			if (index >= instructions.Count)
				return false;

			var stack = new Stack<ConstantInfo<long>>();

			long op1;
			ConstantInfo<long> info1, info2;
			for (; index < instructions.Count; index++) {
				if (ProcessInstructionInt64(ref index, stack)) {
					index--;
					continue;
				}
				var instr = instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Conv_I1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (sbyte)stack.Pop().constant));
					break;

				case Code.Conv_U1:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (byte)stack.Pop().constant));
					break;

				case Code.Conv_I2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (short)stack.Pop().constant));
					break;

				case Code.Conv_U2:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (ushort)stack.Pop().constant));
					break;

				case Code.Conv_I4:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (int)stack.Pop().constant));
					break;

				case Code.Conv_U4:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, (uint)stack.Pop().constant));
					break;

				case Code.Conv_I8:
				case Code.Conv_U8:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, stack.Pop().constant));
					break;

				case Code.Not:
					if (stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, ~stack.Pop().constant));
					break;

				case Code.Neg:
					if (stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<long>(index, -stack.Pop().constant));
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					if (!GetLocalConstantInt64(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<long>(index, op1));
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (!GetArgConstantInt64(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<long>(index, op1));
					break;

				case Code.Ldc_I8:
					stack.Push(new ConstantInfo<long>(index, (long)instr.Operand));
					break;

				case Code.Add:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant + info2.constant));
					break;

				case Code.Sub:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant - info2.constant));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant ^ info2.constant));
					break;

				case Code.Or:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant | info2.constant));
					break;

				case Code.And:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant & info2.constant));
					break;

				case Code.Mul:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant * info2.constant));
					break;

				case Code.Div:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					if (info2.constant == 0)
						goto done;
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, info1.constant / info2.constant));
					break;

				case Code.Div_Un:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					if (info2.constant == 0)
						goto done;
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<long>(index, (int)((uint)info1.constant / (uint)info2.constant)));
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

		public bool GetDouble(ref int index, out double val) {
			val = 0;
			if (index >= instructions.Count)
				return false;

			var stack = new Stack<ConstantInfo<double>>();

			double op1;
			ConstantInfo<double> info1, info2;
			for (; index < instructions.Count; index++) {
				if (ProcessInstructionDouble(ref index, stack)) {
					index--;
					continue;
				}
				var instr = instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Conv_R4:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<double>(index, (float)stack.Pop().constant));
					break;

				case Code.Conv_R8:
					if (!emulateConvInstrs || stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<double>(index, stack.Pop().constant));
					break;

				case Code.Neg:
					if (stack.Count < 1)
						goto done;
					stack.Push(new ConstantInfo<double>(index, -stack.Pop().constant));
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					if (!GetLocalConstantDouble(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<double>(index, op1));
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					if (!GetArgConstantDouble(instr, out op1))
						goto done;
					stack.Push(new ConstantInfo<double>(index, op1));
					break;

				case Code.Ldc_R4:
					stack.Push(new ConstantInfo<double>(index, (float)instr.Operand));
					break;

				case Code.Ldc_R8:
					stack.Push(new ConstantInfo<double>(index, (double)instr.Operand));
					break;

				case Code.Add:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<double>(index, info1.constant + info2.constant));
					break;

				case Code.Sub:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<double>(index, info1.constant - info2.constant));
					break;

				case Code.Mul:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<double>(index, info1.constant * info2.constant));
					break;

				case Code.Div:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<double>(index, info1.constant / info2.constant));
					break;

				case Code.Div_Un:
					if (stack.Count < 2)
						goto done;
					info2 = stack.Pop();
					info1 = stack.Pop();
					stack.Push(new ConstantInfo<double>(index, (int)((uint)info1.constant / (uint)info2.constant)));
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

		protected virtual bool GetLocalConstantInt32(Instruction instr, out int value) {
			value = 0;
			if (locals == null)
				return false;
			var local = instr.GetLocal(locals);
			if (local == null)
				return false;
			if (local.Type.ElementType != ElementType.I4 && local.Type.ElementType != ElementType.U4)
				return false;
			return localsValuesInt32.TryGetValue(local, out value);
		}

		protected virtual bool GetArgConstantInt32(Instruction instr, out int value) {
			value = 0;
			return false;
		}

		protected virtual bool GetLocalConstantInt64(Instruction instr, out long value) {
			value = 0;
			if (locals == null)
				return false;
			var local = instr.GetLocal(locals);
			if (local == null)
				return false;
			if (local.Type.ElementType != ElementType.I8 && local.Type.ElementType != ElementType.U8)
				return false;
			return localsValuesInt64.TryGetValue(local, out value);
		}

		protected virtual bool GetArgConstantInt64(Instruction instr, out long value) {
			value = 0;
			return false;
		}

		protected virtual bool GetLocalConstantDouble(Instruction instr, out double value) {
			value = 0;
			if (locals == null)
				return false;
			var local = instr.GetLocal(locals);
			if (local == null)
				return false;
			if (local.Type.ElementType != ElementType.R8)
				return false;
			return localsValuesDouble.TryGetValue(local, out value);
		}

		protected virtual bool GetArgConstantDouble(Instruction instr, out double value) {
			value = 0;
			return false;
		}
	}
}
