/*
    Copyright (C) 2011 de4dot@gmail.com

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
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace de4dot {
	class AssemblyResolver : DefaultAssemblyResolver {
		public static readonly AssemblyResolver Instance = new AssemblyResolver();
		Dictionary<string, bool> addedAssemblies = new Dictionary<string, bool>(StringComparer.Ordinal);
		Dictionary<string, bool> addedDirectories = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		static AssemblyResolver() {
			// Make sure there's only ONE assembly resolver
			GlobalAssemblyResolver.Instance = Instance;
			addSilverlightSearchPaths();
		}

		static void addSilverlightSearchPaths() {
			addSilverlightSearchPaths(Environment.GetEnvironmentVariable("ProgramFiles"));
			addSilverlightSearchPaths(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
		}

		static void addSilverlightSearchPaths(string path) {
			if (string.IsNullOrEmpty(path))
				return;
			addSilverlightDirs(Path.Combine(path, @"Microsoft Silverlight"));
			addIfExists(path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Server");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Server");
			addIfExists(path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v3.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v4.0");
		}

		// basePath is eg. "C:\Program Files (x86)\Microsoft Silverlight"
		static void addSilverlightDirs(string basePath) {
			try {
				var di = new DirectoryInfo(basePath);
				foreach (var dir in di.GetDirectories()) {
					if (Regex.IsMatch(dir.Name, @"^\d+(?:\.\d+){3}$"))
						addIfExists(basePath, dir.Name);
				}
			}
			catch (Exception) {
			}
		}

		static void addIfExists(string basePath, string extraPath) {
			try {
				var path = Path.Combine(basePath, extraPath);
				if (pathExists(path))
					Instance.addSearchDirectory(path);
			}
			catch (Exception) {
			}
		}

		static bool pathExists(string path) {
			try {
				return new DirectoryInfo(path).Exists;
			}
			catch (Exception) {
				return false;
			}
		}

		public void addSearchDirectory(string dir) {
			if (!addedDirectories.ContainsKey(dir)) {
				addedDirectories[dir] = true;
				AddSearchDirectory(dir);
			}
		}

		public void addModule(ModuleDefinition module) {
			var dir = Path.GetDirectoryName(module.FullyQualifiedName);
			addSearchDirectory(dir);

			var assembly = module.Assembly;
			if (assembly != null) {
				var name = assembly.Name.FullName;
				if (!addedAssemblies.ContainsKey(name) && cache.ContainsKey(name))
					throw new ApplicationException(string.Format("Assembly {0} was loaded by other code.", name));
				addedAssemblies[name] = true;
				RegisterAssembly(assembly);
			}
		}

		public void removeModule(ModuleDefinition module) {
			var assembly = module.Assembly;
			if (assembly == null)
				return;

			var name = assembly.Name.FullName;
			addedAssemblies.Remove(name);
			cache.Remove(name);
		}
	}
}
