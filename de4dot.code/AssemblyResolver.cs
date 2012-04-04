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
using System.Text.RegularExpressions;
using System.Xml;
using Mono.Cecil;

namespace de4dot.code {
	public class AssemblyResolver : DefaultAssemblyResolver {
		public static readonly AssemblyResolver Instance = new AssemblyResolver();
		Dictionary<string, bool> addedDirectories = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		static AssemblyResolver() {
			Instance.resetSearchPaths();
		}

		void resetSearchPaths() {
			addedDirectories.Clear();
			addOtherAssemblySearchPaths();
		}

		void addOtherAssemblySearchPaths() {
			addOtherAssemblySearchPaths(Environment.GetEnvironmentVariable("ProgramFiles"));
			addOtherAssemblySearchPaths(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
		}

		void addOtherAssemblySearchPaths(string path) {
			if (string.IsNullOrEmpty(path))
				return;
			addSilverlightDirs(Path.Combine(path, @"Microsoft Silverlight"));
			addIfExists(path, @"Microsoft SDKs\Silverlight\v2.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v2.0\Libraries\Server");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v2.0\Reference Assemblies");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Server");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Server");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v5.0\Libraries\Client");
			addIfExists(path, @"Microsoft SDKs\Silverlight\v5.0\Libraries\Server");
			addIfExists(path, @"Microsoft.NET\SDK\CompactFramework\v2.0\WindowsCE");
			addIfExists(path, @"Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE");
			addIfExists(path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v3.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v4.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v5.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\FSharp\2.0\Runtime\v2.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\FSharp\2.0\Runtime\v4.0");
			addIfExists(path, @"Reference Assemblies\Microsoft\WindowsPowerShell\v1.0");
			addIfExists(path, @"Microsoft Visual Studio .NET\Common7\IDE\PublicAssemblies");
			addIfExists(path, @"Microsoft Visual Studio .NET\Common7\IDE\PrivateAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 8.0\Common7\IDE\PublicAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 8.0\Common7\IDE\PrivateAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 9.0\Common7\IDE\PublicAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 9.0\Common7\IDE\PrivateAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 10.0\Common7\IDE\PublicAssemblies");
			addIfExists(path, @"Microsoft Visual Studio 10.0\Common7\IDE\PrivateAssemblies");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v2.0\References\Windows\x86");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v2.0\References\Xbox360");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Windows\x86");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Xbox360");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Zune");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Windows\x86");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Xbox360");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Zune");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v4.0\References\Windows\x86");
			addIfExists(path, @"Microsoft XNA\XNA Game Studio\v4.0\References\Xbox360");
			addIfExists(path, @"Windows CE Tools\wce500\Windows Mobile 5.0 Pocket PC SDK\Designtimereferences");
			addIfExists(path, @"Windows CE Tools\wce500\Windows Mobile 5.0 Smartphone SDK\Designtimereferences");
			addIfExists(path, @"Windows Mobile 5.0 SDK R2\Managed Libraries");
			addIfExists(path, @"Windows Mobile 6 SDK\Managed Libraries");
			addIfExists(path, @"Windows Mobile 6.5.3 DTK\Managed Libraries");
		}

		// basePath is eg. "C:\Program Files (x86)\Microsoft Silverlight"
		void addSilverlightDirs(string basePath) {
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

		void addIfExists(string basePath, string extraPath) {
			try {
				var path = Path.Combine(basePath, extraPath);
				if (Utils.pathExists(path))
					Instance.addSearchDirectory(path);
			}
			catch (Exception) {
			}
		}

		public void addSearchDirectory(string dir) {
			if (!addedDirectories.ContainsKey(dir)) {
				addedDirectories[dir] = true;
				AddSearchDirectory(dir);
			}
		}

		public void addModule(ModuleDefinition module) {
			if (module.FullyQualifiedName != "") {
				addSearchDirectory(Path.GetDirectoryName(module.FullyQualifiedName));
				if (module.FullyQualifiedName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
					addConfigFile(module.FullyQualifiedName + ".config");
			}

			var assembly = module.Assembly;
			if (assembly != null) {
				var name = assembly.Name.FullName;
				cache[name] = assembly;
			}
		}

		void addConfigFile(string configFilename) {
			var dirName = Utils.getDirName(Utils.getFullPath(configFilename));
			addSearchDirectory(dirName);

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
							addSearchDirectory(Path.Combine(dirName, path));
					}
				}
			}
			catch (IOException) {
			}
			catch (XmlException) {
			}
		}

		public void removeModule(ModuleDefinition module) {
			var assembly = module.Assembly;
			if (assembly == null)
				return;

			removeModule(assembly.Name.FullName);
		}

		public void removeModule(string asmFullName) {
			if (string.IsNullOrEmpty(asmFullName))
				return;
			cache.Remove(asmFullName);
		}

		public void clearAll() {
			cache.Clear();
			resetSearchPaths();
		}
	}
}
