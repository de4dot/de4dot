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

namespace de4dot.code.deobfuscators.DeepSea {
	class ArrayBlockState {
		ModuleDefinition module;
		FieldDefinitionAndDeclaringTypeDict<FieldInfo> fieldToInfo = new FieldDefinitionAndDeclaringTypeDict<FieldInfo>();

		public class FieldInfo {
			public readonly FieldDefinition field;
			public readonly FieldDefinition arrayInitField;
			public readonly byte[] array;

			public FieldInfo(FieldDefinition field, FieldDefinition arrayInitField) {
				this.field = field;
				this.arrayInitField = arrayInitField;
				this.array = (byte[])arrayInitField.InitialValue.Clone();
			}
		}

		public bool Detected {
			get { return fieldToInfo.Count != 0; }
		}

		public ArrayBlockState(ModuleDefinition module) {
			this.module = module;
		}

		public void init(ISimpleDeobfuscator simpleDeobfuscator) {
			initializeArrays(simpleDeobfuscator, DotNetUtils.getModuleTypeCctor(module));
		}

		void initializeArrays(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition method) {
			if (method == null || method.Body == null)
				return;
			while (initializeArrays2(simpleDeobfuscator, method)) {
			}
		}

		bool initializeArrays2(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition method) {
			bool foundField = false;
			simpleDeobfuscator.deobfuscate(method, true);
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

				if (fieldToInfo.find(targetField) == null) {
					fieldToInfo.add(targetField, new FieldInfo(targetField, arrayInitField));
					foundField = true;
				}
			}
			return foundField;
		}

		public FieldInfo getFieldInfo(FieldReference fieldRef) {
			if (fieldRef == null)
				return null;
			return fieldToInfo.find(fieldRef);
		}

		public IEnumerable<FieldDefinition> cleanUp() {
			var removedFields = new List<FieldDefinition>();
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
				fieldInfo.arrayInitField.FieldType = module.TypeSystem.Byte;
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
					var calledMethod = call.Operand as MethodReference;
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
						var field = instr.Operand as FieldReference;
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
