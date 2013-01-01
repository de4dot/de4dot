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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	class MainType {
		ModuleDefMD module;
		List<MethodDef> protectMethods;
		TypeDef invokerDelegate;
		FieldDef invokerInstanceField;

		public IEnumerable<MethodDef> ProtectMethods {
			get { return protectMethods; }
		}

		public TypeDef InvokerDelegate {
			get { return invokerDelegate; }
		}

		public FieldDef InvokerInstanceField {
			get { return invokerInstanceField; }
		}

		public bool Detected {
			get { return protectMethods != null; }
		}

		public MainType(ModuleDefMD module) {
			this.module = module;
		}

		public void find() {
			checkMethod(DotNetUtils.getModuleTypeCctor(module));
		}

		static string[] ilpLocals = new string[] {
			"System.Boolean",
			"System.IntPtr",
			"System.Object[]",
		};
		bool checkMethod(MethodDef cctor) {
			if (cctor == null || cctor.Body == null)
				return false;
			if (!new LocalTypes(cctor).exactly(ilpLocals))
				return false;

			var type = cctor.DeclaringType;
			var methods = getPinvokeMethods(type, "Protect");
			if (methods.Count == 0)
				methods = getPinvokeMethods(type, "P0");
			if (methods.Count != 2)
				return false;
			if (type.Fields.Count != 1)
				return false;

			var theField = type.Fields[0];
			var theDelegate = theField.FieldType.TryGetTypeDef();
			if (theDelegate == null || !DotNetUtils.derivesFromDelegate(theDelegate))
				return false;

			protectMethods = methods;
			invokerDelegate = theDelegate;
			invokerInstanceField = theField;
			return true;
		}

		static List<MethodDef> getPinvokeMethods(TypeDef type, string name) {
			var list = new List<MethodDef>();
			foreach (var method in type.Methods) {
				if (method.ImplMap != null && method.ImplMap.Name == name)
					list.Add(method);
			}
			return list;
		}

		public void cleanUp() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor != null) {
				cctor.Body.InitLocals = false;
				cctor.Body.Variables.Clear();
				cctor.Body.Instructions.Clear();
				cctor.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
				cctor.Body.ExceptionHandlers.Clear();
			}
		}
	}
}
