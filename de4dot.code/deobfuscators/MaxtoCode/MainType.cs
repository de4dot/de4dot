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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MaxtoCode {
	class MainType {
		ModuleDefMD module;
		TypeDef mcType;
		bool isOld;
		ModuleRef runtimeModule1, runtimeModule2;

		public bool IsOld => isOld;
		public TypeDef Type => mcType;

		public IEnumerable<MethodDef> InitMethods {
			get {
				var list = new List<MethodDef>();
				if (mcType == null)
					return list;
				foreach (var method in mcType.Methods) {
					if (method.IsStatic && DotNetUtils.IsMethod(method, "System.Void", "()"))
						list.Add(method);
				}
				return list;
			}
		}

		public IEnumerable<ModuleRef> RuntimeModuleRefs {
			get {
				if (runtimeModule1 != null)
					yield return runtimeModule1;
				if (runtimeModule2 != null)
					yield return runtimeModule2;
			}
		}

		public bool Detected => mcType != null;
		public MainType(ModuleDefMD module) => this.module = module;

		public MainType(ModuleDefMD module, MainType oldOne) {
			this.module = module;
			mcType = Lookup(oldOne.mcType, "Could not find main type");
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken =>
			DeobUtils.Lookup(module, def, errorMessage);

		public void Find() {
			foreach (var cctor in DeobUtils.GetInitCctors(module, 3)) {
				if (CheckCctor(cctor))
					break;
			}
		}

		bool CheckCctor(MethodDef cctor) {
			foreach (var method in DotNetUtils.GetCalledMethods(module, cctor)) {
				if (method.Name != "Startup")
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;

				if (!CheckType(method.DeclaringType, out runtimeModule1, out runtimeModule2, out bool isOldTmp))
					continue;

				mcType = method.DeclaringType;
				isOld = isOldTmp;
				return true;
			}

			return false;
		}

		static bool CheckType(TypeDef type, out ModuleRef module1, out ModuleRef module2, out bool isOld) {
			module1 = module2 = null;
			isOld = false;

			if (type.FindMethod("Startup") == null)
				return false;

			var pinvokes = GetPinvokes(type);
			var pinvokeList = GetPinvokeList(pinvokes, "CheckRuntime");
			if (pinvokeList == null)
				return false;
			if (GetPinvokeList(pinvokes, "MainDLL") == null)
				return false;

			// Newer versions (3.4+ ???) also have GetModuleBase()
			isOld = GetPinvokeList(pinvokes, "GetModuleBase") == null;

			module1 = pinvokeList[0].ImplMap.Module;
			module2 = pinvokeList[1].ImplMap.Module;
			return true;
		}

		static Dictionary<string, List<MethodDef>> GetPinvokes(TypeDef type) {
			var pinvokes = new Dictionary<string, List<MethodDef>>(StringComparer.Ordinal);
			foreach (var method in type.Methods) {
				var info = method.ImplMap;
				if (info == null || UTF8String.IsNullOrEmpty(info.Name))
					continue;
				if (!pinvokes.TryGetValue(info.Name.String, out var list))
					pinvokes[info.Name.String] = list = new List<MethodDef>();
				list.Add(method);
			}
			return pinvokes;
		}

		static List<MethodDef> GetPinvokeList(Dictionary<string, List<MethodDef>> pinvokes, string methodName) {
			if (!pinvokes.TryGetValue(methodName, out var list))
				return null;
			if (list.Count != 2)
				return null;
			return list;
		}
	}
}
