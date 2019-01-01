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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v1 {
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
		public TypeDef StackValue { get; set; }
		public TypeDef Stack { get; set; }
		public MethodDef PopMethod { get; set; }
		public MethodDef PeekMethod { get; set; }
	}

	class VmOpCodeHandlerDetector {
		ModuleDefMD module;
		List<OpCodeHandler> opCodeHandlers;

		public List<OpCodeHandler> Handlers => opCodeHandlers;
		public VmOpCodeHandlerDetector(ModuleDefMD module) => this.module = module;

		public void FindHandlers() {
			if (opCodeHandlers != null)
				return;
			var vmHandlerTypes = FindVmHandlerTypes();
			if (vmHandlerTypes == null)
				throw new ApplicationException("Could not find CSVM opcode handler types");

			DetectHandlers(vmHandlerTypes, CreateCsvmInfo());
		}

		internal CsvmInfo CreateCsvmInfo() {
			var csvmInfo = new CsvmInfo();
			csvmInfo.StackValue = FindStackValueType();
			csvmInfo.Stack = FindStackType(csvmInfo.StackValue);
			InitStackTypeMethods(csvmInfo);
			return csvmInfo;
		}

		TypeDef FindStackValueType() {
			foreach (var type in module.Types) {
				if (IsStackType(type))
					return type;
			}
			return null;
		}

		static bool IsStackType(TypeDef type) {
			if (type.Fields.Count != 2)
				return false;

			int enumTypes = 0;
			int objectTypes = 0;
			foreach (var field in type.Fields) {
				var fieldType = field.FieldSig.GetFieldType().TryGetTypeDef();
				if (fieldType != null && fieldType.IsEnum)
					enumTypes++;
				if (field.FieldSig.GetFieldType().GetElementType() == ElementType.Object)
					objectTypes++;
			}
			if (enumTypes != 1 || objectTypes != 1)
				return false;

			return true;
		}

		TypeDef FindStackType(TypeDef stackValueType) {
			foreach (var type in module.Types) {
				if (IsStackType(type, stackValueType))
					return type;
			}
			return null;
		}

		bool IsStackType(TypeDef type, TypeDef stackValueType) {
			if (type.Interfaces.Count != 2)
				return false;
			if (!ImplementsInterface(type, "System.Collections.ICollection"))
				return false;
			if (!ImplementsInterface(type, "System.Collections.IEnumerable"))
				return false;
			if (type.NestedTypes.Count == 0)
				return false;

			int stackValueTypes = 0;
			int int32Types = 0;
			int objectTypes = 0;
			foreach (var field in type.Fields) {
				if (field.IsLiteral)
					continue;
				var fieldType = field.FieldSig.GetFieldType();
				if (fieldType == null)
					continue;
				if (fieldType.IsSZArray && fieldType.Next.TryGetTypeDef() == stackValueType)
					stackValueTypes++;
				if (fieldType.ElementType == ElementType.I4)
					int32Types++;
				if (fieldType.ElementType == ElementType.Object)
					objectTypes++;
			}
			if (stackValueTypes != 2 || int32Types != 2 || objectTypes != 1)
				return false;

			return true;
		}

		static bool ImplementsInterface(TypeDef type, string ifaceName) {
			foreach (var iface in type.Interfaces) {
				if (iface.Interface.FullName == ifaceName)
					return true;
			}
			return false;
		}

		void InitStackTypeMethods(CsvmInfo csvmInfo) {
			foreach (var method in csvmInfo.Stack.Methods) {
				var sig = method.MethodSig;
				if (sig != null && sig.Params.Count == 0 && sig.RetType.TryGetTypeDef() == csvmInfo.StackValue) {
					if (HasAdd(method))
						csvmInfo.PopMethod = method;
					else
						csvmInfo.PeekMethod = method;
				}
			}
		}

		static bool HasAdd(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Add)
					return true;
			}
			return false;
		}

		List<TypeDef> FindVmHandlerTypes() {
			var requiredFields = new string[] {
				null,
				"System.Collections.Generic.Dictionary`2<System.UInt16,System.Type>",
				"System.UInt16",
			};
			var cflowDeobfuscator = new CflowDeobfuscator();
			foreach (var type in module.Types) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;
				requiredFields[0] = type.FullName;
				if (!new FieldTypes(type).Exactly(requiredFields))
					continue;

				cflowDeobfuscator.Deobfuscate(cctor);
				var handlers = FindVmHandlerTypes(cctor);
				if (handlers.Count != 31)
					continue;

				return handlers;
			}

			return null;
		}

		static List<TypeDef> FindVmHandlerTypes(MethodDef method) {
			var list = new List<TypeDef>();

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var type = instr.Operand as TypeDef;
				if (type == null)
					continue;

				list.Add(type);
			}

			return list;
		}

		void DetectHandlers(List<TypeDef> handlerTypes, CsvmInfo csvmInfo) {
			opCodeHandlers = new List<OpCodeHandler>();
			var detected = new List<OpCodeHandler>();

			foreach (var handlersList in OpCodeHandlers.Handlers) {
				opCodeHandlers.Clear();

				foreach (var handlerType in handlerTypes) {
					var info = new UnknownHandlerInfo(handlerType, csvmInfo);
					detected.Clear();
					foreach (var opCodeHandler in handlersList) {
						if (opCodeHandler.Detect(info))
							detected.Add(opCodeHandler);
					}
					if (detected.Count != 1)
						goto next;
					opCodeHandlers.Add(detected[0]);
				}
				if (new List<OpCodeHandler>(Utils.Unique(opCodeHandlers)).Count == opCodeHandlers.Count)
					return;
next: ;
			}
			throw new ApplicationException("Could not detect all VM opcode handlers");
		}
	}
}
