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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class ArrayBlockState {
		ModuleDefMD module;
		FieldDefAndDeclaringTypeDict<FieldInfo> fieldToInfo = new FieldDefAndDeclaringTypeDict<FieldInfo>();

		public class FieldInfo {
			public readonly ElementType elementType;
			public readonly FieldDef field;
			public readonly FieldDef arrayInitField;
			public readonly Array array;

			public FieldInfo(FieldDef field, FieldDef arrayInitField) {
				this.field = field;
				this.elementType = ((SZArraySig)field.FieldType).Next.GetElementType();
				this.arrayInitField = arrayInitField;
				this.array = createArray(elementType, arrayInitField.InitialValue);
			}

			static Array createArray(ElementType etype, byte[] data) {
				switch (etype) {
				case ElementType.Boolean:
				case ElementType.I1:
				case ElementType.U1:
					return (byte[])data.Clone();

				case ElementType.Char:
				case ElementType.I2:
				case ElementType.U2:
					var ary2 = new ushort[data.Length / 2];
					Buffer.BlockCopy(data, 0, ary2, 0, ary2.Length * 2);
					return ary2;

				case ElementType.I4:
				case ElementType.U4:
					var ary4 = new uint[data.Length / 4];
					Buffer.BlockCopy(data, 0, ary4, 0, ary4.Length * 4);
					return ary4;

				default:
					throw new ApplicationException("Invalid etype");
				}
			}

			public uint readArrayElement(int index) {
				switch (elementType) {
				case ElementType.Boolean:
				case ElementType.I1:
				case ElementType.U1:
					return ((byte[])array)[index];

				case ElementType.Char:
				case ElementType.I2:
				case ElementType.U2:
					return ((ushort[])array)[index];

				case ElementType.I4:
				case ElementType.U4:
					return ((uint[])array)[index];

				default:
					throw new ApplicationException("Invalid etype");
				}
			}
		}

		public bool Detected {
			get { return fieldToInfo.Count != 0; }
		}

		public ArrayBlockState(ModuleDefMD module) {
			this.module = module;
		}

		public void init(ISimpleDeobfuscator simpleDeobfuscator) {
			initializeArrays(simpleDeobfuscator, DotNetUtils.getModuleTypeCctor(module));
		}

		void initializeArrays(ISimpleDeobfuscator simpleDeobfuscator, MethodDef method) {
			if (method == null || method.Body == null)
				return;
			while (initializeArrays2(simpleDeobfuscator, method)) {
			}
		}

		bool initializeArrays2(ISimpleDeobfuscator simpleDeobfuscator, MethodDef method) {
			bool foundField = false;
			simpleDeobfuscator.deobfuscate(method, true);
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var ldci4 = instructions[i];
				if (!ldci4.IsLdcI4())
					continue;
				i++;
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Newarr, OpCodes.Dup, OpCodes.Ldtoken, OpCodes.Call, OpCodes.Stsfld);
				if (instrs == null)
					continue;

				var arrayInitField = instrs[2].Operand as FieldDef;
				if (arrayInitField == null || arrayInitField.InitialValue == null || arrayInitField.InitialValue.Length == 0)
					continue;

				var calledMethod = instrs[3].Operand as IMethod;
				if (calledMethod == null || calledMethod.FullName != "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
					continue;

				var targetField = instrs[4].Operand as FieldDef;
				if (targetField == null || targetField.FieldType.GetElementType() != ElementType.SZArray)
					continue;
				var etype = ((SZArraySig)targetField.FieldType).Next.GetElementType();
				if (etype < ElementType.Boolean || etype > ElementType.U4)
					continue;

				if (fieldToInfo.find(targetField) == null) {
					fieldToInfo.add(targetField, new FieldInfo(targetField, arrayInitField));
					foundField = true;
				}
			}
			return foundField;
		}

		public FieldInfo getFieldInfo(IField fieldRef) {
			if (fieldRef == null)
				return null;
			return fieldToInfo.find(fieldRef);
		}

		public IEnumerable<FieldDef> cleanUp() {
			var removedFields = new List<FieldDef>();
			var moduleCctor = DotNetUtils.getModuleTypeCctor(module);
			if (moduleCctor == null)
				return removedFields;
			var moduleCctorBlocks = new Blocks(moduleCctor);

			var keep = findFieldsToKeep();
			foreach (var fieldInfo in fieldToInfo.getValues()) {
				if (keep.ContainsKey(fieldInfo))
					continue;
				if (removeInitCode(moduleCctorBlocks, fieldInfo)) {
					removedFields.Add(fieldInfo.field);
					removedFields.Add(fieldInfo.arrayInitField);
				}
				fieldInfo.arrayInitField.InitialValue = new byte[1];
				fieldInfo.arrayInitField.FieldSig.Type = module.CorLibTypes.Byte;
				fieldInfo.arrayInitField.RVA = 0;
			}

			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			moduleCctorBlocks.getCode(out allInstructions, out allExceptionHandlers);
			DotNetUtils.restoreBody(moduleCctorBlocks.Method, allInstructions, allExceptionHandlers);
			return removedFields;
		}

		bool removeInitCode(Blocks blocks, FieldInfo info) {
			bool removedSomething = false;
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					var ldci4 = instrs[i];
					if (!ldci4.isLdcI4())
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Newarr)
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Dup)
						continue;
					var ldtoken = instrs[i + 3];
					if (ldtoken.OpCode.Code != Code.Ldtoken)
						continue;
					if (ldtoken.Operand != info.arrayInitField)
						continue;
					var call = instrs[i + 4];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as IMethod;
					if (calledMethod == null || calledMethod.FullName != "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
						continue;
					var stsfld = instrs[i + 5];
					if (stsfld.OpCode.Code != Code.Stsfld)
						continue;
					if (stsfld.Operand != info.field)
						continue;
					block.remove(i, 6);
					i--;
					removedSomething = true;
				}
			}
			return removedSomething;
		}

		Dictionary<FieldInfo, bool> findFieldsToKeep() {
			var keep = new Dictionary<FieldInfo, bool>();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (type == DotNetUtils.getModuleType(module) && method.Name == ".cctor")
						continue;
					if (method.Body == null)
						continue;

					foreach (var instr in method.Body.Instructions) {
						var field = instr.Operand as IField;
						if (field == null)
							continue;
						var fieldInfo = fieldToInfo.find(field);
						if (fieldInfo == null)
							continue;
						keep[fieldInfo] = true;
					}
				}
			}
			return keep;
		}
	}
}
