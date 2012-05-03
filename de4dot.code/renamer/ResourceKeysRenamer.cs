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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.code.resources;

namespace de4dot.code.renamer {
	class ResourceKeysRenamer {
		ModuleDefinition module;
		INameChecker nameChecker;
		Dictionary<string, bool> newNames = new Dictionary<string, bool>();
		const string DEFAULT_KEY_NAME = "Key";

		public ResourceKeysRenamer(ModuleDefinition module, INameChecker nameChecker) {
			this.module = module;
			this.nameChecker = nameChecker;
		}

		public void rename() {
			Log.v("Renaming resource keys...");
			Log.indent();
			foreach (var type in module.GetTypes()) {
				string resourceName = getResourceName(type);
				if (resourceName == null)
					continue;
				var resource = DotNetUtils.getResource(module, resourceName) as EmbeddedResource;
				if (resource == null) {
					Log.w("Could not find resource {0}", Utils.removeNewlines(resource));
					continue;
				}
				Log.v("Resource: {0}", Utils.toCsharpString(resource.Name));
				Log.indent();
				rename(type, resource);
				Log.deIndent();
			}
			Log.deIndent();
		}

		static string getResourceName(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (method.Body == null)
					continue;
				var instrs = method.Body.Instructions;
				string resourceName = null;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code == Code.Ldstr) {
						resourceName = instr.Operand as string;
						continue;
					}

					if (instr.OpCode.Code == Code.Newobj) {
						var ctor = instr.Operand as MethodReference;
						if (ctor.FullName != "System.Void System.Resources.ResourceManager::.ctor(System.String,System.Reflection.Assembly)")
							continue;
						if (resourceName == null) {
							Log.w("Could not find resource name");
							continue;
						}

						return resourceName + ".resources";
					}
				}
			}
			return null;
		}

		class RenameInfo {
			public readonly ResourceElement element;
			public string newName;
			public bool foundInCode;
			public RenameInfo(ResourceElement element, string newName) {
				this.element = element;
				this.newName = newName;
				this.foundInCode = false;
			}
			public override string ToString() {
				return string.Format("{0} => {1}", element, newName);
			}
		}

		void rename(TypeDefinition type, EmbeddedResource resource) {
			newNames.Clear();
			var resourceSet = ResourceReader.read(module, resource.GetResourceStream());
			var renamed = new List<RenameInfo>();
			foreach (var elem in resourceSet.ResourceElements) {
				if (nameChecker.isValidResourceKeyName(elem.Name))
					continue;

				renamed.Add(new RenameInfo(elem, getNewName(elem)));
			}

			if (renamed.Count == 0)
				return;

			rename(type, renamed);

			var outStream = new MemoryStream();
			ResourceWriter.write(module, outStream, resourceSet);
			outStream.Position = 0;
			var newResource = new EmbeddedResource(resource.Name, resource.Attributes, outStream);
			int resourceIndex = module.Resources.IndexOf(resource);
			if (resourceIndex < 0)
				throw new ApplicationException("Could not find index of resource");
			module.Resources[resourceIndex] = newResource;
		}

		void rename(TypeDefinition type, List<RenameInfo> renamed) {
			var nameToInfo = new Dictionary<string, RenameInfo>(StringComparer.Ordinal);
			foreach (var info in renamed)
				nameToInfo[info.element.Name] = info;

			foreach (var method in type.Methods) {
				if (method.Body == null)
					continue;

				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var calledMethod = call.Operand as MethodReference;
					if (calledMethod == null)
						continue;

					int ldstrIndex;
					switch (calledMethod.FullName) {
					case "System.String System.Resources.ResourceManager::GetString(System.String,System.Globalization.CultureInfo)":
					case "System.IO.UnmanagedMemoryStream System.Resources.ResourceManager::GetString(System.String,System.Globalization.CultureInfo)":
					case "System.Object System.Resources.ResourceManager::GetObject(System.String,System.Globalization.CultureInfo)":
						ldstrIndex = i - 2;
						break;

					case "System.String System.Resources.ResourceManager::GetString(System.String)":
					case "System.IO.UnmanagedMemoryStream System.Resources.ResourceManager::GetString(System.String)":
					case "System.Object System.Resources.ResourceManager::GetObject(System.String)":
						ldstrIndex = i - 1;
						break;

					default:
						continue;
					}

					Instruction ldstr = null;
					string name = null;
					if (ldstrIndex >= 0)
						ldstr = instrs[ldstrIndex];
					if (ldstr == null || (name = ldstr.Operand as string) == null) {
						Log.w("Could not find string argument to method {0}", calledMethod);
						continue;
					}

					RenameInfo info;
					if (!nameToInfo.TryGetValue(name, out info)) {
						Log.w("Could not find resource key '{0}'", Utils.removeNewlines(name));
						continue;
					}

					ldstr.Operand = info.newName;
					Log.v("Renamed resource key {0} => {1}", Utils.toCsharpString(info.element.Name), Utils.toCsharpString(info.newName));
					info.element.Name = info.newName;
					info.foundInCode = true;
				}
			}

			foreach (var info in renamed) {
				if (!info.foundInCode)
					Log.w("Could not find resource key {0} in code", Utils.removeNewlines(info.element.Name));
			}
		}

		string getNewName(ResourceElement elem) {
			if (elem.ResourceData.Code != ResourceTypeCode.String)
				return createDefaultName();
			var stringData = (BuiltInResourceData)elem.ResourceData;
			return createName(createPrefixFromStringData((string)stringData.Data), false);
		}

		string createPrefixFromStringData(string data) {
			const int MAX_LEN = 30;

			var sb = new StringBuilder();
			data = data.Substring(0, Math.Min(data.Length, 100));
			data = Regex.Replace(data, "[`'\"]", "");
			data = Regex.Replace(data, @"[^\w]", " ");
			data = Regex.Replace(data, @"[\s]", " ");
			foreach (var piece in data.Split(' ')) {
				if (piece.Length == 0)
					continue;
				var piece2 = piece.Substring(0, 1).ToUpperInvariant() + piece.Substring(1).ToLowerInvariant();
				int maxLen = MAX_LEN - sb.Length;
				if (maxLen <= 0)
					break;
				if (piece2.Length > maxLen)
					piece2 = piece2.Substring(0, maxLen);
				sb.Append(piece2);
			}
			if (sb.Length <= 3)
				return createDefaultName();
			return sb.ToString();
		}

		string createDefaultName() {
			return createName(DEFAULT_KEY_NAME, true);
		}

		string createName(string prefix, bool useZeroPostfix) {
			for (int counter = 0; ; counter++) {
				string newName;
				if (useZeroPostfix || counter != 0)
					newName = prefix + counter;
				else
					newName = prefix;
				if (!newNames.ContainsKey(newName)) {
					newNames[newName] = true;
					return newName;
				}
			}
		}
	}
}
