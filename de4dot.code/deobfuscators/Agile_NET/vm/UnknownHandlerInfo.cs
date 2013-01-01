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

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	class UnknownHandlerInfo {
		TypeDef type;
		CsvmInfo csvmInfo;
		FieldsInfo fieldsInfo;
		MethodDef readMethod, executeMethod;
		int numStaticMethods, numInstanceMethods, numVirtualMethods, numCtors;
		int executeMethodThrows, executeMethodPops;

		public MethodDef ReadMethod {
			get { return readMethod; }
		}

		public MethodDef ExecuteMethod {
			get { return executeMethod; }
		}

		public int NumStaticMethods {
			get { return numStaticMethods; }
		}

		public int NumInstanceMethods {
			get { return numInstanceMethods; }
		}

		public int NumVirtualMethods {
			get { return numVirtualMethods; }
		}

		public int ExecuteMethodThrows {
			get { return executeMethodThrows; }
		}

		public int ExecuteMethodPops {
			get { return executeMethodPops; }
		}

		public int NumCtors {
			get { return numCtors; }
		}

		public UnknownHandlerInfo(TypeDef type, CsvmInfo csvmInfo) {
			this.type = type;
			this.csvmInfo = csvmInfo;
			fieldsInfo = new FieldsInfo(getFields(type));
			countMethods();
			findOverrideMethods();
			executeMethodThrows = countThrows(executeMethod);
			executeMethodPops = countPops(executeMethod);
		}

		static internal IEnumerable<FieldDef> getFields(TypeDef type) {
			var typeFields = new FieldDefAndDeclaringTypeDict<FieldDef>();
			foreach (var field in type.Fields)
				typeFields.add(field, field);
			var realFields = new Dictionary<FieldDef, bool>();
			foreach (var method in type.Methods) {
				if (method.Body == null)
					continue;
				foreach (var instr in method.Body.Instructions) {
					var fieldRef = instr.Operand as IField;
					if (fieldRef == null)
						continue;
					var field = typeFields.find(fieldRef);
					if (field == null)
						continue;
					realFields[field] = true;
				}
			}
			return realFields.Keys;
		}

		void countMethods() {
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

		void findOverrideMethods() {
			foreach (var method in type.Methods) {
				if (!method.IsVirtual)
					continue;
				if (DotNetUtils.isMethod(method, "System.Void", "(System.IO.BinaryReader)")) {
					if (readMethod != null)
						throw new ApplicationException("Found another read method");
					readMethod = method;
				}
				else if (!DotNetUtils.hasReturnValue(method) && method.MethodSig.GetParamCount() == 1) {
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

		static int countThrows(MethodDef method) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					count++;
			}
			return count;
		}

		int countPops(MethodDef method) {
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

		public bool hasSameFieldTypes(object[] fieldTypes) {
			return new FieldsInfo(fieldTypes).isSame(fieldsInfo);
		}
	}
}
