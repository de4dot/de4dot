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

namespace de4dot.code.deobfuscators.ILProtector {
	class MainType {
		ModuleDefMD module;
		List<RuntimeFileInfo> runtimeFileInfos;
		TypeDef invokerDelegate;
		FieldDef invokerInstanceField;

		public List<RuntimeFileInfo> RuntimeFileInfos => runtimeFileInfos;
		public TypeDef InvokerDelegate => invokerDelegate;
		public FieldDef InvokerInstanceField => invokerInstanceField;
		public bool Detected => runtimeFileInfos != null;
		public MainType(ModuleDefMD module) => this.module = module;
		public void Find() => CheckMethod(DotNetUtils.GetModuleTypeCctor(module));

		static string[] ilpLocalsV1x = new string[] {
			"System.Boolean",
			"System.IntPtr",
			"System.Object[]",
		};
		static string[] ilpLocalsV2x = new string[] {
			"System.IntPtr",
		};
		bool CheckMethod(MethodDef cctor) {
			if (cctor == null || cctor.Body == null)
				return false;
			var localTypes = new LocalTypes(cctor);
			if (!localTypes.Exactly(ilpLocalsV1x) &&
				!localTypes.Exactly(ilpLocalsV2x))
				return false;

			var type = cctor.DeclaringType;
			var methods = GetPinvokeMethods(type, "Protect");
			if (methods.Count == 0)
				methods = GetPinvokeMethods(type, "P0");
			if (methods.Count != 2)
				return false;
			if (type.Fields.Count < 1 || type.Fields.Count > 2)
				return false;

			if (!GetDelegate(type, out invokerInstanceField, out invokerDelegate))
				return false;

			runtimeFileInfos = new List<RuntimeFileInfo>(methods.Count);
			foreach (var method in methods)
				runtimeFileInfos.Add(new RuntimeFileInfo(method));
			return true;
		}

		bool GetDelegate(TypeDef type, out FieldDef field, out TypeDef del) {
			foreach (var fld in type.Fields) {
				var theDelegate = fld.FieldType.TryGetTypeDef();
				if (theDelegate != null && DotNetUtils.DerivesFromDelegate(theDelegate)) {
					field = fld;
					del = theDelegate;
					return true;
				}
			}

			field = null;
			del = null;
			return false;
		}

		static List<MethodDef> GetPinvokeMethods(TypeDef type, string name) {
			var list = new List<MethodDef>();
			foreach (var method in type.Methods) {
				if (method.ImplMap != null && method.ImplMap.Name == name)
					list.Add(method);
			}
			return list;
		}

		public string GetRuntimeVersionString() {
			if (runtimeFileInfos == null)
				return null;
			foreach (var info in runtimeFileInfos) {
				var version = info.GetVersion();
				if (version != null)
					return version.ToString();
			}
			return null;
		}

		public void CleanUp() {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
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
