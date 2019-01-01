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
using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace de4dot.code {
	public class TheAssemblyResolver : AssemblyResolver {
		public static readonly TheAssemblyResolver Instance = new TheAssemblyResolver();

		public TheAssemblyResolver() {
			EnableTypeDefCache = true;
			AddOtherSearchPaths(PostSearchPaths);
		}

		public void AddSearchDirectory(string dir) {
			if (!PostSearchPaths.Contains(dir))
				PostSearchPaths.Add(dir);
		}

		public void AddModule(ModuleDef module) => AddToCache(module.Assembly);

		public void RemoveModule(ModuleDef module) {
			var assembly = module.Assembly;
			if (assembly == null)
				return;

			Remove(module.Assembly);
		}

		public void ClearAll() {
			//TODO: cache.Clear();
			//TODO: resetSearchPaths();
		}

		static void AddOtherSearchPaths(IList<string> paths) {
			var dirPF = Environment.GetEnvironmentVariable("ProgramFiles");
			AddOtherAssemblySearchPaths(paths, dirPF);
			var dirPFx86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
			if (!StringComparer.OrdinalIgnoreCase.Equals(dirPF, dirPFx86))
				AddOtherAssemblySearchPaths(paths, dirPFx86);

			var windir = Environment.GetEnvironmentVariable("WINDIR");
			if (!string.IsNullOrEmpty(windir)) {
				AddIfExists(paths, windir, @"Microsoft.NET\Framework\v1.1.4322");
				AddIfExists(paths, windir, @"Microsoft.NET\Framework\v1.0.3705");
			}
		}

		static void AddOtherAssemblySearchPaths(IList<string> paths, string path) {
			if (string.IsNullOrEmpty(path))
				return;
			AddSilverlightDirs(paths, Path.Combine(path, @"Microsoft Silverlight"));
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v2.0\Libraries\Client");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v2.0\Libraries\Server");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v2.0\Reference Assemblies");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Client");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v3.0\Libraries\Server");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Client");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v4.0\Libraries\Server");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v5.0\Libraries\Client");
			AddIfExists(paths, path, @"Microsoft SDKs\Silverlight\v5.0\Libraries\Server");
			AddIfExists(paths, path, @"Microsoft.NET\SDK\CompactFramework\v2.0\WindowsCE");
			AddIfExists(paths, path, @"Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.2");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\Profile\Client");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETFramework\v3.5\Profile\Client");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETCore\v5.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETCore\v4.5.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETCore\v4.5");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETMicroFramework\v3.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETMicroFramework\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETMicroFramework\v4.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETMicroFramework\v4.2");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETMicroFramework\v4.3");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETPortable\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETPortable\v4.5");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETPortable\v4.6");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\.NETPortable\v5.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\v3.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\v3.5");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v3.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\Silverlight\v5.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\WindowsPhone\v8.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\Framework\WindowsPhoneApp\v8.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.259.4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.259.3.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.78.4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.78.3.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.7.4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETCore\3.3.1.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETFramework\v2.0\2.3.0.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.3.0.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.3.1.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.4.0.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETPortable\2.3.5.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETPortable\2.3.5.1");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\.NETPortable\3.47.4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\2.0\Runtime\v2.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\2.0\Runtime\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\3.0\Runtime\.NETPortable");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\3.0\Runtime\v2.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\FSharp\3.0\Runtime\v4.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\WindowsPowerShell\v1.0");
			AddIfExists(paths, path, @"Reference Assemblies\Microsoft\WindowsPowerShell\3.0");
			AddIfExists(paths, path, @"Microsoft Visual Studio .NET\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio .NET\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio .NET 2003\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio .NET 2003\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 8\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 8\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 9.0\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 9.0\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 10.0\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 10.0\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 11.0\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 11.0\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 12.0\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 12.0\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 14.0\Common7\IDE\PublicAssemblies");
			AddIfExists(paths, path, @"Microsoft Visual Studio 14.0\Common7\IDE\PrivateAssemblies");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v2.0\References\Windows\x86");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v2.0\References\Xbox360");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Windows\x86");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Xbox360");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.0\References\Zune");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Windows\x86");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Xbox360");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v3.1\References\Zune");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v4.0\References\Windows\x86");
			AddIfExists(paths, path, @"Microsoft XNA\XNA Game Studio\v4.0\References\Xbox360");
			AddIfExists(paths, path, @"Windows CE Tools\wce500\Windows Mobile 5.0 Pocket PC SDK\Designtimereferences");
			AddIfExists(paths, path, @"Windows CE Tools\wce500\Windows Mobile 5.0 Smartphone SDK\Designtimereferences");
			AddIfExists(paths, path, @"Windows Mobile 5.0 SDK R2\Managed Libraries");
			AddIfExists(paths, path, @"Windows Mobile 6 SDK\Managed Libraries");
			AddIfExists(paths, path, @"Windows Mobile 6.5.3 DTK\Managed Libraries");
			AddIfExists(paths, path, @"Microsoft SQL Server\90\SDK\Assemblies");
			AddIfExists(paths, path, @"Microsoft SQL Server\100\SDK\Assemblies");
			AddIfExists(paths, path, @"Microsoft SQL Server\110\SDK\Assemblies");
			AddIfExists(paths, path, @"Microsoft SQL Server\120\SDK\Assemblies");
			AddIfExists(paths, path, @"Microsoft ASP.NET\ASP.NET MVC 2\Assemblies");
			AddIfExists(paths, path, @"Microsoft ASP.NET\ASP.NET MVC 3\Assemblies");
			AddIfExists(paths, path, @"Microsoft ASP.NET\ASP.NET MVC 4\Assemblies");
			AddIfExists(paths, path, @"Microsoft ASP.NET\ASP.NET Web Pages\v1.0\Assemblies");
			AddIfExists(paths, path, @"Microsoft ASP.NET\ASP.NET Web Pages\v2.0\Assemblies");
			AddIfExists(paths, path, @"Microsoft SDKs\F#\3.0\Framework\v4.0");
		}

		static void AddSilverlightDirs(IList<string> paths, string basePath) {
			if (!Directory.Exists(basePath))
				return;
			try {
				var di = new DirectoryInfo(basePath);
				foreach (var dir in di.GetDirectories()) {
					if (Regex.IsMatch(dir.Name, @"^\d+(?:\.\d+){3}$"))
						AddIfExists(paths, basePath, dir.Name);
				}
			}
			catch {
			}
		}

		static void AddIfExists(IList<string> paths, string basePath, string extraPath) {
			var path = Path.Combine(basePath, extraPath);
			if (Directory.Exists(path))
				paths.Add(path);
		}
	}
}
