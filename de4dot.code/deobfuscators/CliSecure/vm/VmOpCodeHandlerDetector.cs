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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	class OpCodeHandlerSigInfo {
		public object[] RequiredFieldTypes { get; set; }
		public string[] ExecuteMethodLocals { get; set; }
		public int? ExecuteMethodThrows { get; set; }
		public int? ExecuteMethodPops { get; set; }
		public int? NumStaticMethods { get; set; }
		public int? NumInstanceMethods { get; set; }
		public int? NumVirtualMethods { get; set; }
		public int? NumCtors { get; set; }
	}

	class CsvmInfo {
		public TypeDefinition StackValue { get; set; }
		public TypeDefinition Stack { get; set; }
		public MethodDefinition PopMethod { get; set; }
		public MethodDefinition PeekMethod { get; set; }
	}

	class VmOpCodeHandlerDetector {
		ModuleDefinition module;
		List<OpCodeHandler> opCodeHandlers;

		public List<OpCodeHandler> Handlers {
			get { return opCodeHandlers; }
		}

		public VmOpCodeHandlerDetector(ModuleDefinition module) {
			this.module = module;
		}

		public void findHandlers() {
			if (opCodeHandlers != null)
				return;
			var vmHandlerTypes = findVmHandlerTypes();
			if (vmHandlerTypes == null)
				throw new ApplicationException("Could not find CSVM opcode handler types");

			detectHandlers(vmHandlerTypes, createCsvmInfo());
		}

		internal CsvmInfo createCsvmInfo() {
			var csvmInfo = new CsvmInfo();
			csvmInfo.StackValue = findStackValueType();
			csvmInfo.Stack = findStackType(csvmInfo.StackValue);
			initStackTypeMethods(csvmInfo);
			return csvmInfo;
		}

		TypeDefinition findStackValueType() {
			foreach (var type in module.Types) {
				if (isStackType(type))
					return type;
			}
			return null;
		}

		static bool isStackType(TypeDefinition type) {
			if (type.Fields.Count != 2)
				return false;

			int enumTypes = 0;
			int objectTypes = 0;
			foreach (var field in type.Fields) {
				var fieldType = field.FieldType as TypeDefinition;
				if (fieldType != null && fieldType.IsEnum)
					enumTypes++;
				if (field.FieldType.FullName == "System.Object")
					objectTypes++;
			}
			if (enumTypes != 1 || objectTypes != 1)
				return false;

			return true;
		}

		TypeDefinition findStackType(TypeDefinition stackValueType) {
			foreach (var type in module.Types) {
				if (isStackType(type, stackValueType))
					return type;
			}
			return null;
		}

		bool isStackType(TypeDefinition type, TypeDefinition stackValueType) {
			if (type.Interfaces.Count != 2)
				return false;
			if (!implementsInterface(type, "System.Collections.ICollection"))
				return false;
			if (!implementsInterface(type, "System.Collections.IEnumerable"))
				return false;
			if (type.NestedTypes.Count == 0)
				return false;

			int stackValueTypes = 0;
			int int32Types = 0;
			int objectTypes = 0;
			foreach (var field in type.Fields) {
				if (field.IsLiteral)
					continue;
				if (field.FieldType is ArrayType && ((ArrayType)field.FieldType).ElementType == stackValueType)
					stackValueTypes++;
				if (field.FieldType.FullName == "System.Int32")
					int32Types++;
				if (field.FieldType.FullName == "System.Object")
					objectTypes++;
			}
			if (stackValueTypes != 2 || int32Types != 2 || objectTypes != 1)
				return false;

			return true;
		}

		static bool implementsInterface(TypeDefinition type, string ifaceName) {
			foreach (var iface in type.Interfaces) {
				if (iface.FullName == ifaceName)
					return true;
			}
			return false;
		}

		void initStackTypeMethods(CsvmInfo csvmInfo) {
			foreach (var method in csvmInfo.Stack.Methods) {
				if (method.Parameters.Count == 0 && method.MethodReturnType.ReturnType == csvmInfo.StackValue) {
					if (hasAdd(method))
						csvmInfo.PopMethod = method;
					else
						csvmInfo.PeekMethod = method;
				}
			}
		}

		static bool hasAdd(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Add)
					return true;
			}
			return false;
		}

		List<TypeDefinition> findVmHandlerTypes() {
			var requiredFields = new string[] {
				null,
				"System.Collections.Generic.Dictionary`2<System.UInt16,System.Type>",
				"System.UInt16",
			};
			var cflowDeobfuscator = new CflowDeobfuscator();
			foreach (var type in module.Types) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;
				requiredFields[0] = type.FullName;
				if (!new FieldTypes(type).exactly(requiredFields))
					continue;

				cflowDeobfuscator.deobfuscate(cctor);
				var handlers = findVmHandlerTypes(cctor);
				if (handlers.Count != 31)
					continue;

				return handlers;
			}

			return null;
		}

		static List<TypeDefinition> findVmHandlerTypes(MethodDefinition method) {
			var list = new List<TypeDefinition>();

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var type = instr.Operand as TypeDefinition;
				if (type == null)
					continue;

				list.Add(type);
			}

			return list;
		}

		void detectHandlers(List<TypeDefinition> handlerTypes, CsvmInfo csvmInfo) {
			opCodeHandlers = new List<OpCodeHandler>();
			var detected = new List<OpCodeHandler>();

			foreach (var handlersList in OpCodeHandlers.opcodeHandlers) {
				opCodeHandlers.Clear();

				foreach (var handlerType in handlerTypes) {
					var info = new UnknownHandlerInfo(handlerType, csvmInfo);
					detected.Clear();
					foreach (var opCodeHandler in handlersList) {
						if (opCodeHandler.detect(info))
							detected.Add(opCodeHandler);
					}
					if (detected.Count != 1)
						goto next;
					opCodeHandlers.Add(detected[0]);
				}
				if (new List<OpCodeHandler>(Utils.unique(opCodeHandlers)).Count == opCodeHandlers.Count)
					return;
next: ;
			}
			throw new ApplicationException("Could not detect all VM opcode handlers");
		}
	}
}
