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
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace AssemblyData {
	class AssemblyResolver {
		Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
		Dictionary<string, bool> assemblySearchPathsDict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		List<string> assemblySearchPaths = new List<string>();

		public AssemblyResolver() {
			AppDomain.CurrentDomain.AssemblyResolve += assemblyResolve;
		}

		void addAssemblySearchPath(string path) {
			if (assemblySearchPathsDict.ContainsKey(path))
				return;
			assemblySearchPathsDict[path] = true;
			assemblySearchPaths.Add(path);
		}

		Assembly get(string assemblyFullName) {
			var asmName = new AssemblyName(assemblyFullName);

			Assembly assembly;
			if (assemblies.TryGetValue(asmName.FullName, out assembly))
				return assembly;
			if (assemblies.TryGetValue(asmName.Name, out assembly))
				return assembly;

			return null;
		}

		static string[] assemblyExtensions = new string[] { ".dll", ".exe" };
		Assembly assemblyResolve(object sender, ResolveEventArgs args) {
			var assembly = get(args.Name);
			if (assembly != null)
				return assembly;

			var asmName = new AssemblyName(args.Name);
			foreach (var path in assemblySearchPaths) {
				foreach (var ext in assemblyExtensions) {
					try {
						var filename = Path.Combine(path, asmName.Name + ext);
						if (!new FileInfo(filename).Exists)
							continue;
						addConfigFile(filename + ".config");
						return addAssembly(Assembly.LoadFile(filename));
					}
					catch (IOException) {
					}
					catch (BadImageFormatException) {
					}
					catch (ArgumentException) {
					}
					catch (NotSupportedException) {
					}
					catch (UnauthorizedAccessException) {
					}
					catch (System.Security.SecurityException) {
					}
				}
			}

			return null;
		}

		public Assembly load(string filename) {
			addConfigFile(filename + ".config");
			return addAssembly(loadFile(filename));
		}

		Assembly loadFile(string filename) {
			try {
				return Assembly.LoadFrom(filename);
			}
			catch (FileLoadException) {
				// Here if eg. strong name signature validation failed and possibly other errors
				return Assembly.Load(File.ReadAllBytes(filename));
			}
		}

		Assembly addAssembly(Assembly assembly) {
			var asmName = assembly.GetName();
			assemblies[asmName.FullName] = assembly;
			assemblies[asmName.Name] = assembly;
			return assembly;
		}

		void addConfigFile(string configFilename) {
			var dirName = Utils.getDirName(Utils.getFullPath(configFilename));
			addAssemblySearchPath(dirName);

			try {
				using (var xmlStream = new FileStream(configFilename, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					var doc = new XmlDocument();
					doc.Load(XmlReader.Create(xmlStream));
					foreach (var tmp in doc.GetElementsByTagName("probing")) {
						var probingElem = tmp as XmlElement;
						if (probingElem == null)
							continue;
						var privatePath = probingElem.GetAttribute("privatePath");
						if (string.IsNullOrEmpty(privatePath))
							continue;
						foreach (var path in privatePath.Split(';'))
							addAssemblySearchPath(Path.Combine(dirName, path));
					}
				}
			}
			catch (IOException) {
			}
			catch (XmlException) {
			}
		}
	}
}
