/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	static class CoUtils {
		public static EmbeddedResource GetResource(ModuleDefMD module, MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			return GetResource(module, DotNetUtils.GetCodeStrings(method));
		}

		public static EmbeddedResource GetResource(ModuleDefMD module, IEnumerable<string> names) {
			foreach (var name in names) {
				var resource = DotNetUtils.GetResource(module, name) as EmbeddedResource;
				if (resource != null)
					return resource;
				try {
					resource = DotNetUtils.GetResource(module, Encoding.UTF8.GetString(Convert.FromBase64String(name))) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
				catch {
				}
			}
			return null;
		}
	}
}
