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

namespace de4dot.code.deobfuscators.CodeVeil {
	// Detects the type CV adds to the assembly that gets called from <Module>::.cctor.
	class MainType {
		ModuleDefMD module;
		TypeDef theType;
		MethodDef initMethod;
		MethodDef tamperCheckMethod;
		ObfuscatorVersion obfuscatorVersion = ObfuscatorVersion.Unknown;
		List<uint> rvas = new List<uint>();	// _stub and _executive
		List<MethodDef> otherInitMethods = new List<MethodDef>();

		public bool Detected {
			get { return theType != null; }
		}

		public ObfuscatorVersion Version {
			get { return obfuscatorVersion; }
		}

		public TypeDef Type {
			get { return theType; }
		}

		public MethodDef InitMethod {
			get { return initMethod; }
		}

		public List<MethodDef> OtherInitMethods {
			get { return otherInitMethods; }
		}

		public MethodDef TamperCheckMethod {
			get { return tamperCheckMethod; }
		}

		public List<uint> Rvas {
			get { return rvas; }
		}

		public MainType(ModuleDefMD module) {
			this.module = module;
		}

		public MainType(ModuleDefMD module, MainType oldOne) {
			this.module = module;
			this.theType = Lookup(oldOne.theType, "Could not find main type");
			this.initMethod = Lookup(oldOne.initMethod, "Could not find main type init method");
			this.tamperCheckMethod = Lookup(oldOne.tamperCheckMethod, "Could not find tamper detection method");
			this.obfuscatorVersion = oldOne.obfuscatorVersion;
			this.rvas = oldOne.rvas;
			foreach (var otherInitMethod in otherInitMethods)
				otherInitMethods.Add(Lookup(otherInitMethod, "Could not find otherInitMethod"));
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.Lookup(module, def, errorMessage);
		}

		public void Find() {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4())
					continue;

				var ldci4_2 = instrs[i + 1];
				if (!ldci4_2.IsLdcI4())
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var initMethodTmp = call.Operand as MethodDef;
				ObfuscatorVersion obfuscatorVersionTmp;
				if (!CheckInitMethod(initMethodTmp, out obfuscatorVersionTmp))
					continue;
				if (!CheckMethodsType(initMethodTmp.DeclaringType))
					continue;

				obfuscatorVersion = obfuscatorVersionTmp;
				theType = initMethodTmp.DeclaringType;
				initMethod = initMethodTmp;
				break;
			}
		}

		static string[] fieldTypesV5 = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.List`1<System.Delegate>",
			"System.Runtime.InteropServices.GCHandle",
		};
		bool CheckInitMethod(MethodDef initMethod, out ObfuscatorVersion obfuscatorVersionTmp) {
			obfuscatorVersionTmp = ObfuscatorVersion.Unknown;

			if (initMethod == null)
				return false;
			if (initMethod.Body == null)
				return false;
			if (!initMethod.IsStatic)
				return false;
			if (!DotNetUtils.IsMethod(initMethod, "System.Void", "(System.Boolean,System.Boolean)"))
				return false;

			if (HasCodeString(initMethod, "E_FullTrust")) {
				if (DotNetUtils.GetPInvokeMethod(initMethod.DeclaringType, "user32", "CallWindowProcW") != null)
					obfuscatorVersionTmp = ObfuscatorVersion.V4_1;
				else
					obfuscatorVersionTmp = ObfuscatorVersion.V4_0;
			}
			else if (HasCodeString(initMethod, "Full Trust Required"))
				obfuscatorVersionTmp = ObfuscatorVersion.V3;
			else if (initMethod.DeclaringType.HasNestedTypes && new FieldTypes(initMethod.DeclaringType).All(fieldTypesV5))
				obfuscatorVersionTmp = ObfuscatorVersion.V5_0;
			else
				return false;

			return true;
		}

		static bool HasCodeString(MethodDef method, string str) {
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				if (s == str)
					return true;
			}
			return false;
		}

		bool CheckMethodsType(TypeDef type) {
			rvas = new List<uint>();

			var fields = GetRvaFields(type);
			if (fields.Count < 2)	// RVAs for executive and stub are always present if encrypted methods
				return true;

			foreach (var field in fields)
				rvas.Add((uint)field.RVA);
			return true;
		}

		static List<FieldDef> GetRvaFields(TypeDef type) {
			var fields = new List<FieldDef>();
			foreach (var field in type.Fields) {
				var etype = field.FieldSig.GetFieldType().GetElementType();
				if (etype != ElementType.U1 && etype != ElementType.U4)
					continue;
				if (field.RVA == 0)
					continue;

				fields.Add(field);
			}
			return fields;
		}

		public void Initialize() {
			if (theType == null)
				return;

			tamperCheckMethod = FindTamperCheckMethod();
			otherInitMethods = FindOtherInitMethods();
		}

		MethodDef FindTamperCheckMethod() {
			foreach (var method in theType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.Reflection.Assembly,System.UInt64)"))
					continue;

				return method;
			}

			return null;
		}

		List<MethodDef> FindOtherInitMethods() {
			var list = new List<MethodDef>();
			foreach (var method in theType.Methods) {
				if (!method.IsStatic)
					continue;
				if (method.Name == ".cctor")
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;

				list.Add(method);
			}
			return list;
		}

		public MethodDef GetInitStringDecrypterMethod(MethodDef stringDecrypterInitMethod) {
			if (stringDecrypterInitMethod == null)
				return null;
			if (theType == null)
				return null;

			foreach (var method in theType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (CallsMethod(method, stringDecrypterInitMethod))
					return method;
			}
			return null;
		}

		bool CallsMethod(MethodDef methodToCheck, MethodDef calledMethod) {
			foreach (var method in DotNetUtils.GetCalledMethods(module, methodToCheck)) {
				if (method == calledMethod)
					return true;
			}
			return false;
		}

		public void RemoveInitCall(Blocks blocks) {
			if (initMethod == null || theType == null)
				return;
			if (blocks.Method.Name != ".cctor")
				return;
			if (blocks.Method.DeclaringType != DotNetUtils.GetModuleType(module))
				return;

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					if (!instrs[i].IsLdcI4())
						continue;
					if (!instrs[i + 1].IsLdcI4())
						continue;
					var call = instrs[i + 2];
					if (call.OpCode.Code != Code.Call)
						continue;
					if (call.Operand != initMethod)
						continue;

					block.Remove(i, 3);
					return;
				}
			}
		}
	}
}
