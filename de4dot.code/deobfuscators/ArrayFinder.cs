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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators {
	class ArrayFinder {
		List<byte[]> arrays = new List<byte[]>();

		public ArrayFinder(MethodDefinition method) {
			init(method);
		}

		void init(MethodDefinition method) {
			if (method.Body == null)
				return;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDefinition;
				if (field == null)
					continue;
				arrays.Add(field.InitialValue);
			}

			var instructions = method.Body.Instructions;
			for (int i = 1; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.Code != Code.Newarr)
					continue;
				var ldci4 = instructions[i - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				int arraySize = DotNetUtils.getLdcI4Value(ldci4);
				var ary = getInitializedByteArray(arraySize, method, ref i);
				if (ary != null)
					arrays.Add(ary);
			}
		}

		public bool exists(byte[] array) {
			foreach (var ary in arrays) {
				if (isEqual(ary, array))
					return true;
			}
			return false;
		}

		static bool isEqual(byte[] ary1, byte[] ary2) {
			if (ary1.Length != ary2.Length)
				return false;
			for (int i = 0; i < ary1.Length; i++) {
				if (ary1[i] != ary2[i])
					return false;
			}
			return true;
		}

		public static byte[] getInitializedByteArray(MethodDefinition method, int arraySize) {
			int newarrIndex = findNewarr(method, arraySize);
			if (newarrIndex < 0)
				return null;
			return getInitializedByteArray(arraySize, method, ref newarrIndex);
		}

		public static byte[] getInitializedByteArray(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultValueArray = getInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I1);

			var resultArray = new byte[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (byte)intValue.value;
			}
			return resultArray;
		}

		public static int[] getInitializedInt32Array(int arraySize, MethodDefinition method, ref int newarrIndex) {
			var resultValueArray = getInitializedArray(arraySize, method, ref newarrIndex, Code.Stelem_I4);

			var resultArray = new int[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (int)intValue.value;
			}
			return resultArray;
		}

		public static Value[] getInitializedArray(int arraySize, MethodDefinition method, ref int newarrIndex, Code stelemOpCode) {
			var resultValueArray = new Value[arraySize];

			var emulator = new InstructionEmulator(method.HasThis, false, method.Parameters, method.Body.Variables);
			var theArray = new UnknownValue();
			emulator.push(theArray);

			var instructions = method.Body.Instructions;
			int i;
			for (i = newarrIndex + 1; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (instr.OpCode.Code == Code.Newarr)
					break;

				if (instr.OpCode.Code == stelemOpCode) {
					var value = emulator.pop();
					var index = emulator.pop() as Int32Value;
					var array = emulator.pop();
					if (ReferenceEquals(array, theArray) && index != null && index.allBitsValid()) {
						if (0 <= index.value && index.value < resultValueArray.Length)
							resultValueArray[index.value] = value;
					}
				}
				else
					emulator.emulate(instr);
			}
			if (i != newarrIndex + 1)
				i--;
			newarrIndex = i;

			return resultValueArray;
		}

		static int findNewarr(MethodDefinition method, int arraySize) {
			for (int i = 0; ; i++) {
				int size;
				if (!findNewarr(method, ref i, out size))
					return -1;
				if (size == arraySize)
					return i;
			}
		}

		public static bool findNewarr(MethodDefinition method, ref int i, out int size) {
			var instructions = method.Body.Instructions;
			for (; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.Code != Code.Newarr || i < 1)
					continue;
				var ldci4 = instructions[i - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				size = DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			size = -1;
			return false;
		}
	}
}
