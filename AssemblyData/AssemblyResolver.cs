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
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace AssemblyData {
	class AssemblyResolver {
		Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
		Dictionary<string, bool> assemblySearchPathsDict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		List<string> assemblySearchPaths = new List<string>();

		public AssemblyResolver() => AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;

		void AddAssemblySearchPath(string path) {
			if (assemblySearchPathsDict.ContainsKey(path))
				return;
			assemblySearchPathsDict[path] = true;
			assemblySearchPaths.Add(path);
		}

		Assembly Get(string assemblyFullName) {
			var asmName = new AssemblyName(assemblyFullName);

			if (assemblies.TryGetValue(asmName.FullName, out var assembly))
				return assembly;
			if (assemblies.TryGetValue(asmName.Name, out assembly))
				return assembly;

			return null;
		}

		static string[] assemblyExtensions = new string[] { ".dll", ".exe" };
		Assembly AssemblyResolve(object sender, ResolveEventArgs args) {
			var assembly = Get(args.Name);
			if (assembly != null)
				return assembly;

			var asmName = new AssemblyName(args.Name);
			foreach (var path in assemblySearchPaths) {
				foreach (var ext in assemblyExtensions) {
					try {
						var filename = Path.Combine(path, asmName.Name + ext);
						if (!new FileInfo(filename).Exists)
							continue;
						AddConfigFile(filename + ".config");
						return AddAssembly(Assembly.LoadFile(filename));
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

		public Assembly Load(string filename) {
			AddConfigFile(filename + ".config");
			return AddAssembly(LoadFile(filename));
		}

		Assembly LoadFile(string filename) {
			try {
				return Assembly.LoadFrom(filename);
			}
			catch (FileLoadException) {
				// Here if eg. strong name signature validation failed and possibly other errors
				return Assembly.Load(File.ReadAllBytes(filename));
			}
		}

		Assembly AddAssembly(Assembly assembly) {
			var asmName = assembly.GetName();
			assemblies[asmName.FullName] = assembly;
			assemblies[asmName.Name] = assembly;
			return assembly;
		}

		void AddConfigFile(string configFilename) {
			var dirName = Utils.GetDirName(Utils.GetFullPath(configFilename));
			AddAssemblySearchPath(dirName);

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
							AddAssemblySearchPath(Path.Combine(dirName, path));
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
