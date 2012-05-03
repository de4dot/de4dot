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

using System.Collections.Generic;
using Mono.Cecil;

namespace de4dot.code.deobfuscators.DeepSea {
	static class DsInlinedMethodsFinder {
		public static List<MethodDefinition> find(ModuleDefinition module, IEnumerable<MethodDefinition> notInlinedMethods) {
			var notInlinedMethodsDict = new Dictionary<MethodDefinition, bool>();
			foreach (var method in notInlinedMethods)
				notInlinedMethodsDict[method] = true;

			var inlinedMethods = new List<MethodDefinition>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (!notInlinedMethodsDict.ContainsKey(method) && DsMethodCallInliner.canInline(method))
						inlinedMethods.Add(method);
				}
			}

			return inlinedMethods;
		}
	}
}
