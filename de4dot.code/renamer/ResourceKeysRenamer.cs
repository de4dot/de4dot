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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using dnlib.DotNet.Resources;

namespace de4dot.code.renamer {
	public class ResourceKeysRenamer {
		const int RESOURCE_KEY_MAX_LEN = 50;
		const string DEFAULT_KEY_NAME = "Key";

		ModuleDefMD module;
		INameChecker nameChecker;
		Dictionary<string, bool> newNames = new Dictionary<string, bool>();

		public ResourceKeysRenamer(ModuleDefMD module, INameChecker nameChecker) {
			this.module = module;
			this.nameChecker = nameChecker;
		}

		public void Rename() {
			Logger.v("Renaming resource keys ({0})", module);
			Logger.Instance.Indent();
			foreach (var type in module.GetTypes()) {
				string resourceName = GetResourceName(type);
				if (resourceName == null)
					continue;
				var resource = GetResource(resourceName);
				if (resource == null) {
					Logger.w("Could not find resource {0}", Utils.RemoveNewlines(resourceName));
					continue;
				}
				Logger.v("Resource: {0}", Utils.ToCsharpString(resource.Name));
				Logger.Instance.Indent();
				Rename(type, resource);
				Logger.Instance.DeIndent();
			}
			Logger.Instance.DeIndent();
		}

		EmbeddedResource GetResource(string resourceName) {
			if (DotNetUtils.GetResource(module, resourceName + ".resources") is EmbeddedResource resource)
				return resource;

			string name = "";
			var pieces = resourceName.Split('.');
			Array.Reverse(pieces);
			foreach (var piece in pieces) {
				name = piece + name;
				resource = DotNetUtils.GetResource(module, name + ".resources") as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		static string GetResourceName(TypeDef type) {
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
						var ctor = instr.Operand as IMethod;
						if (ctor.FullName != "System.Void System.Resources.ResourceManager::.ctor(System.String,System.Reflection.Assembly)")
							continue;
						if (resourceName == null) {
							Logger.w("Could not find resource name");
							continue;
						}

						return resourceName;
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
				foundInCode = false;
			}
			public override string ToString() => $"{element} => {newName}";
		}

		void Rename(TypeDef type, EmbeddedResource resource) {
			newNames.Clear();
			var resourceSet = ResourceReader.Read(module, resource.CreateReader());
			var renamed = new List<RenameInfo>();
			foreach (var elem in resourceSet.ResourceElements) {
				if (nameChecker.IsValidResourceKeyName(elem.Name)) {
					newNames.Add(elem.Name, true);
					continue;
				}

				renamed.Add(new RenameInfo(elem, GetNewName(elem)));
			}

			if (renamed.Count == 0)
				return;

			Rename(type, renamed);

			var outStream = new MemoryStream();
			ResourceWriter.Write(module, outStream, resourceSet);
			var newResource = new EmbeddedResource(resource.Name, outStream.ToArray(), resource.Attributes);
			int resourceIndex = module.Resources.IndexOf(resource);
			if (resourceIndex < 0)
				throw new ApplicationException("Could not find index of resource");
			module.Resources[resourceIndex] = newResource;
		}

		void Rename(TypeDef type, List<RenameInfo> renamed) {
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
					var calledMethod = call.Operand as IMethod;
					if (calledMethod == null)
						continue;

					int ldstrIndex;
					switch (calledMethod.FullName) {
					case "System.String System.Resources.ResourceManager::GetString(System.String,System.Globalization.CultureInfo)":
					case "System.IO.UnmanagedMemoryStream System.Resources.ResourceManager::GetStream(System.String,System.Globalization.CultureInfo)":
					case "System.Object System.Resources.ResourceManager::GetObject(System.String,System.Globalization.CultureInfo)":
						ldstrIndex = i - 2;
						break;

					case "System.String System.Resources.ResourceManager::GetString(System.String)":
					case "System.IO.UnmanagedMemoryStream System.Resources.ResourceManager::GetStream(System.String)":
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
						Logger.w("Could not find string argument to method {0}", calledMethod);
						continue;
					}

					if (!nameToInfo.TryGetValue(name, out var info))
						continue;   // should not be renamed

					ldstr.Operand = info.newName;
					Logger.v("Renamed resource key {0} => {1}", Utils.ToCsharpString(info.element.Name), Utils.ToCsharpString(info.newName));
					info.element.Name = info.newName;
					info.foundInCode = true;
				}
			}

			foreach (var info in renamed) {
				if (!info.foundInCode)
					Logger.w("Could not find resource key {0} in code", Utils.RemoveNewlines(info.element.Name));
			}
		}

		string GetNewName(ResourceElement elem) {
			if (elem.ResourceData.Code != ResourceTypeCode.String)
				return CreateDefaultName();
			var stringData = (BuiltInResourceData)elem.ResourceData;
			var name = CreatePrefixFromStringData((string)stringData.Data);
			return CreateName(counter => counter == 0 ? name : $"{name}_{counter}");
		}

		string CreatePrefixFromStringData(string data) {
			var sb = new StringBuilder();
			data = data.Substring(0, Math.Min(data.Length, 100));
			data = Regex.Replace(data, "[`'\"]", "");
			data = Regex.Replace(data, @"[^\w]+", " ");
			foreach (var piece in data.Split(' ')) {
				if (piece.Length == 0)
					continue;
				var piece2 = piece.Substring(0, 1).ToUpperInvariant() + piece.Substring(1).ToLowerInvariant();
				int maxLen = RESOURCE_KEY_MAX_LEN - sb.Length;
				if (maxLen <= 0)
					break;
				if (piece2.Length > maxLen)
					piece2 = piece2.Substring(0, maxLen);
				sb.Append(piece2);
			}
			if (sb.Length <= 3)
				return CreateDefaultName();
			return sb.ToString();
		}

		string CreateDefaultName() => CreateName(counter => $"{DEFAULT_KEY_NAME}{counter}");

		string CreateName(Func<int, string> create) {
			for (int counter = 0; ; counter++) {
				string newName = create(counter);
				if (!newNames.ContainsKey(newName)) {
					newNames[newName] = true;
					return newName;
				}
			}
		}
	}
}
