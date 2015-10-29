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

namespace de4dot.code.deobfuscators {
	public static class ArrayFinder {
		public static List<byte[]> GetArrays(MethodDef method) {
			return GetArrays(method, null);
		}

		public static List<byte[]> GetArrays(MethodDef method, IType arrayElementType) {
			var arrays = new List<byte[]>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				IType type;
				var ary = GetArray(instrs, ref i, out type);
				if (ary == null)
					break;
				if (arrayElementType != null && !new SigComparer().Equals(type, arrayElementType))
					continue;

				arrays.Add(ary);
			}
			return arrays;
		}

		public static byte[] GetArray(IList<Instruction> instrs, ref int index, out IType type) {
			for (int i = index; i < instrs.Count - 2; i++) {
				var newarr = instrs[i++];
				if (newarr.OpCode.Code != Code.Newarr)
					continue;

				if (instrs[i++].OpCode.Code != Code.Dup)
					continue;

				var ldtoken = instrs[i++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDef;
				if (field == null || field.InitialValue == null)
					continue;

				index = i - 3;
				type = newarr.Operand as IType;
				return field.InitialValue;
			}

			index = instrs.Count;
			type = null;
			return null;
		}

		public static byte[] GetInitializedByteArray(MethodDef method, int arraySize) {
			int newarrIndex = FindNewarr(method, arraySize);
			if (newarrIndex < 0)
				return null;
			return GetInitializedByteArray(arraySize, method, ref newarrIndex);
		}

		public static byte[] GetInitializedByteArray(int arraySize, MethodDef method, ref int newarrIndex) {
			var resultValueArray = GetInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I1);

			var resultArray = new byte[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.AllBitsValid())
					return null;
				resultArray[i] = (byte)intValue.Value;
			}
			return resultArray;
		}

		public static short[] GetInitializedInt16Array(int arraySize, MethodDef method, ref int newarrIndex) {
			var resultValueArray = GetInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I2);

			var resultArray = new short[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.AllBitsValid())
					return null;
				resultArray[i] = (short)intValue.Value;
			}
			return resultArray;
		}

		public static int[] GetInitializedInt32Array(int arraySize, MethodDef method, ref int newarrIndex) {
			var resultValueArray = GetInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I4);

			var resultArray = new int[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.AllBitsValid())
					return null;
				resultArray[i] = (int)intValue.Value;
			}
			return resultArray;
		}

		public static uint[] GetInitializedUInt32Array(int arraySize, MethodDef method, ref int newarrIndex) {
			var resultArray = GetInitializedInt32Array(arraySize, method, ref newarrIndex);
			if (resultArray == null)
				return null;

			var ary = new uint[resultArray.Length];
			for (int i = 0; i < ary.Length; i++)
				ary[i] = (uint)resultArray[i];
			return ary;
		}

		public static Value[] GetInitializedArray(int arraySize, MethodDef method, ref int newarrIndex, Code stelemOpCode) {
			var resultValueArray = new Value[arraySize];

			var emulator = new InstructionEmulator(method);
			var theArray = new UnknownValue();
			emulator.Push(theArray);

			var instructions = method.Body.Instructions;
			int i;
			for (i = newarrIndex + 1; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (instr.OpCode.Code == Code.Newarr)
					break;
				switch (instr.OpCode.Code) {
				case Code.Newarr:
				case Code.Newobj:
					goto done;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Starg:
				case Code.Starg_S:
				case Code.Stsfld:
				case Code.Stfld:
					if (emulator.Peek() == theArray && i != newarrIndex + 1 && i != newarrIndex + 2)
						goto done;
					break;
				}

				if (instr.OpCode.Code == stelemOpCode) {
					var value = emulator.Pop();
					var index = emulator.Pop() as Int32Value;
					var array = emulator.Pop();
					if (ReferenceEquals(array, theArray) && index != null && index.AllBitsValid()) {
						if (0 <= index.Value && index.Value < resultValueArray.Length)
							resultValueArray[index.Value] = value;
					}
				}
				else
					emulator.Emulate(instr);
			}
done:
			if (i != newarrIndex + 1)
				i--;
			newarrIndex = i;

			return resultValueArray;
		}

		static int FindNewarr(MethodDef method, int arraySize) {
			for (int i = 0; ; i++) {
				int size;
				if (!FindNewarr(method, ref i, out size))
					return -1;
				if (size == arraySize)
					return i;
			}
		}

		public static bool FindNewarr(MethodDef method, ref int i, out int size) {
			var instructions = method.Body.Instructions;
			for (; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.Code != Code.Newarr || i < 1)
					continue;
				var ldci4 = instructions[i - 1];
				if (!ldci4.IsLdcI4())
					continue;

				size = ldci4.GetLdcI4Value();
				return true;
			}

			size = -1;
			return false;
		}
	}
}
