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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MaxtoCode {
	class MainType {
		ModuleDefinition module;
		TypeDefinition mcType;
		ModuleReference mcModule1, mcModule2;
		bool isOld;

		public bool IsOld {
			get { return isOld; }
		}

		public TypeDefinition Type {
			get { return mcType; }
		}

		public IEnumerable<ModuleReference> ModuleReferences {
			get {
				var list = new List<ModuleReference>();
				if (mcModule1 != null)
					list.Add(mcModule1);
				if (mcModule2 != null)
					list.Add(mcModule2);
				return list;
			}
		}

		public IEnumerable<MethodDefinition> InitMethods {
			get {
				var list = new List<MethodDefinition>();
				if (mcType == null)
					return list;
				foreach (var method in mcType.Methods) {
					if (method.IsStatic && DotNetUtils.isMethod(method, "System.Void", "()"))
						list.Add(method);
				}
				return list;
			}
		}

		public bool Detected {
			get { return mcType != null; }
		}

		public MainType(ModuleDefinition module) {
			this.module = module;
		}

		public MainType(ModuleDefinition module, MainType oldOne) {
			this.module = module;
			this.mcType = lookup(oldOne.mcType, "Could not find main type");
			this.mcModule1 = DeobUtils.lookup(module, oldOne.mcModule1, "Could not find MC runtime module ref #1");
			this.mcModule2 = DeobUtils.lookup(module, oldOne.mcModule2, "Could not find MC runtime module ref #2");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			foreach (var cctor in DeobUtils.getInitCctors(module, 3)) {
				if (checkCctor(cctor))
					break;
			}
		}

		bool checkCctor(MethodDefinition cctor) {
			foreach (var method in DotNetUtils.getCalledMethods(module, cctor)) {
				if (method.Name != "Startup")
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;

				ModuleReference module1, module2;
				bool isOldTmp;
				if (!checkType(method.DeclaringType, out module1, out module2, out isOldTmp))
					continue;

				mcType = method.DeclaringType;
				mcModule1 = module1;
				mcModule2 = module2;
				isOld = isOldTmp;
				return true;
			}

			return false;
		}

		static bool checkType(TypeDefinition type, out ModuleReference module1, out ModuleReference module2, out bool isOld) {
			module1 = module2 = null;
			isOld = false;

			if (DotNetUtils.getMethod(type, "Startup") == null)
				return false;

			var pinvokes = getPinvokes(type);
			var pinvokeList = getPinvokeList(pinvokes, "CheckRuntime");
			if (pinvokeList == null)
				return false;
			if (getPinvokeList(pinvokes, "MainDLL") == null)
				return false;

			// Newer versions (3.4+ ???) also have GetModuleBase()
			isOld = getPinvokeList(pinvokes, "GetModuleBase") == null;

			module1 = pinvokeList[0].PInvokeInfo.Module;
			module2 = pinvokeList[1].PInvokeInfo.Module;
			return true;
		}

		static Dictionary<string, List<MethodDefinition>> getPinvokes(TypeDefinition type) {
			var pinvokes = new Dictionary<string, List<MethodDefinition>>(StringComparer.Ordinal);
			foreach (var method in type.Methods) {
				var info = method.PInvokeInfo;
				if (info == null || info.EntryPoint == null)
					continue;
				List<MethodDefinition> list;
				if (!pinvokes.TryGetValue(info.EntryPoint, out list))
					pinvokes[info.EntryPoint] = list = new List<MethodDefinition>();
				list.Add(method);
			}
			return pinvokes;
		}

		static List<MethodDefinition> getPinvokeList(Dictionary<string, List<MethodDefinition>> pinvokes, string methodName) {
			List<MethodDefinition> list;
			if (!pinvokes.TryGetValue(methodName, out list))
				return null;
			if (list.Count != 2)
				return null;
			return list;
		}
	}
}
