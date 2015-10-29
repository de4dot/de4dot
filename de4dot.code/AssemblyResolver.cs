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

using dnlib.DotNet;

namespace de4dot.code {
	public class TheAssemblyResolver : AssemblyResolver {
		public static readonly TheAssemblyResolver Instance = new TheAssemblyResolver();

		public TheAssemblyResolver() {
			EnableTypeDefCache = true;
		}

		public void AddSearchDirectory(string dir) {
			if (!PostSearchPaths.Contains(dir))
				PostSearchPaths.Add(dir);
		}

		public void AddModule(ModuleDef module) {
			AddToCache(module.Assembly);
		}

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
	}
}
