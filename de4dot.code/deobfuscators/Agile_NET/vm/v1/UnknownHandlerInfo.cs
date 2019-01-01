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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v1 {
	class UnknownHandlerInfo {
		TypeDef type;
		CsvmInfo csvmInfo;
		FieldsInfo fieldsInfo;
		MethodDef readMethod, executeMethod;
		int numStaticMethods, numInstanceMethods, numVirtualMethods, numCtors;
		int executeMethodThrows, executeMethodPops;

		public MethodDef ReadMethod => readMethod;
		public MethodDef ExecuteMethod => executeMethod;
		public int NumStaticMethods => numStaticMethods;
		public int NumInstanceMethods => numInstanceMethods;
		public int NumVirtualMethods => numVirtualMethods;
		public int ExecuteMethodThrows => executeMethodThrows;
		public int ExecuteMethodPops => executeMethodPops;
		public int NumCtors => numCtors;

		public UnknownHandlerInfo(TypeDef type, CsvmInfo csvmInfo) {
			this.type = type;
			this.csvmInfo = csvmInfo;
			fieldsInfo = new FieldsInfo(GetFields(type));
			CountMethods();
			FindOverrideMethods();
			executeMethodThrows = CountThrows(executeMethod);
			executeMethodPops = CountPops(executeMethod);
		}

		static internal IEnumerable<FieldDef> GetFields(TypeDef type) {
			var typeFields = new FieldDefAndDeclaringTypeDict<FieldDef>();
			foreach (var field in type.Fields)
				typeFields.Add(field, field);
			var realFields = new Dictionary<FieldDef, bool>();
			foreach (var method in type.Methods) {
				if (method.Body == null)
					continue;
				foreach (var instr in method.Body.Instructions) {
					var fieldRef = instr.Operand as IField;
					if (fieldRef == null)
						continue;
					var field = typeFields.Find(fieldRef);
					if (field == null)
						continue;
					realFields[field] = true;
				}
			}
			return realFields.Keys;
		}

		void CountMethods() {
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
				}
				else if (method.Name == ".ctor")
					numCtors++;
				else if (method.IsStatic)
					numStaticMethods++;
				else if (method.IsVirtual)
					numVirtualMethods++;
				else
					numInstanceMethods++;
			}
		}

		void FindOverrideMethods() {
			foreach (var method in type.Methods) {
				if (!method.IsVirtual)
					continue;
				if (DotNetUtils.IsMethod(method, "System.Void", "(System.IO.BinaryReader)")) {
					if (readMethod != null)
						throw new ApplicationException("Found another read method");
					readMethod = method;
				}
				else if (!DotNetUtils.HasReturnValue(method) && method.MethodSig.GetParamCount() == 1) {
					if (executeMethod != null)
						throw new ApplicationException("Found another execute method");
					executeMethod = method;
				}
			}

			if (readMethod == null)
				throw new ApplicationException("Could not find read method");
			if (executeMethod == null)
				throw new ApplicationException("Could not find execute method");
		}

		static int CountThrows(MethodDef method) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					count++;
			}
			return count;
		}

		int CountPops(MethodDef method) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(calledMethod, csvmInfo.PopMethod))
					continue;

				count++;
			}
			return count;
		}

		public bool HasSameFieldTypes(object[] fieldTypes) => new FieldsInfo(fieldTypes).IsSame(fieldsInfo);
	}
}
