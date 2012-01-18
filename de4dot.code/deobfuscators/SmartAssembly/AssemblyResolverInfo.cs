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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	public class EmbeddedAssemblyInfo {
		public string assemblyName;
		public string simpleName;
		public string resourceName;
		public EmbeddedResource resource;
		public bool isCompressed = false;
		public bool isTempFile = false;
		public string flags = "";

		public override string ToString() {
			return assemblyName ?? base.ToString();
		}

		public static EmbeddedAssemblyInfo create(ModuleDefinition module, string encName, string rsrcName) {
			var info = new EmbeddedAssemblyInfo();

			try {
				if (encName == "" || Convert.ToBase64String(Convert.FromBase64String(encName)) != encName)
					return null;
			}
			catch (FormatException) {
				return null;
			}

			if (rsrcName.Length > 0 && rsrcName[0] == '[') {
				int i = rsrcName.IndexOf(']');
				if (i < 0)
					return null;
				info.flags = rsrcName.Substring(1, i - 1);
				info.isTempFile = info.flags.IndexOf('t') >= 0;
				info.isCompressed = info.flags.IndexOf('z') >= 0;
				rsrcName = rsrcName.Substring(i + 1);
			}
			if (rsrcName == "")
				return null;

			info.assemblyName = Encoding.UTF8.GetString(Convert.FromBase64String(encName));
			info.resourceName = rsrcName;
			info.resource = DotNetUtils.getResource(module, rsrcName) as EmbeddedResource;
			info.simpleName = Utils.getAssemblySimpleName(info.assemblyName);

			return info;
		}
	}

	class AssemblyResolverInfo : ResolverInfoBase {
		MethodDefinition simpleZipTypeMethod;
		List<EmbeddedAssemblyInfo> embeddedAssemblyInfos = new List<EmbeddedAssemblyInfo>();

		public MethodDefinition SimpleZipTypeMethod {
			get { return simpleZipTypeMethod; }
		}

		public IList<EmbeddedAssemblyInfo> EmbeddedAssemblyInfos {
			get { return embeddedAssemblyInfos; }
		}

		public AssemblyResolverInfo(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		public bool resolveResources() {
			bool ok = true;

			foreach (var info in embeddedAssemblyInfos) {
				if (info.resource != null)
					continue;
				info.resource = DotNetUtils.getResource(module, info.resourceName) as EmbeddedResource;
				if (info.resource == null)
					ok = false;
			}

			return ok;
		}

		protected override bool checkResolverType(TypeDefinition type) {
			if (DotNetUtils.findFieldType(type, "System.Collections.Hashtable", true) != null)
				return true;

			foreach (var field in type.Fields) {
				if (DotNetUtils.derivesFromDelegate(DotNetUtils.getType(module, field.FieldType)))
					continue;
				if (field.IsLiteral && field.FieldType.ToString() == "System.String")
					continue;
				return false;
			}
			return true;
		}

		protected override bool checkHandlerMethod(MethodDefinition method) {
			if (!method.IsStatic || !method.HasBody)
				return false;

			var infos = new List<EmbeddedAssemblyInfo>();
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				if (string.IsNullOrEmpty(s))
					continue;
				if (!initInfos(infos, s.Split(',')))
					continue;

				embeddedAssemblyInfos = infos;
				findSimpleZipType(method);
				return true;
			}

			return false;
		}

		bool initInfos(IList<EmbeddedAssemblyInfo> list, string[] strings) {
			list.Clear();
			if (strings.Length % 2 == 1)
				return false;

			for (int i = 0; i < strings.Length; i += 2) {
				var info = EmbeddedAssemblyInfo.create(module, strings[i], strings[i + 1]);
				if (info == null)
					return false;
				list.Add(info);
			}

			Log.v("Found embedded assemblies:");
			Log.indent();
			foreach (var info in list)
				Log.v("{0}", info.assemblyName);
			Log.deIndent();

			return true;
		}

		void findSimpleZipType(MethodDefinition method) {
			if (method == null || !method.HasBody)
				return;
			foreach (var call in method.Body.Instructions) {
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (!SimpleZipInfo.isSimpleZipDecryptMethod_QuickCheck(module, calledMethod, out simpleZipTypeMethod))
					continue;

				return;
			}
		}

		public EmbeddedAssemblyInfo find(string simpleName) {
			foreach (var info in embeddedAssemblyInfos) {
				if (info.simpleName == simpleName)
					return info;
			}

			return null;
		}

		public bool removeEmbeddedAssemblyInfo(EmbeddedAssemblyInfo info) {
			bool removed = false;
			for (int i = 0; i < EmbeddedAssemblyInfos.Count; i++) {
				var other = EmbeddedAssemblyInfos[i];
				if (info.simpleName == other.simpleName) {
					EmbeddedAssemblyInfos.RemoveAt(i--);
					removed = true;
				}
			}
			return removed;
		}
	}
}
